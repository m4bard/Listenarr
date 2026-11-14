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
using Listenarr.Domain.Search.Discovery;

namespace Listenarr.Application.Search.Discovery
{
    /// <summary>
    /// Persistence for recent-release discovery: the per-indexer checkpoint, the inbox of
    /// discovered releases and the coverage gaps. Relational databases only; every method
    /// throws <see cref="InvalidOperationException"/> on any other provider.
    /// </summary>
    public interface IDiscoveryStore
    {
        /// <summary>
        /// Stores one poll in a single transaction: every release not already known for the
        /// indexer is inserted as Pending, the checkpoint row is created or updated, and the
        /// gap, if any, is recorded. Either all of it is committed or none of it is, so the
        /// checkpoint never moves past releases that were not stored.
        /// </summary>
        /// <param name="indexerId">The indexer that was polled.</param>
        /// <param name="releases">The items fetched; an item whose release id is already stored for this indexer is skipped.</param>
        /// <param name="advance">The new checkpoint position, or null to keep the current one.</param>
        /// <param name="gap">A coverage gap found by this poll, or null.</param>
        /// <param name="futureDatedDelta">Items in this poll dated later than the poll time allows; added to the running count.</param>
        /// <param name="undatedDelta">Items in this poll with no usable date; added to the running count.</param>
        /// <param name="pollUtc">The poll time: first-seen time of new rows, last poll time, and, on advance, last success time.</param>
        /// <param name="cancellationToken">The caller's own token. Cancellation before the commit writes nothing.</param>
        /// <returns>The number of releases inserted.</returns>
        Task<int> CommitPollAsync(
            int indexerId,
            IReadOnlyList<DiscoveredReleaseCandidate> releases,
            DiscoveryCheckpointAdvance? advance,
            DiscoveryCoverageGapSpan? gap,
            int futureDatedDelta,
            int undatedDelta,
            DateTime pollUtc,
            CancellationToken cancellationToken);

        /// <summary>
        /// Reads up to <paramref name="limit"/> releases in one state, oldest first
        /// (first-seen time, then id), untracked.
        /// </summary>
        Task<IReadOnlyList<DiscoveredRelease>> GetBatchAsync(
            DiscoveredReleaseState state,
            int limit,
            CancellationToken cancellationToken);

        /// <summary>
        /// Moves one release from <paramref name="expected"/> to <paramref name="next"/>,
        /// writing the outcome, the matched audiobook id and the handled time exactly as given.
        /// Nothing changes when the row is no longer in the expected state.
        /// </summary>
        /// <returns>True when this call made the transition.</returns>
        Task<bool> TryTransitionAsync(
            int releaseId,
            DiscoveredReleaseState expected,
            DiscoveredReleaseState next,
            string? outcome,
            int? audiobookId,
            DateTime utc,
            CancellationToken cancellationToken);

        /// <summary>
        /// Deletes Handled and Ignored releases first seen before <paramref name="olderThanUtc"/>,
        /// oldest first, at most <paramref name="limitPerState"/> per state. Pending and Matched
        /// releases are never deleted.
        /// </summary>
        /// <returns>The number of rows deleted.</returns>
        Task<int> PruneAsync(
            DateTime olderThanUtc,
            int limitPerState,
            CancellationToken cancellationToken);

        /// <summary>
        /// Reads an indexer's checkpoint by its key and its newest unacknowledged gaps. Never
        /// reads the discovered releases.
        /// </summary>
        /// <param name="indexerId">The indexer.</param>
        /// <param name="gapLimit">The most gaps to return, newest first; 0 skips the gap read.</param>
        /// <param name="cancellationToken">The caller's own token.</param>
        Task<DiscoveryIndexerStatus> GetStatusAsync(
            int indexerId,
            int gapLimit,
            CancellationToken cancellationToken);

        /// <summary>
        /// Marks one coverage gap as acknowledged. A gap that is already acknowledged keeps its
        /// original acknowledgement time.
        /// </summary>
        /// <param name="gapId">The gap.</param>
        /// <param name="utc">The acknowledgement time, written only by the call that acknowledges.</param>
        /// <param name="cancellationToken">The caller's own token.</param>
        /// <returns>False when no gap has this id; true when it exists, acknowledged by this call or before.</returns>
        Task<bool> AcknowledgeGapAsync(
            int gapId,
            DateTime utc,
            CancellationToken cancellationToken);
    }

    /// <summary>One fetched feed item, as it is stored in the inbox.</summary>
    public sealed record DiscoveredReleaseCandidate(
        string ReleaseId,
        DateTime? PublishedUtc,
        string Title,
        string Payload);

    /// <summary>
    /// Where the checkpoint moves to. Both values are written as given, so the caller passes
    /// the existing value for anything that should stay.
    /// </summary>
    public sealed record DiscoveryCheckpointAdvance(
        DateTime? NewestPublishedUtc,
        string? NewestReleaseId);

    /// <summary>A stretch of an indexer's feed that a poll could not cover.</summary>
    public sealed record DiscoveryCoverageGapSpan(
        DateTime GapStartUtc,
        DateTime GapEndUtc);

    /// <summary>An indexer's checkpoint, null before its first poll, and its open gaps.</summary>
    public sealed record DiscoveryIndexerStatus(
        IndexerDiscoveryCheckpoint? Checkpoint,
        IReadOnlyList<IndexerCoverageGap> OpenGaps);
}
