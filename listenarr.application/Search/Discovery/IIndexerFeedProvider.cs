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
    /// Reads an indexer's recent-release feed one page at a time, without a search query.
    /// </summary>
    public interface IIndexerFeedProvider
    {
        /// <summary>
        /// Whether this provider can read the feed of <paramref name="indexer"/>.
        /// </summary>
        bool Supports(Indexer indexer);

        /// <summary>
        /// Fetches one feed page with exactly one request through the request gate. The caller
        /// supplies the whole request context: Origin Discovery, no cycle allowance, and
        /// <see cref="IndexerSearchContext.QueryKind"/> set to
        /// <see cref="IndexerRequestKind.FeedPoll"/> for the first page of a poll and
        /// <see cref="IndexerRequestKind.FeedPage"/> for every page after it.
        /// </summary>
        /// <param name="indexer">The indexer to read.</param>
        /// <param name="offset">How many feed items the poll has received so far.</param>
        /// <param name="context">The request context; the provider sends it unchanged.</param>
        /// <param name="cancellationToken">The caller's own token.</param>
        /// <returns>The page, with a status that says whether it was read.</returns>
        /// <exception cref="ArgumentException">The context's kind is not a feed kind.</exception>
        Task<IndexerFeedPage> FetchPageAsync(
            Indexer indexer,
            int offset,
            IndexerSearchContext context,
            CancellationToken cancellationToken);
    }
}
