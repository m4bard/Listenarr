/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Listenarr.Tests.Common;

internal static class IndexerSearchProviderTestExtensions
{
    private static readonly IndexerSearchContext InteractiveContext = new(SearchOrigin.Interactive, CycleAllowance: null);

    /// <summary>
    /// Runs an interactive search with no cycle allowance and returns only what it found, for tests
    /// that assert on a provider's results rather than on how its search ended.
    /// </summary>
    public static async Task<IReadOnlyList<IndexerSearchResult>> SearchResultsAsync(
        this IIndexerSearchProvider provider,
        Indexer indexer,
        string query,
        string? category = null,
        SearchRequest? request = null)
    {
        var outcome = await provider.SearchAsync(indexer, query, category, request, InteractiveContext, CancellationToken.None);
        return outcome.Results;
    }
}
