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
using Listenarr.Application.Common;

namespace Listenarr.Application.Search.Indexers.MyAnonamouse
{
    internal static class MyAnonamouseDownloadUrlBuilder
    {
        public static string Build(string dlHash, string torrentId, Indexer indexer, bool spendFreeleechWedge = false)
        {
            if (string.IsNullOrWhiteSpace(dlHash) && string.IsNullOrWhiteSpace(torrentId))
            {
                return string.Empty;
            }

            var baseUrl = (indexer.Url ?? "https://www.myanonamouse.net").TrimEnd('/');

            // fl=1 asks MyAnonamouse to apply a freeleech wedge to this grab. Prowlarr adds it in
            // MyAnonamouseParser.GetDownloadUrl, and only ever to /tor/download.php?tid={id};
            // nothing evidences that MyAnonamouse reads fl on the /tor/download.php/{hash} form.
            // So a grab that spends a wedge takes the ?tid= form, and without a torrent id to build
            // it from, the wedge goes unspent rather than riding on a guessed shape. An unspent
            // wedge costs ratio, which seeding earns back; a rejected download URL costs the grab.
            var spendingWedge = spendFreeleechWedge && IsTorrentId(torrentId);
            var downloadUrl = spendingWedge || string.IsNullOrWhiteSpace(dlHash)
                ? $"{baseUrl}/tor/download.php?tid={Uri.EscapeDataString(torrentId)}"
                : $"{baseUrl}/tor/download.php/{Uri.EscapeDataString(dlHash)}";

            if (spendingWedge)
            {
                downloadUrl += "&fl=1";
            }

            var mamIdLocal = MyAnonamouseHelper.TryGetMamId(indexer.AdditionalSettings);
            if (!string.IsNullOrEmpty(mamIdLocal))
            {
                try
                {
                    mamIdLocal = Uri.UnescapeDataString(mamIdLocal);
                }
                catch (Exception caughtEx) when (caughtEx is not OperationCanceledException && caughtEx is not OutOfMemoryException && caughtEx is not StackOverflowException)
                {
                    System.Diagnostics.Debug.WriteLine("Suppressed non-fatal exception in catch block.");
                }

                var separator = downloadUrl.Contains('?') ? '&' : '?';
                downloadUrl += $"{separator}mam_id={Uri.EscapeDataString(mamIdLocal)}";
            }

            return downloadUrl;
        }

        /// <summary>
        /// Whether this is a torrent id MyAnonamouse could resolve, rather than merely a non-empty
        /// string. A torrent id there is a number: Prowlarr deserialises it as int and hands it to
        /// GetDownloadUrl as int (src/NzbDrone.Core/Indexers/Definitions/MyAnonamouse.cs).
        ///
        /// Worth checking rather than assuming, because the caller does not always have one. An
        /// item that arrives without an "id" is given a generated one so the result has a key, and
        /// a generated id in tid would build a download URL that cannot resolve. Spending a wedge
        /// on that while discarding a working hash URL is the worse of the two errors.
        /// </summary>
        private static bool IsTorrentId(string? value)
        {
            return int.TryParse(
                value,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var torrentId) && torrentId > 0;
        }
    }
}
