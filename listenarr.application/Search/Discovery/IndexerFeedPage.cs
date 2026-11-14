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
    /// One page of an indexer's feed. An empty item list never stands for failed, deferred or
    /// cancelled; <see cref="Status"/> says which.
    /// </summary>
    /// <param name="IndexerId">The indexer that was read.</param>
    /// <param name="Status">Completed when the page was read; otherwise why it was not.</param>
    /// <param name="Items">The page's releases that have a release identity, in feed order.</param>
    /// <param name="ItemsReceived">Every item the page carried, including the ones left out of
    /// <see cref="Items"/> because they had no download link or no release identity. The next
    /// page's offset adds this, and a Completed page with none is the end of the feed.</param>
    /// <param name="TotalAvailable">The feed's own item total, when the response states one.</param>
    /// <param name="Deferral">The deferral, when <see cref="Status"/> is Deferred.</param>
    /// <param name="FailureReason">A short reason, when <see cref="Status"/> is Failed.</param>
    public sealed record IndexerFeedPage(
        int IndexerId,
        IndexerSearchStatus Status,
        IReadOnlyList<DiscoveredFeedItem> Items,
        int ItemsReceived,
        int? TotalAvailable,
        RequestDeferral? Deferral = null,
        string? FailureReason = null);

    /// <summary>
    /// One release read from a feed.
    /// </summary>
    /// <param name="Result">The release as the indexer described it.</param>
    /// <param name="ReleaseId">The release's identity, from <see cref="ReleaseIdentity"/>.</param>
    /// <param name="PublishedUtc">The indexer's publish time in UTC, or null when the item has
    /// none or it could not be parsed.</param>
    public sealed record DiscoveredFeedItem(
        IndexerSearchResult Result,
        string ReleaseId,
        DateTime? PublishedUtc);
}
