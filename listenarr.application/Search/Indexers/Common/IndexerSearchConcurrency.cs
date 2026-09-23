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

/// <summary>
/// Resolves how many indexers one search may query at once, from
/// <see cref="ApplicationSettings.MaxConcurrentIndexerSearches"/>.
/// </summary>
/// <remarks>
/// Every enabled indexer is fanned out to concurrently. A local Jackett/Prowlarr proxy has its
/// own connection-handling capacity, and firing all of them at once (unbounded, via
/// Task.WhenAll) can exceed what that proxy can accept in one burst, producing
/// SocketException(111)/SocketException(104) against ports Listenarr itself configured --
/// overload, not a remote outage. Bounded the same way DownloadClientQueuePoller (SemaphoreSlim)
/// and UnmatchedScanBackgroundService (Parallel.ForEachAsync) already bound their own fan-outs.
/// The shipped 4 sits in the middle of the original finding's suggested 3-5 range: enough that a
/// typical few-indexer interactive search still runs effectively unthrottled, low enough that a
/// large automatic-search sweep never asks the local proxy to accept more than 4 simultaneous
/// connections per book.
/// </remarks>
internal static class IndexerSearchConcurrency
{
    internal const int Default = 4;

    // Clamped at use, the same way MetadataRefreshOptionsLoader clamps its budget. The floor
    // matters more than it looks: Parallel.ForEachAsync throws on 0 and reads -1 as "no limit",
    // so an unclamped non-positive value would either fail every search or remove the ceiling.
    // The top is well past any indexer count this ceiling was ever meant to bite on.
    internal const int Min = 1;
    internal const int Max = 32;

    /// <summary>
    /// Reads the ceiling for one search. A missing or unreadable settings row searches at the
    /// shipped <see cref="Default"/>: never wider than what an upgraded install already had.
    /// </summary>
    internal static async Task<int> ResolveAsync(
        IConfigurationService configurationService,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var settings = await configurationService.GetApplicationSettingsAsync();
            ct.ThrowIfCancellationRequested();
            return settings == null
                ? Default
                : Math.Clamp(settings.MaxConcurrentIndexerSearches, Min, Max);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            logger.LogWarning(ex, "Failed to read the indexer search concurrency setting; using {Default}", Default);
            return Default;
        }
    }
}
