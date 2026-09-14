/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Metadata.Audible
{
    internal sealed class AudibleAuthorLookupWorkflow
    {
        private static readonly CompareInfo CompareInfo = CultureInfo.InvariantCulture.CompareInfo;
        private const CompareOptions DiacriticIgnore = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

        private readonly AudibleApiClient _apiClient;
        private readonly AudibleProductSearchWorkflow _productSearchWorkflow;
        private readonly ILogger _logger;

        public AudibleAuthorLookupWorkflow(
            AudibleApiClient apiClient,
            AudibleProductSearchWorkflow productSearchWorkflow,
            ILogger logger)
        {
            _apiClient = apiClient;
            _productSearchWorkflow = productSearchWorkflow;
            _logger = logger;
        }

        /// <summary>
        /// Resolves an author name to the identity that will be cached against it.
        /// Candidate generation stays deliberately loose -- a stored name carrying a title, a
        /// credential or an honorific has to reach the right contributor, and a candidate missed
        /// here is unrecoverable inside the call. Identity binding is where strictness belongs,
        /// because a wrong binding is durable, self-confirming and visible as the wrong face on an
        /// author page. So the candidates are ranked rather than taken in the order Audible
        /// returned them, and the winning ASIN is confirmed against the contributor endpoint by
        /// comparing the contributor's name with the candidate's own name. Both sides of that
        /// comparison are Audible's, which is what makes it safe to be exact there while staying
        /// loose between the library's stored string and the provider's name.
        /// </summary>
        public async Task<AuthorLookupItem?> LookupAuthorAsync(string author, string region)
        {
            if (string.IsNullOrWhiteSpace(author)) return null;

            try
            {
                var candidates = await LookupAuthorItemsAsync(author, region);
                var best = candidates.FirstOrDefault();
                if (best == null)
                {
                    return null;
                }

                if (!RequiresIdentityConfirmation(candidates, best, author.Trim()))
                {
                    // One ASIN in play and the winner is the name that was asked for. There is
                    // nothing to choose between, so the contributor call stays what it has always
                    // been here: enrichment, fired only when image or description are missing.
                    if (!string.IsNullOrWhiteSpace(best.Asin) &&
                        (string.IsNullOrWhiteSpace(best.Image) || string.IsNullOrWhiteSpace(best.Description)))
                    {
                        var detailed = await GetAuthorByAsinAsync(best.Asin, region);
                        if (detailed != null)
                        {
                            return detailed;
                        }
                    }

                    return best;
                }

                foreach (var candidate in candidates)
                {
                    if (string.IsNullOrWhiteSpace(candidate.Asin))
                    {
                        continue;
                    }

                    var (outcome, contributor) = await ProbeContributorAsync(candidate.Asin, region);
                    if (outcome == AudibleRequestOutcome.Unreachable)
                    {
                        // Audible failing to answer is not evidence that the ASIN is wrong, and
                        // failing closed here would stop every author resolving during an outage.
                        // The write-time guard on the author cache is the backstop for this.
                        _logger.LogDebug(
                            "Could not reach the contributor endpoint for {AuthorAsin}; accepting it unconfirmed",
                            LogRedaction.SanitizeText(candidate.Asin));
                        return candidate;
                    }

                    if (contributor != null && NamesAgree(contributor.Name, candidate.Name))
                    {
                        return contributor;
                    }

                    _logger.LogDebug(
                        "Audible does not credit {AuthorAsin} to {Author}; trying the next candidate",
                        LogRedaction.SanitizeText(candidate.Asin),
                        LogRedaction.SanitizeText(candidate.Name ?? string.Empty));
                }

                // Nothing confirmed an ASIN. The name is still the best answer available, and
                // saying "this is who they are and I do not know their id" is better than handing
                // back somebody else's id: callers already treat a null ASIN as a skip.
                return new AuthorLookupItem
                {
                    Name = best.Name,
                    Image = best.Image,
                    Region = best.Region,
                    Description = best.Description
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogWarning(ex, "Failed to lookup author {Author}", LogRedaction.SanitizeText(author));
                return null;
            }
        }

        /// <summary>
        /// Whether the winning candidate's ASIN is worth a round trip before it becomes an
        /// identity. It is not when only one ASIN survived the filter and the winner's name is the
        /// one that was asked for; anything else is a choice, and a choice wants evidence.
        /// </summary>
        private static bool RequiresIdentityConfirmation(
            List<AuthorLookupItem> candidates,
            AuthorLookupItem best,
            string query)
        {
            var distinctAsins = candidates
                .Select(candidate => candidate.Asin)
                .Where(asin => !string.IsNullOrWhiteSpace(asin))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            return distinctAsins > 1 || !IsExactNameMatch(best.Name, query);
        }

        /// <summary>
        /// Asks the contributor endpoint who an ASIN belongs to, and says whether the answer means
        /// anything. An ASIN that is not a contributor id yields no contributor object, which is a
        /// negative; a request that never completed is not.
        /// </summary>
        private async Task<(AudibleRequestOutcome Outcome, AuthorLookupItem? Contributor)> ProbeContributorAsync(
            string authorAsin,
            string region)
        {
            try
            {
                var locale = AudibleRequestHelper.GetLocale(region);
                var url =
                    $"{AudibleRequestHelper.BuildApiBaseUrl(region)}/1.0/catalog/contributors/{Uri.EscapeDataString(authorAsin)}" +
                    $"?locale={Uri.EscapeDataString(locale)}";
                var (outcome, doc) = await _apiClient.GetJsonDocumentWithOutcomeAsync(
                    url, region, includeLocaleHeaders: true, timeoutSeconds: 10);
                using (doc)
                {
                    if (outcome == AudibleRequestOutcome.Unreachable)
                    {
                        return (outcome, null);
                    }

                    if (doc == null ||
                        !doc.RootElement.TryGetProperty("contributor", out var contributor) ||
                        contributor.ValueKind != JsonValueKind.Object)
                    {
                        return (AudibleRequestOutcome.NotFound, null);
                    }

                    return (AudibleRequestOutcome.Success, new AuthorLookupItem
                    {
                        Asin = GetString(contributor, "contributor_id") ?? authorAsin,
                        Name = GetString(contributor, "name"),
                        Image = GetString(contributor, "profile_image_url"),
                        Region = AudibleRequestHelper.NormalizeRegion(region),
                        Description = GetString(contributor, "bio")
                    });
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogWarning(ex, "Failed to lookup Audible author details by ASIN {AuthorAsin}", LogRedaction.SanitizeText(authorAsin));
                return (AudibleRequestOutcome.Unreachable, null);
            }
        }

        /// <summary>
        /// Both names here came from Audible for the same claimed entity, so they should be equal.
        /// Case and diacritic encoding are still forgiven, because two endpoints need not agree on
        /// Unicode normalization and that difference says nothing about who the contributor is.
        /// </summary>
        private static bool NamesAgree(string? contributorName, string? candidateName)
        {
            if (string.IsNullOrWhiteSpace(contributorName) || string.IsNullOrWhiteSpace(candidateName))
            {
                return false;
            }

            return CompareInfo.Compare(contributorName.Trim(), candidateName.Trim(), DiacriticIgnore) == 0;
        }

        private static bool IsExactNameMatch(string? name, string query)
        {
            return !string.IsNullOrWhiteSpace(name)
                && CompareInfo.Compare(name.Trim(), query, DiacriticIgnore) == 0;
        }

        public async Task<AuthorLookupItem?> GetAuthorByAsinAsync(string authorAsin, string region)
        {
            if (string.IsNullOrWhiteSpace(authorAsin)) return null;

            var (_, contributor) = await ProbeContributorAsync(authorAsin, region);
            return contributor;
        }

        public async Task<List<AuthorLookupItem>> LookupAuthorItemsAsync(string author, string region, string? language = null)
        {
            var response = await _productSearchWorkflow.SearchProductsDirectAsync(
                query: null,
                title: null,
                author: author,
                narrator: null,
                publisher: null,
                page: 1,
                limit: 10,
                region: region,
                language: language,
                sortBy: "Relevance",
                returnRawProducts: true);

            if (response.RawProducts == null || response.RawProducts.Count == 0)
            {
                return new List<AuthorLookupItem>();
            }

            var normalizedAuthor = author.Trim();
            return response.RawProducts
                .SelectMany(product =>
                    GetArray(product, "authors")
                        .Select(authorItem => new AuthorLookupItem
                        {
                            Asin = GetString(authorItem, "asin"),
                            Name = GetString(authorItem, "name"),
                            Region = AudibleRequestHelper.NormalizeRegion(region)
                        }))
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                // Containment in either direction, case and diacritics ignored. Kept exactly as
                // loose as it was: it is what lets a stored "Sir Arthur Conan Doyle" reach the
                // contributor credited as "Arthur Conan Doyle", and tightening it would trade a
                // loud defect for a silent one -- an author that stops resolving produces no error
                // and no author page, and nobody finds out until they look.
                .Where(item =>
                    CompareInfo.Compare(item.Name, normalizedAuthor, DiacriticIgnore) == 0 ||
                    CompareInfo.IndexOf(item.Name!, normalizedAuthor, DiacriticIgnore) >= 0 ||
                    CompareInfo.IndexOf(normalizedAuthor, item.Name!, DiacriticIgnore) >= 0)
                .GroupBy(item => $"{item.Asin}|{item.Name}", StringComparer.OrdinalIgnoreCase)
                .Select(group => new { Candidate = group.First(), Credits = group.Count() })
                // Ordered, not taken in the order Audible returned them. The consequential part is
                // that an exact name with no id outranks a merely-containing one that has an id:
                // authors[] entries frequently carry no asin, so filtering on id presence first
                // discarded the right candidate for the wrong one on the commonest response shape.
                .OrderBy(entry => RankCandidate(entry.Candidate, normalizedAuthor))
                .ThenBy(entry => Math.Abs(entry.Candidate.Name!.Trim().Length - normalizedAuthor.Length))
                // A contributor credited across several results of an author-scoped search is a
                // better bet than one credited on a single product.
                .ThenByDescending(entry => entry.Credits)
                .Select(entry => entry.Candidate)
                .ToList();
        }

        /// <summary>
        /// Match quality, best first: the name asked for with an id, the name asked for without
        /// one, a containing credit with an id, a containing credit without one.
        /// </summary>
        private static int RankCandidate(AuthorLookupItem candidate, string query)
        {
            var exact = IsExactNameMatch(candidate.Name, query);
            var hasAsin = !string.IsNullOrWhiteSpace(candidate.Asin);
            return (exact, hasAsin) switch
            {
                (true, true) => 0,
                (true, false) => 1,
                (false, true) => 2,
                _ => 3
            };
        }

        private static IEnumerable<JsonElement> GetArray(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray()
                : Enumerable.Empty<JsonElement>();
        }

        private static string? GetString(JsonElement element, params string[] path)
        {
            var current = element;
            foreach (var segment in path)
            {
                if (current.ValueKind != JsonValueKind.Object ||
                    !current.TryGetProperty(segment, out current))
                {
                    return null;
                }
            }

            return current.ValueKind switch
            {
                JsonValueKind.String => current.GetString(),
                JsonValueKind.Number => current.ToString(),
                JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
                JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
                _ => null
            };
        }
    }
}
