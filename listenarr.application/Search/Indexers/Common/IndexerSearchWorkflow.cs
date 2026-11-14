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

namespace Listenarr.Application.Search.Indexers.Common;

public class IndexerSearchWorkflow
{
    private static readonly IndexerSearchContext InteractiveContext = new(SearchOrigin.Interactive, CycleAllowance: null);

    private readonly HttpClient _httpClient;
    private readonly IConfigurationService _configurationService;
    private readonly IIndexerRepository _indexerRepository;
    private readonly IEnumerable<IIndexerSearchProvider> _searchProviders;
    private readonly IndexerAdditionalSettingsParser _additionalSettingsParser;
    private readonly TorznabResponseParser _torznabResponseParser;
    private readonly ILogger<IndexerSearchWorkflow> _logger;

    public IndexerSearchWorkflow(
        HttpClient httpClient,
        IConfigurationService configurationService,
        IIndexerRepository indexerRepository,
        IEnumerable<IIndexerSearchProvider> searchProviders,
        IndexerAdditionalSettingsParser additionalSettingsParser,
        ILogger<IndexerSearchWorkflow> logger,
        IHtmlTextExtractor? htmlTextExtractor = null)
    {
        _httpClient = httpClient;
        _configurationService = configurationService;
        _indexerRepository = indexerRepository;
        _searchProviders = searchProviders;
        _additionalSettingsParser = additionalSettingsParser;
        _logger = logger;
        _torznabResponseParser = new TorznabResponseParser(httpClient, logger, htmlTextExtractor);
    }

    public async Task<List<IndexerSearchResult>> SearchIndexersAsync(
        string query,
        string? category = null,
        SearchSortBy sortBy = SearchSortBy.Seeders,
        SearchSortDirection sortDirection = SearchSortDirection.Descending,
        bool isAutomaticSearch = false,
        SearchRequest? request = null,
        SearchOrigin origin = SearchOrigin.Interactive)
    {
        var report = await SearchEnabledIndexersAsync(
            query,
            category,
            new IndexerSearchContext(origin, CycleAllowance: null),
            indexerIds: null,
            isAutomaticSearch,
            request,
            CancellationToken.None);
        var results = report.Outcomes.SelectMany(outcome => outcome.Results).ToList();

        _logger.LogInformation("Total {Count} results from all indexers for query: {Query}", results.Count, query);

        return results.OrderByDescending(r => r.Seeders ?? 0).ThenByDescending(r => r.PublishedDate).ToList();
    }

    /// <summary>
    /// Searches the enabled indexers and reports one typed outcome per indexer, so a caller can tell
    /// an indexer that failed, was deferred or was cancelled from one that answered with nothing.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="context">Who caused the search, the cycle's request allowance, and the primary send's kind.</param>
    /// <param name="indexerIds">Restricts the search to these indexers; null searches every enabled indexer.</param>
    /// <param name="isAutomaticSearch">Whether only indexers enabled for automatic search are used.</param>
    /// <param name="request">Optional indexer-specific parameters.</param>
    /// <param name="cancellationToken">The caller's token. Only its cancellation makes an outcome Cancelled.</param>
    public Task<IndexerSearchReport> SearchIndexersDetailedAsync(
        string query,
        IndexerSearchContext context,
        IReadOnlyCollection<int>? indexerIds,
        bool isAutomaticSearch,
        SearchRequest? request,
        CancellationToken cancellationToken)
    {
        return SearchEnabledIndexersAsync(query, category: null, context, indexerIds, isAutomaticSearch, request, cancellationToken);
    }

    private async Task<IndexerSearchReport> SearchEnabledIndexersAsync(
        string query,
        string? category,
        IndexerSearchContext context,
        IReadOnlyCollection<int>? indexerIds,
        bool isAutomaticSearch,
        SearchRequest? request,
        CancellationToken cancellationToken)
    {
        var indexers = await _indexerRepository.GetEnabledAsync(isAutomaticSearch, cancellationToken);
        if (indexerIds != null)
        {
            indexers = indexers.Where(indexer => indexerIds.Contains(indexer.Id)).ToList();
        }

        _logger.LogInformation("Searching {Count} enabled indexers for query: {Query}", indexers.Count, query);

        if (indexers.Count == 0)
        {
            _logger.LogWarning("No indexers configured or enabled; search returned no results for query: {Query}", query);
            return new IndexerSearchReport(Array.Empty<IndexerSearchOutcome>());
        }

        var outcomes = await Task.WhenAll(indexers.Select(indexer =>
            SearchIndexerContainedAsync(indexer, query, category, request, context, cancellationToken)));
        return new IndexerSearchReport(outcomes);
    }

    private async Task<IndexerSearchOutcome> SearchIndexerContainedAsync(
        Indexer indexer,
        string query,
        string? category,
        SearchRequest? request,
        IndexerSearchContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Searching indexer {Name} ({Type}) for query: {Query}", indexer.Name, indexer.Type, query);
            var perIndexerRequest = ApplyIndexerMamOptions(indexer, request);

            var outcome = await SearchIndexerAsync(indexer, query, category, perIndexerRequest, context, cancellationToken);
            _logger.LogInformation("Found {Count} results from indexer {Name}", outcome.Results.Count, indexer.Name);
            return outcome;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new IndexerSearchOutcome(indexer.Id, IndexerSearchStatus.Cancelled, Array.Empty<IndexerSearchResult>(), RequestsSent: 0);
        }
        catch (OperationCanceledException ex)
        {
            // The caller's token is not cancelled, so this is an HttpClient per-request timeout
            // (TaskCanceledException derives from OperationCanceledException). It is a failure of
            // this indexer, contained so a single slow indexer can't abort every other one's results.
            _logger.LogWarning(ex, "Timed out searching indexer {Name} for query: {Query}", indexer.Name, query);
            return Failed(indexer, "timeout");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            _logger.LogError(ex, "Error searching indexer {Name} for query: {Query}", indexer.Name, query);
            return Failed(indexer, "error");
        }
    }

    public async Task<List<SearchResult>> SearchByApiAsync(string apiId, string query, string? category = null)
    {
        try
        {
            var indexer = await GetIndexerByApiIdAsync(apiId);

            if (indexer == null)
            {
                _logger.LogWarning("Indexer not found for apiId: {ApiId}", apiId);
                return new List<SearchResult>();
            }

            if (!indexer.IsEnabled)
            {
                _logger.LogWarning("Indexer {IndexerName} (apiId: {ApiId}) is not enabled", indexer.Name, apiId);
                return new List<SearchResult>();
            }

            var req = new SearchRequest();
            var mamOpts = _additionalSettingsParser.ParseMamOptions(indexer.AdditionalSettings);
            if (mamOpts != null) req.MyAnonamouse = mamOpts;

            var outcome = await SearchIndexerAsync(indexer, query, category, req, InteractiveContext, CancellationToken.None);
            return outcome.Results.Select(SearchResultConverters.ToSearchResult).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            _logger.LogError(ex, "Error searching indexer {ApiId} for query: {Query}", apiId, query);
            return new List<SearchResult>();
        }
    }

    public async Task<List<IndexerSearchResult>> SearchIndexerResultsAsync(
        string apiId,
        string query,
        string? category = null,
        SearchRequest? request = null)
    {
        try
        {
            var indexer = await GetIndexerByApiIdAsync(apiId);

            if (indexer == null || !indexer.IsEnabled)
            {
                _logger.LogWarning("Indexer not found or disabled for apiId: {ApiId}", apiId);
                return new List<IndexerSearchResult>();
            }

            request = ApplyIndexerMamOptions(indexer, request);
            var outcome = await SearchIndexerAsync(indexer, query, category, request, InteractiveContext, CancellationToken.None);
            return outcome.Results.ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            _logger.LogError(ex, "Error searching indexer {ApiId} for query: {Query}", apiId, query);
            return new List<IndexerSearchResult>();
        }
    }

    public async Task<bool> TestApiConnectionAsync(string apiId)
    {
        try
        {
            var apiConfig = await _configurationService.GetApiConfigurationAsync(apiId);
            if (apiConfig == null) return false;

            var response = await _httpClient.GetAsync(apiConfig.BaseUrl);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            _logger.LogError(ex, "Error testing API connection for {ApiId}", apiId);
            return false;
        }
    }

    public async Task<List<IndexerSearchResult>> ParseTorznabResponseAsync(string xmlContent, Indexer indexer)
    {
        return await _torznabResponseParser.ParseAsync(xmlContent, indexer);
    }

    private async Task<Indexer?> GetIndexerByApiIdAsync(string apiId)
    {
        return int.TryParse(apiId, out var indexerId)
            ? await _indexerRepository.GetByIdAsync(indexerId)
            : await _indexerRepository.GetByNameAsync(apiId);
    }

    private SearchRequest? ApplyIndexerMamOptions(Indexer indexer, SearchRequest? request)
    {
        if (request?.MyAnonamouse != null)
            return request;

        var mam = _additionalSettingsParser.ParseMamOptions(indexer.AdditionalSettings);
        if (mam == null)
            return request;

        request ??= new SearchRequest();
        request.MyAnonamouse = mam;
        return request;
    }

    private async Task<IndexerSearchOutcome> SearchIndexerAsync(
        Indexer indexer,
        string query,
        string? category,
        SearchRequest? request,
        IndexerSearchContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            query = IndexerQuerySanitizer.Sanitize(query);
            if (string.IsNullOrWhiteSpace(query))
            {
                // A query that sanitizes to nothing (dash-only or punctuation-only input) must
                // never be dispatched: some indexers treat an empty query as "browse everything"
                // and would return unrelated releases that could then be grabbed. Fail closed.
                _logger.LogWarning(
                    "Skipping indexer {Name} ({Implementation}): the search query was empty after sanitization",
                    indexer.Name, indexer.Implementation);
                return Failed(indexer, "empty query");
            }
            _logger.LogInformation("Searching indexer {Name} ({Implementation}) for: {Query}", indexer.Name, indexer.Implementation, query);

            var fallbackName = GetFallbackIndexerName(indexer);
            var provider = _searchProviders.FirstOrDefault(p =>
                p.IndexerType.Equals(indexer.Implementation, StringComparison.OrdinalIgnoreCase) ||
                (p.IndexerType.Equals("Torznab", StringComparison.OrdinalIgnoreCase)
                 && indexer.Implementation.Equals("Newznab", StringComparison.OrdinalIgnoreCase)));

            if (provider == null)
            {
                _logger.LogWarning("No provider found for indexer type: {Implementation}", indexer.Implementation);
                return Failed(indexer, "no provider");
            }

            var outcome = await provider.SearchAsync(indexer, query, category, request, context, cancellationToken);
            foreach (var r in outcome.Results.Where(r => string.IsNullOrWhiteSpace(r.Source)))
            {
                r.Source = fallbackName;
            }

            return outcome;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            _logger.LogError(ex, "Error searching indexer {Name}", indexer.Name);
            return Failed(indexer, "error");
        }
    }

    private static IndexerSearchOutcome Failed(Indexer indexer, string reason)
    {
        return new IndexerSearchOutcome(
            indexer.Id,
            IndexerSearchStatus.Failed,
            Array.Empty<IndexerSearchResult>(),
            RequestsSent: 0,
            FailureReason: reason);
    }

    private static string GetFallbackIndexerName(Indexer indexer)
    {
        if (!string.IsNullOrWhiteSpace(indexer.Name))
            return indexer.Name;

        if (!string.IsNullOrWhiteSpace(indexer.Implementation))
            return indexer.Implementation;

        try
        {
            var baseUrl = indexer.Url?.TrimEnd('/') ?? string.Empty;
            var baseUri = new Uri(baseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? baseUrl : "https://" + baseUrl);
            return baseUri.Host;
        }
        catch (Exception caughtEx) when (caughtEx is not OperationCanceledException && caughtEx is not OutOfMemoryException && caughtEx is not StackOverflowException)
        {
            return "Indexer";
        }
    }
}
