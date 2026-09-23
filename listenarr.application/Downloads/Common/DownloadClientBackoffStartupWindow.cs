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

namespace Listenarr.Application.Downloads.Common
{
    /// <summary>
    /// The first minutes after the process starts, during which a download client failure neither
    /// escalates nor blocks the client for longer than rung 2.
    /// </summary>
    /// <remarks>
    /// Readarr's MinimumTimeSinceStartup
    /// (src/NzbDrone.Core/ThingiProvider/Status/ProviderStatusServiceBase.cs:33,106,127-134). A
    /// container that comes up before its network, or before the client it points at, fails every
    /// client it has; the status is persisted, so without the cap one unlucky restart would carry a
    /// long block across the restart that should have cleared it. Registered as a singleton because
    /// it has to time from process start and the service reading it is scoped.
    /// </remarks>
    public sealed class DownloadClientBackoffStartupWindow
    {
        /// <summary>How long after start the cap applies.</summary>
        public static readonly TimeSpan Duration = TimeSpan.FromMinutes(15);

        private readonly DateTimeOffset _startedAt;

        public DownloadClientBackoffStartupWindow(TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            _startedAt = timeProvider.GetUtcNow();
        }

        /// <summary>Whether <paramref name="now"/> still falls inside the window.</summary>
        public bool Contains(DateTimeOffset now) => now - _startedAt < Duration;
    }
}
