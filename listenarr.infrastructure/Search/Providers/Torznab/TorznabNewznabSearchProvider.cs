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
    private readonly IIndexerRequestGate _requestGate;
    private readonly ILogger<TorznabNewznabSearchProvider> _logger;

    public string IndexerType => "Torznab"; // Handles both Torznab and Newznab

    public TorznabNewznabSearchProvider(
        HttpClient httpClient,
        IIndexerRequestGate requestGate,
        ILogger<TorznabNewznabSearchProvider> logger)
    {
        _httpClient = httpClient;
        _requestGate = requestGate;
        _logger = logger;
    }

    public async Task<IndexerSearchOutcome> SearchAsync(
        Indexer indexer,
        string query,
        string? category,
        SearchRequest? request,
        IndexerSearchContext context,
        CancellationToken cancellationToken)
    {
        var requestsSent = 0;
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

            var requestContext = new IndexerRequestContext(indexer.Id, context.Origin, context.QueryKind, context.CycleAllowance);
            var sendResult = await _requestGate.SendAsync(
                requestContext,
                token =>
                {
                    requestsSent++;
                    return _httpClient.SendAsync(requestMessage, token);
                },
                cancellationToken);
            if (sendResult is IndexerRequestResult.Deferred deferred)
            {
                _logger.LogInformation(
                    "Indexer {Name} search deferred until {Until} ({Reason})",
                    indexer.Name, deferred.Deferral.UntilUtc, deferred.Deferral.Reason);
                return new IndexerSearchOutcome(
                    indexer.Id, IndexerSearchStatus.Deferred, Array.Empty<IndexerSearchResult>(), requestsSent, deferred.Deferral);
            }

            var response = ((IndexerRequestResult.Sent)sendResult).Response;
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Indexer {Name} returned status {Status}", indexer.Name, response.StatusCode);
                return Failed(indexer, requestsSent, $"status {(int)response.StatusCode}");
            }

            var xmlContent = await response.Content.ReadAsStringAsync(cancellationToken);

            // Parse Torznab/Newznab XML response
            var results = await ParseTorznabResponseAsync(xmlContent, indexer);

            _logger.LogInformation("Indexer {Name} returned {Count} results", indexer.Name, results.Count);
            return new IndexerSearchOutcome(indexer.Id, IndexerSearchStatus.Completed, results, requestsSent);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new IndexerSearchOutcome(indexer.Id, IndexerSearchStatus.Cancelled, Array.Empty<IndexerSearchResult>(), requestsSent);
        }
        catch (OperationCanceledException ex)
        {
            // The caller's token is not cancelled, so this is the HttpClient's per-request timeout.
            _logger.LogWarning(ex, "Timed out searching Torznab/Newznab indexer {Name}", indexer.Name);
            return Failed(indexer, requestsSent, "timeout");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            _logger.LogError(ex, "Error searching Torznab/Newznab indexer {Name}", indexer.Name);
            return Failed(indexer, requestsSent, "error");
        }
    }

    private static IndexerSearchOutcome Failed(Indexer indexer, int requestsSent, string reason)
    {
        return new IndexerSearchOutcome(
            indexer.Id, IndexerSearchStatus.Failed, Array.Empty<IndexerSearchResult>(), requestsSent, FailureReason: reason);
    }

    private string BuildTorznabUrl(Indexer indexer, string query, string? category)
    {
        return TorznabNewznabRequestBuilder.BuildUrl(indexer, query, category);
    }

}
