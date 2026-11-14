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
    /// Default per-cycle bounds of the automatic search. One sweep searches at most
    /// <see cref="DefaultMaxBooksPerCycle"/> books and sends at most
    /// <see cref="DefaultMaxRequestsPerCycle"/> indexer requests, whichever is reached first.
    /// </summary>
    public static class AutomaticSearchBounds
    {
        public const int DefaultMaxBooksPerCycle = 25;
        public const int DefaultMaxRequestsPerCycle = 300;
    }
}
