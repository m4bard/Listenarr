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
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Listenarr.Application.Metadata.Faults;

namespace Listenarr.Application.Metadata.Core
{
    /// <summary>
    /// Routes audiobook metadata lookups across configured providers.
    /// </summary>
    public class AudiobookMetadataService : IAudiobookMetadataService
    {
        private readonly ISearchService _searchService;
        private readonly AudibleService _audibleService;
        private readonly IAudnexusService _audnexusService;
        private readonly ILogger<AudiobookMetadataService> _logger;

        public AudiobookMetadataService(
            ISearchService searchService,
            AudibleService audibleService,
            IAudnexusService audnexusService,
            ILogger<AudiobookMetadataService> logger)
        {
            _searchService = searchService;
            _audibleService = audibleService;
            _audnexusService = audnexusService;
            _logger = logger;
        }

        public async Task<AudiobookMetadataEnvelope?> GetMetadataAsync(
            string asin,
            string region = "us",
            bool cache = true)
        {
            if (string.IsNullOrWhiteSpace(asin))
            {
                _logger.LogWarning("GetMetadataAsync called with empty ASIN");
                return null;
            }

            // Get enabled metadata sources ordered by priority
            var metadataSources = await _searchService.GetEnabledMetadataSourcesAsync();

            _logger.LogInformation("Found {Count} enabled metadata sources for ASIN {Asin}: {Sources}",
                metadataSources?.Count ?? 0, LogRedaction.SanitizeText(asin),
                string.Join(", ", metadataSources?.Select(s => $"{s.Name} (Priority: {s.Priority}, Enabled: {s.IsEnabled})") ?? new List<string>()));

            if (metadataSources == null || !metadataSources.Any())
            {
                _logger.LogWarning("No enabled metadata sources found for ASIN {Asin}", LogRedaction.SanitizeText(asin));
                return null;
            }

            // The first fault that stopped a source from answering, kept so a walk that ends with
            // nothing can say which of the two nothings it was. A null return means every
            // configured source answered and none of them had the book, and callers act on that
            // as a settled fact about the book. Folding a 429 or a refused connection into the
            // same null tells them a throttled provider has no such book.
            //
            // Only faults that mean the provider did not answer are kept. Anything else, a
            // payload that will not parse above all, is a property of the book rather than of
            // the provider: it will fail the same way on the next attempt and on every one
            // after that. Reported as a provider fault, such a book is never settled, and a
            // caller that retries what it could not settle retries it forever.
            Exception? providerFault = null;

            void NoteProviderFault(Exception exception)
            {
                if (MetadataProviderFaults.IsProviderUnavailable(exception))
                {
                    providerFault ??= exception;
                }
            }

            foreach (var source in metadataSources)
            {
                try
                {
                    _logger.LogInformation("Attempting to fetch metadata from {SourceName} (Priority: {Priority}) for ASIN: {Asin}",
                        source.Name, source.Priority, LogRedaction.SanitizeText(asin));

                    AudibleBookResponse? result = null;

                    if (IsAudibleMetadataSource(source))
                    {
                        result = await _audibleService.GetBookMetadataAsync(asin, region, cache);
                    }
                    else if (source.BaseUrl.Contains("audnex.us", StringComparison.OrdinalIgnoreCase) || source.BaseUrl.Contains("audnex", StringComparison.OrdinalIgnoreCase) || source.BaseUrl.Contains("audnexus", StringComparison.OrdinalIgnoreCase))
                    {
                        // Use Audnexus service to fetch metadata
                        try
                        {
                            var audnexusResult = await _audnexusService.GetBookMetadataAsync(asin, region, seedAuthors: true, update: false);
                            if (audnexusResult != null)
                            {
                                // Convert AudnexusBookResponse to a general shape to return (reuse AudibleBookResponse for compatibility where possible)
                                var converted = new AudibleBookResponse
                                {
                                    Asin = audnexusResult.Asin,
                                    Title = audnexusResult.Title,
                                    Subtitle = audnexusResult.Subtitle,
                                    ImageUrl = audnexusResult.Image,
                                    Publisher = audnexusResult.PublisherName,
                                    LengthMinutes = audnexusResult.RuntimeLengthMin,
                                    Language = audnexusResult.Language,
                                    Explicit = audnexusResult.IsAdult ?? false,
                                    Isbn = audnexusResult.Isbn,
                                    ReleaseDate = audnexusResult.ReleaseDate,
                                    Description = audnexusResult.Description ?? audnexusResult.Summary,
                                    Authors = audnexusResult.Authors?.Select(a => new AudibleAuthor { Asin = a.Asin, Name = a.Name, Region = audnexusResult.Region }).ToList(),
                                    Narrators = audnexusResult.Narrators?.Select(n => new AudibleNarrator { Name = n.Name }).ToList(),
                                    Genres = audnexusResult.Genres?.Select(g => new AudibleGenre { Asin = g.Asin, Name = g.Name, Type = g.Type }).ToList()
                                };
                                result = converted;
                            }
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                        {
                            NoteProviderFault(ex);
                            _logger.LogWarning(ex, "Audnexus lookup failed, trying next source");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Unknown metadata source: {SourceName} ({BaseUrl})", source.Name, source.BaseUrl);
                        continue;
                    }

                    if (result != null)
                    {
                        _logger.LogInformation("Successfully fetched metadata from {SourceName} for ASIN: {Asin}", source.Name, LogRedaction.SanitizeText(asin));
                        return new AudiobookMetadataEnvelope(
                            result,
                            source.Name,
                            source.BaseUrl);
                    }
                }
                catch (Exception sourceEx) when (sourceEx is not OperationCanceledException && sourceEx is not OutOfMemoryException && sourceEx is not StackOverflowException)
                {
                    NoteProviderFault(sourceEx);
                    _logger.LogWarning(sourceEx, "Failed to fetch metadata from {SourceName}, trying next source", source.Name);
                    continue;
                }
            }

            if (providerFault != null)
            {
                // Still tried every source first, so a second provider that does answer wins.
                // Only a walk that ended with no answer at all raises, and it raises the fault
                // that started it rather than a manufactured one.
                //
                // Sanitized like every sibling that logs an ASIN. The value is an unvalidated
                // route argument, so a newline in it writes a line of its own into the log and
                // a reader cannot tell it from one this service wrote.
                _logger.LogWarning(
                    providerFault,
                    "No source answered for ASIN {Asin}; reporting the provider failure rather than a miss",
                    LogRedaction.SanitizeText(asin));
                ExceptionDispatchInfo.Capture(providerFault).Throw();
            }

            _logger.LogWarning("No metadata found for ASIN: {Asin} from any configured source", LogRedaction.SanitizeText(asin));
            return null;
        }

        public async Task<AudibleBookResponse?> GetAudibleMetadataAsync(string asin, string region = "us", bool cache = true)
        {
            if (string.IsNullOrWhiteSpace(asin))
            {
                _logger.LogWarning("GetAudibleMetadataAsync called with empty ASIN");
                return null;
            }

            return await _audibleService.GetBookMetadataAsync(asin, region, cache);
        }

        private static bool IsAudibleMetadataSource(ApiConfiguration source)
        {
            if (source == null) return false;

            var name = source.Name ?? string.Empty;
            var baseUrl = source.BaseUrl ?? string.Empty;

            return name.Contains("Audible", StringComparison.OrdinalIgnoreCase) ||
                baseUrl.Contains("api.audible", StringComparison.OrdinalIgnoreCase);
        }
    }
}
