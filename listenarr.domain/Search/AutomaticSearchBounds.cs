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

        /// <summary>
        /// The configured books per cycle are clamped to this range.
        /// </summary>
        public const int MinBooksPerCycle = 1;
        public const int MaxBooksPerCycle = 1000;

        /// <summary>
        /// The configured requests per cycle are clamped to this range. The floor sits above what one
        /// indexer search for one book normally needs, so a search cut by the bound fits in full
        /// when its book heads the next cycle.
        /// </summary>
        public const int MinRequestsPerCycle = 250;
        public const int MaxRequestsPerCycle = 10000;

        /// <summary>
        /// The configured cooldown is clamped to this many hours, for the sweep and the manual search alike.
        /// </summary>
        public const int MinCooldownHours = 1;
        public const int MaxCooldownHours = 720;

        public static TimeSpan Cooldown(int configuredHours) =>
            TimeSpan.FromHours(Math.Clamp(configuredHours, MinCooldownHours, MaxCooldownHours));

        /// <summary>
        /// One sweep reads due books this many at a time, and at most <see cref="SelectionPageBound"/> pages.
        /// </summary>
        public const int SelectionPageSize = 50;
        public const int SelectionPageBound = 4;

        /// <summary>
        /// The no-result wait never grows past this many hours, unless the configured cooldown is
        /// longer (the cap is then the cooldown). A book with nothing found is retried at least weekly.
        /// </summary>
        public const int NoResultMaxIntervalHours = 168;

        /// <summary>
        /// The first wait after an indexer failed for a book; it doubles with each further failure.
        /// </summary>
        public const int PairFailureBaseMinutes = 60;

        /// <summary>
        /// A failing indexer is retried for a book at least daily.
        /// </summary>
        public const int PairFailureMaxIntervalHours = 24;

        /// <summary>
        /// The first wait after a book-level failure (or a grab that did not complete); it doubles
        /// with each further failure up to the configured cooldown.
        /// </summary>
        public const int BookFailureBaseMinutes = 15;
    }
}
