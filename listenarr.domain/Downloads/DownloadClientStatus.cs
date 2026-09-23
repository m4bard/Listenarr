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

namespace Listenarr.Domain.Downloads
{
    /// <summary>
    /// The persisted failure record for one download client: how long it has been failing and
    /// until when selection should avoid it. The fields are Readarr's ProviderStatusBase
    /// (src/NzbDrone.Core/ThingiProvider/Status/ProviderStatusBase.cs:8-13), keyed by the client's
    /// string id rather than an integer ProviderId. A client with no row has never failed.
    /// </summary>
    /// <remarks>
    /// Kept in its own table rather than as columns on <see cref="DownloadClientConfiguration"/>,
    /// as Readarr keeps DownloadClientStatus apart from DownloadClients. Saving a client copies the
    /// whole posted object over the stored one, and the settings form knows nothing of these
    /// fields, so as columns every save from the form would have wiped the record.
    /// </remarks>
    public class DownloadClientStatus
    {
        /// <summary>The <see cref="DownloadClientConfiguration.Id"/> this record belongs to.</summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// The first failure of the current run. Set when the escalation level leaves zero and kept
        /// until it returns there, so "failing since" survives later failures.
        /// </summary>
        public DateTime? InitialFailure { get; set; }

        /// <summary>The latest failure, whatever the level.</summary>
        public DateTime? MostRecentFailure { get; set; }

        /// <summary>The rung on the backoff ladder. Zero is healthy.</summary>
        public int EscalationLevel { get; set; }

        /// <summary>Selection avoids the client until this time, UTC. Null when not blocked.</summary>
        public DateTime? DisabledTill { get; set; }

        /// <summary>Whether the client is blocked at <paramref name="utcNow"/>.</summary>
        public bool IsDisabled(DateTime utcNow) => DisabledTill is { } till && till > utcNow;
    }
}
