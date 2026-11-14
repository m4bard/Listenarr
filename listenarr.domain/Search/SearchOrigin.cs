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

namespace Listenarr.Domain.Search
{
    /// <summary>
    /// Who caused an indexer search. This is separate from the isAutomaticSearch flag, which
    /// only selects the indexer set and skips metadata scraping: the per-book search button and
    /// the background sweep both set that flag, but only the sweep is unattended.
    /// </summary>
    public enum SearchOrigin
    {
        /// <summary>
        /// A person asked for this search and is waiting for the answer. This is the attended
        /// class.
        /// </summary>
        Interactive = 0,

        /// <summary>
        /// The automatic-search sweep. Unattended: nobody is waiting for the answer.
        /// </summary>
        Sweep = 1,

        /// <summary>
        /// The search that follows a failed download. Unattended, like the sweep.
        /// </summary>
        Retry = 2,

        /// <summary>
        /// One request in a search over many books at once, such as the Wanted page's search
        /// all. A person clicked, but nobody waits on any single book, so it is unattended like
        /// the sweep. Kept apart from Sweep so logs and the sweep's own limits do not mix the
        /// two.
        /// </summary>
        Bulk = 3,

        /// <summary>
        /// Recent-release discovery: the worker that polls indexer feeds for new releases.
        /// Unattended: nobody is waiting for the answer.
        /// </summary>
        Discovery = 4
    }
}
