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

namespace Listenarr.Application.Search.Scoring;

/// <summary>
/// Applies user-facing search result ordering without making SearchService own every sorting rule.
/// </summary>
public class SearchResultSortingService
{
    private readonly IIndexerRepository _indexerRepository;
    private readonly ILogger<SearchResultSortingService> _logger;

    public SearchResultSortingService(
        IIndexerRepository indexerRepository,
        ILogger<SearchResultSortingService> logger)
    {
        _indexerRepository = indexerRepository;
        _logger = logger;
    }

    public async Task<List<SearchResult>> ApplySortingAsync(
        List<SearchResult> results,
        SearchSortBy sortBy,
        SearchSortDirection sortDirection)
    {
        if (!results.Any())
            return results;

        IEnumerable<SearchResult> orderedResults;

        Dictionary<int, Indexer>? indexerCache = null;
        if (sortBy == SearchSortBy.Seeders || sortBy == SearchSortBy.Smart)
        {
            var allIndexers = await _indexerRepository.GetAllAsync();
            indexerCache = allIndexers.ToDictionary(i => i.Id);
        }

        switch (sortBy)
        {
            case SearchSortBy.Seeders:
                var seedScored = ScoreResults(results, indexerCache);
                orderedResults = sortDirection == SearchSortDirection.Descending
                    ? seedScored.OrderByDescending(x => x.Score).Select(x => x.Result)
                    : seedScored.OrderBy(x => x.Score).Select(x => x.Result);
                break;

            case SearchSortBy.Size:
                orderedResults = sortDirection == SearchSortDirection.Descending
                    ? results.OrderByDescending(r => r.Size)
                    : results.OrderBy(r => r.Size);
                break;

            case SearchSortBy.PublishedDate:
                orderedResults = sortDirection == SearchSortDirection.Descending
                    ? results.OrderByDescending(r => r.PublishedDate)
                    : results.OrderBy(r => r.PublishedDate);
                break;

            case SearchSortBy.Title:
                orderedResults = sortDirection == SearchSortDirection.Descending
                    ? results.OrderByDescending(r => r.Title, StringComparer.OrdinalIgnoreCase)
                    : results.OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase);
                break;

            case SearchSortBy.Source:
                orderedResults = sortDirection == SearchSortDirection.Descending
                    ? results.OrderByDescending(r => r.Source, StringComparer.OrdinalIgnoreCase)
                    : results.OrderBy(r => r.Source, StringComparer.OrdinalIgnoreCase);
                break;

            case SearchSortBy.Language:
                orderedResults = sortDirection == SearchSortDirection.Descending
                    ? results.OrderByDescending(r => r.Language ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    : results.OrderBy(r => r.Language ?? string.Empty, StringComparer.OrdinalIgnoreCase);
                break;

            case SearchSortBy.Quality:
                orderedResults = sortDirection == SearchSortDirection.Descending
                    ? results.OrderByDescending(r => QualityScoreLadder.Score(r.Quality))
                    : results.OrderBy(r => QualityScoreLadder.Score(r.Quality));
                break;

            case SearchSortBy.Smart:
                var smartScored = ScoreResults(results, indexerCache);
                orderedResults = sortDirection == SearchSortDirection.Descending
                    ? smartScored.OrderByDescending(x => x.Score).Select(x => x.Result)
                    : smartScored.OrderBy(x => x.Score).Select(x => x.Result);
                break;

            case SearchSortBy.Grabs:
                orderedResults = sortDirection == SearchSortDirection.Descending
                    ? results.OrderByDescending(r => r.Grabs)
                    : results.OrderBy(r => r.Grabs);
                break;

            default:
                orderedResults = results.OrderByDescending(r => r.Seeders ?? 0);
                break;
        }

        return orderedResults.ToList();
    }

    private List<(SearchResult Result, double Score)> ScoreResults(
        IEnumerable<SearchResult> results,
        IReadOnlyDictionary<int, Indexer>? indexerCache)
    {
        return results.Select(r =>
        {
            Indexer? indexer = null;
            if (r.IndexerId.HasValue)
                indexerCache?.TryGetValue(r.IndexerId.Value, out indexer);

            var score = CompositeScorer.CalculateProwlarrStyleScore(r, indexer, _logger).Total;
            return (Result: r, Score: score);
        }).ToList();
    }
}
