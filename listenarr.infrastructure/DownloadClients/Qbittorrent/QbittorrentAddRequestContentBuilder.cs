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

using System.Globalization;
using System.Net.Http.Headers;

namespace Listenarr.Infrastructure.DownloadClients.Qbittorrent
{
    internal static class QbittorrentAddRequestContentBuilder
    {
        /// <summary>
        /// Builds the form body for a follow-up call to /api/v2/torrents/setShareLimits.
        /// Both keys are always sent once this is called at all, matching the qBittorrent Web
        /// API contract that setShareLimits sets the whole share-limit state for a torrent in
        /// one call. A field the indexer left unset is sent as -2 ("use the global limit"),
        /// which is the request-level equivalent of not overriding it, not a value of zero.
        /// Callers must not invoke this at all when both fields are unset
        /// (see <see cref="TorrentSeedConfiguration.HasAnyValue"/>): that is what keeps an
        /// indexer with no seed criteria from producing a setShareLimits call at all.
        /// </summary>
        public static HttpContent BuildShareLimitsContent(string hash, TorrentSeedConfiguration seedConfiguration)
        {
            var ratioLimit = seedConfiguration.Ratio.HasValue
                ? seedConfiguration.Ratio.Value.ToString(CultureInfo.InvariantCulture)
                : "-2";
            var seedingTimeLimit = seedConfiguration.SeedTime.HasValue
                ? ((long)seedConfiguration.SeedTime.Value.TotalMinutes).ToString(CultureInfo.InvariantCulture)
                : "-2";

            return new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
            {
                new("hashes", hash),
                new("ratioLimit", ratioLimit),
                new("seedingTimeLimit", seedingTimeLimit)
            });
        }

        public static HttpContent Build(QbittorrentTorrentAddPlan addPlan)
        {
            if (addPlan.TorrentFileData != null)
            {
                var multipart = new MultipartFormDataContent();
                multipart.Add(new StringContent(addPlan.SavePath), "savepath");
                if (!string.IsNullOrEmpty(addPlan.Category))
                    multipart.Add(new StringContent(addPlan.Category), "category");
                if (!string.IsNullOrEmpty(addPlan.Tags))
                    multipart.Add(new StringContent(addPlan.Tags), "tags");

                var torrentFileName = string.IsNullOrEmpty(addPlan.FileName) ? "download.torrent" : addPlan.FileName;
                var torrentContent = new ByteArrayContent(addPlan.TorrentFileData);
                torrentContent.Headers.ContentType = new MediaTypeHeaderValue("application/x-bittorrent");
                multipart.Add(torrentContent, "torrents", torrentFileName);
                return multipart;
            }

            var url = addPlan.MagnetLink ?? string.Empty;

            var formData = new List<KeyValuePair<string, string>>
            {
                new("urls", url),
                new("savepath", addPlan.SavePath)
            };

            if (!string.IsNullOrEmpty(addPlan.Category))
                formData.Add(new("category", addPlan.Category));
            if (!string.IsNullOrEmpty(addPlan.Tags))
                formData.Add(new("tags", addPlan.Tags));

            return new FormUrlEncodedContent(formData);
        }
    }
}
