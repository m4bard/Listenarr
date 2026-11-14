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

namespace Listenarr.Application.Search.Scheduling;

/// <summary>
/// Turns the candidates an automatic search found for one book into a download, or says why not.
/// Shared by the background sweep and the recent-release discovery, which each run their own
/// indexer searches first.
/// </summary>
public interface IAutomaticSearchAcquisition
{
    /// <summary>
    /// Runs the book's pre-checks again (quality profile, active download, cutoff met), then the
    /// post-search steps: scoring, the blocklist, the comparison with the quality already held, the
    /// choice of download client and the download start. Issues no indexer request and persists no
    /// search-schedule state; the caller does. An exception in these steps is returned as
    /// <see cref="BookSearchOutcome.BookFailed"/>; an <see cref="OperationCanceledException"/>
    /// propagates only while <paramref name="ct"/> is cancelled.
    /// </summary>
    /// <param name="audiobook">The book, with its quality profile loaded.</param>
    /// <param name="candidates">Every result the caller's searches gathered for the book.</param>
    /// <param name="origin">Who caused the searches.</param>
    /// <param name="ct">The caller's token.</param>
    Task<AcquisitionResult> AcquireAsync(
        Audiobook audiobook,
        IReadOnlyList<IndexerSearchResult> candidates,
        SearchOrigin origin,
        CancellationToken ct);
}

/// <summary>
/// How an acquisition ended.
/// </summary>
/// <param name="Outcome">Satisfied, NoAcceptableResults, NotNeeded, GrabNotCompleted or BookFailed.</param>
/// <param name="DownloadsQueued">How many downloads were started.</param>
/// <param name="GrabDeferral">The deferral that kept the download's own fetch from being sent, when
/// that is why the outcome is GrabNotCompleted.</param>
public sealed record AcquisitionResult(
    BookSearchOutcome Outcome,
    int DownloadsQueued,
    RequestDeferral? GrabDeferral = null);
