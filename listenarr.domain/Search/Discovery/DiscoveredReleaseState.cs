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

namespace Listenarr.Domain.Search.Discovery
{
    /// <summary>
    /// Where a discovered release is in its handling. Rows move forward only: Pending to
    /// Matched or Ignored, and Matched to Handled.
    /// </summary>
    public enum DiscoveredReleaseState
    {
        /// <summary>
        /// Stored from a feed page and not yet compared with the monitored library.
        /// </summary>
        Pending,

        /// <summary>
        /// Matched exactly one monitored book and waits to be handed to acquisition.
        /// </summary>
        Matched,

        /// <summary>
        /// Matched no book, or more than one, or could not be matched. Terminal.
        /// </summary>
        Ignored,

        /// <summary>
        /// Handed to acquisition, or found not to need it. Terminal.
        /// </summary>
        Handled
    }
}
