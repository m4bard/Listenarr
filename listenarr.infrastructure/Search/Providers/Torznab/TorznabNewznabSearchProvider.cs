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

using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Search.Providers.Torznab;

/// <summary>
/// Search provider for Torznab and Newznab compatible indexers.
/// Supports both torrent and usenet indexers using the standard Torznab/Newznab XML API.
/// </summary>
public partial class TorznabNewznabSearchProvider : IIndexerSearchProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TorznabNewznabSearchProvider> _logger;

    public string IndexerType => "Torznab"; // Handles both Torznab and Newznab

    public TorznabNewznabSearchProvider(
        HttpClient httpClient,
        ILogger<TorznabNewznabSearchProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IndexerQueryObservation> SearchAsync(
        Indexer indexer,
        string query,
        string? category = null,
        SearchRequest? request = null)
    {
        try
        {
            // Build Torznab/Newznab API URL (redact api keys before logging)
            var url = TorznabNewznabRequestBuilder.BuildUrl(indexer, query, category);
            var redactedUrl = LogRedaction.RedactText(url, LogRedaction.GetSensitiveValuesFromEnvironment().Concat(new[] { indexer.ApiKey ?? string.Empty }));
            _logger.LogDebug("Indexer API URL: {Url}", redactedUrl);

            // Make HTTP request with User-Agent header
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
            var version = typeof(TorznabNewznabSearchProvider).Assembly.GetName().Version?.ToString() ?? "0.0.0";
            var userAgent = $"Listenarr/{version} (+https://github.com/Listenarrs/listenarr)";
            requestMessage.Headers.UserAgent.ParseAdd(userAgent);

            var response = await _httpClient.SendAsync(requestMessage);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Indexer {Name} returned status {Status}", indexer.Name, response.StatusCode);
                return IndexerQueryObservation.Unavailable(
                    IndexerQueryReason.HttpStatus,
                    query,
                    IndexerQueryFailureClassifier.Describe(response.StatusCode));
            }

            var xmlContent = await response.Content.ReadAsStringAsync();

            // Parse Torznab/Newznab XML response
            var observation = await ParseTorznabResponseAsync(xmlContent, indexer, query);

            _logger.LogInformation("Indexer {Name} returned {Count} results", indexer.Name, observation.Results.Count);
            return observation;
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            // HttpClient reports its own request timeout as a cancellation. Left uncaught it escapes the
            // per-indexer containment upstream and fails the whole fan-out.
            _logger.LogWarning(ex, "Torznab/Newznab indexer {Name} did not answer in time", indexer.Name);
            return IndexerQueryObservation.Unavailable(
                IndexerQueryFailureClassifier.Classify(ex),
                query,
                IndexerQueryFailureClassifier.Describe(ex));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            _logger.LogError(ex, "Error searching Torznab/Newznab indexer {Name}", indexer.Name);
            return IndexerQueryObservation.Unavailable(
                IndexerQueryFailureClassifier.Classify(ex),
                query,
                IndexerQueryFailureClassifier.Describe(ex));
        }
    }

    private string BuildTorznabUrl(Indexer indexer, string query, string? category)
    {
        return TorznabNewznabRequestBuilder.BuildUrl(indexer, query, category);
    }

}
