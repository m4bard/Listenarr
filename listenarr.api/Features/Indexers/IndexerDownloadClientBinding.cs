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

namespace Listenarr.Api.Features.Indexers
{
    /// <summary>
    /// Save-time handling of an indexer's download client binding.
    /// </summary>
    internal static class IndexerDownloadClientBinding
    {
        /// <summary>
        /// Treats a blank binding as "any client" and refuses one naming a client that does not
        /// exist, as Readarr's DownloadClientExistsValidator does on its indexer resource
        /// (src/NzbDrone.Core/Validation/DownloadClientExistsValidator.cs:17-25). Existence is
        /// all that is checked, again as in Readarr: a client that is disabled or of the other
        /// protocol is refused at grab time instead, with a message naming the indexer, because
        /// either can change after the indexer is saved.
        /// </summary>
        /// <returns>An operator-facing error, or null when the binding may be saved.</returns>
        public static async Task<string?> NormalizeAsync(Indexer indexer, IConfigurationService configurationService)
        {
            if (string.IsNullOrWhiteSpace(indexer.DownloadClientId))
            {
                indexer.DownloadClientId = null;
                return null;
            }

            var client = await configurationService.GetDownloadClientConfigurationAsync(indexer.DownloadClientId);
            return client == null ? "The download client chosen for this indexer does not exist" : null;
        }
    }
}
