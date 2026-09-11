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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Metadata.Refresh;

public sealed partial class MetadataRefreshService
{
    private async Task<MetadataRefreshApplyResult> ApplyRefreshResultAsync(
        int audiobookId,
        AudibleBookMetadata metadata,
        string expectedMetadataState,
        IMetadataRefreshBudget budget,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var audiobook = await _repository.GetByIdAsync(audiobookId);
        cancellationToken.ThrowIfCancellationRequested();
        if (audiobook == null)
        {
            return new MetadataRefreshApplyResult(MetadataRefreshApplyStatus.NotFound);
        }

        // The preflight read and the one above share this caller's unit of work, so a tracking
        // query hands back the instance already in memory rather than what is stored now. The
        // snapshot is the read that reaches the database, and a concurrent edit is only visible
        // through it.
        var storedState = await _repository.GetByIdSnapshotAsync(audiobookId, cancellationToken);
        if (storedState == null)
        {
            return new MetadataRefreshApplyResult(MetadataRefreshApplyStatus.NotFound);
        }

        if (!string.Equals(
                CreateMetadataStateFingerprint(storedState),
                expectedMetadataState,
                StringComparison.Ordinal))
        {
            return new MetadataRefreshApplyResult(MetadataRefreshApplyStatus.Conflict);
        }

        var legacyIdentifierFieldsTouched = ApplyRefreshPatch(audiobook, metadata);
        var fallbackImageUrl = audiobook.ImageUrl;
        if (!string.IsNullOrWhiteSpace(metadata.ImageUrl))
        {
            fallbackImageUrl = metadata.ImageUrl;
            audiobook.ImageUrl = fallbackImageUrl;
        }

        if (legacyIdentifierFieldsTouched)
        {
            AudiobookIdentifierMapper.SyncImportedIdentifiersFromLegacyFields(audiobook);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!await _repository.UpdateAsync(audiobook))
        {
            return new MetadataRefreshApplyResult(
                MetadataRefreshApplyStatus.NotFound);
        }

        // A cover is a request to the provider's CDN like any other, one per updated book, and
        // it was going out outside the counter and outside the spacing floor: a run at sixty an
        // hour that updated every book it touched was making a hundred and twenty. A refusal
        // here is the run's window closing, and the metadata is already written, so the book
        // keeps the provider's own image URL exactly as it does when the move fails.
        //
        // The charge happens even when the move turns out to be served from cache and no
        // request leaves the process, which overcharges the budget for that book. Left alone on
        // purpose. Only the image cache knows whether it will fetch, and the nearest thing to
        // asking it, GetCachedImagePathAsync, answers a different question: whether a copy
        // exists under that key, not whether MoveToLibraryStorageAsync will go to the network
        // for this URL. Skipping the charge on that answer would undercount real requests, and
        // a budget that undercounts is worse than one that is occasionally pessimistic. A real
        // fix belongs in the cache's own contract, where the two questions can be separated.
        if (!string.IsNullOrWhiteSpace(metadata.ImageUrl)
            && await budget.ChargeAsync(cancellationToken))
        {
            var publishedImageUrl =
                await MoveMetadataImageToLibraryStorageAsync(
                    audiobook,
                    metadata.ImageUrl);
            if (!string.IsNullOrWhiteSpace(publishedImageUrl)
                && !string.Equals(
                    publishedImageUrl,
                    fallbackImageUrl,
                    StringComparison.Ordinal))
            {
                if (await _repository.TryUpdateImageUrlAsync(
                        audiobook.Id,
                        fallbackImageUrl,
                        publishedImageUrl,
                        CancellationToken.None))
                {
                    audiobook.ImageUrl = publishedImageUrl;
                }
                else
                {
                    _logger.LogWarning(
                        "Metadata rescan committed for audiobook {AudiobookId}, but its published image URL could not be enrolled because the stored value changed",
                        audiobook.Id);
                }
            }
        }

        return new MetadataRefreshApplyResult(
            MetadataRefreshApplyStatus.Applied,
            audiobook);
    }

    private static string CreateMetadataStateFingerprint(Audiobook audiobook) =>
        JsonSerializer.Serialize(new
        {
            audiobook.Title,
            audiobook.Subtitle,
            audiobook.PublishYear,
            audiobook.PublishedDate,
            audiobook.Description,
            audiobook.Publisher,
            audiobook.Language,
            audiobook.Runtime,
            audiobook.Version,
            audiobook.Series,
            audiobook.SeriesNumber,
            audiobook.Authors,
            audiobook.Narrators,
            audiobook.Genres,
            audiobook.Isbn,
            audiobook.Asin,
            audiobook.OpenLibraryId,
            audiobook.ImageUrl,
            SeriesMemberships = audiobook.SeriesMemberships?
                .OrderBy(membership => membership.Id)
                .Select(membership => new
                {
                    membership.Id,
                    membership.SeriesName,
                    membership.SeriesAsin,
                    membership.SeriesNumber,
                    membership.IsPrimary,
                    membership.SortOrder
                }),
            ExternalIdentifiers = audiobook.ExternalIdentifiers?
                .OrderBy(identifier => identifier.Id)
                .Select(identifier => new
                {
                    identifier.Id,
                    identifier.Type,
                    identifier.ValueRaw,
                    identifier.ValueNormalized,
                    identifier.Region,
                    identifier.IsPrimary,
                    identifier.Source
                })
        });

    private static IEnumerable<string> EnumerateRefreshRegions(string? preferredRegion)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var ordered = new List<string>();
        void AddOrdered(string? region)
        {
            var normalized = AudiobookIdentifierNormalizer.NormalizeRegion(region);
            if (string.IsNullOrWhiteSpace(normalized)) return;
            if (seen.Add(normalized)) ordered.Add(normalized);
        }

        AddOrdered(preferredRegion);
        AddOrdered("us");
        AddOrdered("uk");

        if (ordered.Count == 0)
        {
            ordered.Add("us");
        }

        return ordered;
    }

    private static bool ApplyRefreshPatch(Audiobook audiobook, AudibleBookMetadata metadata)
    {
        var legacyIdentifierFieldsTouched = false;

        if (!string.IsNullOrWhiteSpace(metadata.Title)) audiobook.Title = metadata.Title;
        if (!string.IsNullOrWhiteSpace(metadata.Subtitle)) audiobook.Subtitle = metadata.Subtitle;
        if (!string.IsNullOrWhiteSpace(metadata.PublishYear)) audiobook.PublishYear = metadata.PublishYear;
        if (!string.IsNullOrWhiteSpace(metadata.PublishedDate)) audiobook.PublishedDate = metadata.PublishedDate;
        if (!string.IsNullOrWhiteSpace(metadata.Description)) audiobook.Description = metadata.Description;
        if (!string.IsNullOrWhiteSpace(metadata.Publisher)) audiobook.Publisher = metadata.Publisher;
        if (!string.IsNullOrWhiteSpace(metadata.Language)) audiobook.Language = metadata.Language;
        if (metadata.Runtime.HasValue && metadata.Runtime.Value > 0) audiobook.Runtime = metadata.Runtime;
        if (!string.IsNullOrWhiteSpace(metadata.Version)) audiobook.Version = metadata.Version;

        if ((metadata.SeriesMemberships != null && metadata.SeriesMemberships.Any()) ||
            !string.IsNullOrWhiteSpace(metadata.Series) ||
            !string.IsNullOrWhiteSpace(metadata.SeriesNumber))
        {
            // Preserve the user's manually-chosen primary series across a rescan rather than
            // reverting to the metadata provider's default (see issue #658).
            AudiobookSeriesMembershipHelper.ApplyToAudiobookPreservingPrimary(
                audiobook,
                metadata.SeriesMemberships,
                metadata.Series,
                metadata.SeriesNumber);
        }

        var authors = NormalizeMetadataStringList(
            (metadata.Authors != null && metadata.Authors.Any())
                ? metadata.Authors
                : (!string.IsNullOrWhiteSpace(metadata.Author) ? new List<string> { metadata.Author! } : null));
        if (authors.Count > 0) audiobook.Authors = authors;

        var narrators = NormalizeMetadataStringList(
            (metadata.Narrators != null && metadata.Narrators.Any())
                ? metadata.Narrators
                : (!string.IsNullOrWhiteSpace(metadata.Narrator) ? new List<string> { metadata.Narrator! } : null));
        if (narrators.Count > 0) audiobook.Narrators = narrators;

        var genres = NormalizeMetadataStringList(metadata.Genres);
        if (genres.Count > 0) audiobook.Genres = genres;

        var isbns = NormalizeMetadataStringList(metadata.Isbn);
        if (isbns.Count > 0)
        {
            audiobook.Isbn = isbns;
            legacyIdentifierFieldsTouched = true;
        }

        if (!string.IsNullOrWhiteSpace(metadata.Asin))
        {
            audiobook.Asin = metadata.Asin;
            legacyIdentifierFieldsTouched = true;
        }

        if (!string.IsNullOrWhiteSpace(metadata.OpenLibraryId))
        {
            audiobook.OpenLibraryId = metadata.OpenLibraryId;
            legacyIdentifierFieldsTouched = true;
        }

        return legacyIdentifierFieldsTouched;
    }

    private async Task<string?> MoveMetadataImageToLibraryStorageAsync(Audiobook audiobook, string imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl)) return null;

        try
        {
            var imageKey = !string.IsNullOrWhiteSpace(audiobook.Asin)
                ? audiobook.Asin!
                : (audiobook.Isbn != null && audiobook.Isbn.Any(i => !string.IsNullOrWhiteSpace(i))
                    ? "img-" + ComputeShortHash(audiobook.Isbn.First(i => !string.IsNullOrWhiteSpace(i)))
                    : "img-" + ComputeShortHash($"{audiobook.Title}|{audiobook.Authors?.FirstOrDefault()}"));

            var libraryImagePath = await _imageCacheService.MoveToLibraryStorageAsync(imageKey, imageUrl);
            if (string.IsNullOrWhiteSpace(libraryImagePath))
            {
                return null;
            }

            return "/" + libraryImagePath.TrimStart('/');
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to move rescanned metadata image for audiobook {AudiobookId}", audiobook.Id);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Failed to move rescanned metadata image for audiobook {AudiobookId}", audiobook.Id);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to move rescanned metadata image for audiobook {AudiobookId}", audiobook.Id);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Failed to move rescanned metadata image for audiobook {AudiobookId}", audiobook.Id);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to move rescanned metadata image for audiobook {AudiobookId}", audiobook.Id);
            return null;
        }
        catch (UriFormatException ex)
        {
            _logger.LogWarning(ex, "Failed to move rescanned metadata image for audiobook {AudiobookId}", audiobook.Id);
            return null;
        }
    }

    private static List<string> NormalizeMetadataStringList(IEnumerable<string>? values)
    {
        if (values == null) return new List<string>();

        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        var first = values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        return first?.Trim();
    }

    private static string ComputeShortHash(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return Guid.NewGuid().ToString("N").Substring(0, 12);

        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA1.HashData(bytes);
        return BitConverter.ToString(hash).Replace("-", "").Substring(0, 16).ToLowerInvariant();
    }
}

/// <summary>What the exclusive apply step did, once it had the audiobook lock.</summary>
public enum MetadataRefreshApplyStatus
{
    Applied,
    NotFound,
    Conflict
}

/// <summary>The apply step's outcome, and the book it wrote when it wrote one.</summary>
public sealed record MetadataRefreshApplyResult(
    MetadataRefreshApplyStatus Status,
    Audiobook? Audiobook = null);
