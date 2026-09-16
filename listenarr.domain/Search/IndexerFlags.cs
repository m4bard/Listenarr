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

namespace Listenarr.Domain.Search
{
    /// <summary>
    /// Names for the release flags a tracker can advertise. These match Prowlarr's IndexerFlag
    /// statics (src/NzbDrone.Core/Indexers/IndexerFlag.cs) so that a Listenarr result and a
    /// Prowlarr one describe the same release the same way. Freeleech25 and Freeleech75 have no
    /// Prowlarr equivalent and take their names from the IndexerFlags enum shared by Sonarr,
    /// Radarr and Readarr.
    /// </summary>
    public static class IndexerFlagNames
    {
        public const string FreeLeech = "freeleech";
        public const string HalfLeech = "halfleech";
        public const string Freeleech25 = "freeleech25";
        public const string Freeleech75 = "freeleech75";
        public const string DoubleUpload = "doubleupload";
        public const string Internal = "internal";
        public const string Scene = "scene";
    }

    /// <summary>
    /// Derives release flags from the standard Torznab attributes. Trackers express the ratio
    /// economy through downloadvolumefactor and uploadvolumefactor, and the release's provenance
    /// through repeated "tag" attributes.
    /// </summary>
    public static class TorznabIndexerFlagParser
    {
        /// <summary>
        /// Reads the flags advertised by one item's Torznab attributes. Attribute names are
        /// matched case-insensitively and unparseable values are ignored, the same way an absent
        /// attribute is.
        /// </summary>
        public static List<string> Parse(IEnumerable<KeyValuePair<string, string>> attributes)
        {
            var flags = new List<string>();
            var downloadFactor = 1d;
            var uploadFactor = 1d;
            var tags = new List<string>();

            foreach (var attribute in attributes)
            {
                switch (attribute.Key?.ToLowerInvariant())
                {
                    case "downloadvolumefactor":
                        if (TryParseFactor(attribute.Value, out var parsedDownload))
                            downloadFactor = parsedDownload;
                        break;
                    case "uploadvolumefactor":
                        if (TryParseFactor(attribute.Value, out var parsedUpload))
                            uploadFactor = parsedUpload;
                        break;
                    case "tag":
                        if (!string.IsNullOrWhiteSpace(attribute.Value))
                            tags.Add(attribute.Value.Trim());
                        break;
                }
            }

            // The mapping is the one Sonarr, Radarr and Readarr share in TorznabRssParser.GetFlags:
            // the name says how much of the download is free, the factor says how much is counted.
            if (downloadFactor == 0d)
                flags.Add(IndexerFlagNames.FreeLeech);
            else if (downloadFactor == 0.25d)
                flags.Add(IndexerFlagNames.Freeleech75);
            else if (downloadFactor == 0.5d)
                flags.Add(IndexerFlagNames.HalfLeech);
            else if (downloadFactor == 0.75d)
                flags.Add(IndexerFlagNames.Freeleech25);

            if (uploadFactor == 2d)
                flags.Add(IndexerFlagNames.DoubleUpload);

            if (tags.Any(tag => string.Equals(tag, IndexerFlagNames.Internal, StringComparison.OrdinalIgnoreCase)))
                flags.Add(IndexerFlagNames.Internal);

            if (tags.Any(tag => string.Equals(tag, IndexerFlagNames.Scene, StringComparison.OrdinalIgnoreCase)))
                flags.Add(IndexerFlagNames.Scene);

            return flags;
        }

        private static bool TryParseFactor(string? value, out double factor)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out factor);
        }
    }
}
