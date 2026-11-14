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
namespace Listenarr.Application.Search.Discovery
{
    /// <summary>
    /// Reports how recent-release discovery stands for each indexer that has RSS enabled,
    /// and lets an operator acknowledge a coverage gap. Reads the checkpoint and the gaps
    /// only, never the discovered releases.
    /// </summary>
    public interface IDiscoveryStatusService
    {
        /// <summary>One entry for every enabled indexer with RSS enabled, in indexer id order.</summary>
        Task<IReadOnlyList<DiscoveryIndexerStatusReport>> GetStatusAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Acknowledges a coverage gap; acknowledging it again keeps the first time.
        /// </summary>
        /// <returns>False when no gap has this id.</returns>
        Task<bool> AcknowledgeGapAsync(int gapId, CancellationToken cancellationToken);
    }

    /// <summary>Whether, and why not, discovery can read an indexer's feed.</summary>
    public enum DiscoveryFeedSupport
    {
        /// <summary>The indexer is a Torznab or Newznab indexer with categories configured.</summary>
        Supported,

        /// <summary>No feed provider reads this kind of indexer.</summary>
        Unsupported,

        /// <summary>The indexer is supported but has no categories, so its query-less feed is not read.</summary>
        NoCategories
    }

    /// <summary>
    /// One indexer's discovery status. The time and count fields are null or zero until the
    /// indexer's first poll.
    /// </summary>
    /// <param name="IndexerId">The indexer.</param>
    /// <param name="IndexerName">The indexer's display name.</param>
    /// <param name="Feed">Whether the feed is read, and the reason when it is not.</param>
    /// <param name="NewestPublishedUtc">The checkpoint's publish date, in the indexer's own clock.</param>
    /// <param name="LastPollUtc">Local time of the last poll attempt, whatever its outcome.</param>
    /// <param name="LastSuccessUtc">Local time of the last poll that advanced the checkpoint.</param>
    /// <param name="FutureDatedItemCount">
    /// Cumulative over polls, not distinct items: every item a poll read that was dated later
    /// than poll time plus the overlap, counted again by each poll that reads it.
    /// </param>
    /// <param name="UndatedItemCount">
    /// Cumulative over polls, not distinct items: every item a poll read with no usable publish
    /// date, future-dated items included, counted again by each poll that reads it.
    /// </param>
    /// <param name="OpenGaps">Unacknowledged coverage gaps, newest first, at most the newest few.</param>
    public sealed record DiscoveryIndexerStatusReport(
        int IndexerId,
        string IndexerName,
        DiscoveryFeedSupport Feed,
        DateTime? NewestPublishedUtc,
        DateTime? LastPollUtc,
        DateTime? LastSuccessUtc,
        int FutureDatedItemCount,
        int UndatedItemCount,
        IReadOnlyList<DiscoveryCoverageGapReport> OpenGaps);

    /// <summary>An unacknowledged stretch of an indexer's feed that discovery did not read.</summary>
    public sealed record DiscoveryCoverageGapReport(
        int GapId,
        DateTime GapStartUtc,
        DateTime GapEndUtc,
        DateTime DetectedUtc);
}
