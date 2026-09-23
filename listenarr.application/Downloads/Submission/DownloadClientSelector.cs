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

using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Downloads.Submission
{
    /// <summary>
    /// Chooses which configured download client receives a grab.
    /// </summary>
    /// <remarks>
    /// This is the only selection policy in the backend. The automatic-search sweep used to
    /// carry a second copy of the same vendor-preference chain, which meant a policy added to
    /// one was silently absent from the other, and the two disagreed on both the direct-download
    /// case and on what they returned when nothing matched.
    ///
    /// The policy is: keep the enabled clients that speak the requested protocol, take the group
    /// with the lowest Priority value, and rotate within that group. Lower Priority wins, which
    /// matches Readarr, Sonarr and Prowlarr. There is deliberately no vendor preference any more:
    /// with qBittorrent and Transmission both enabled and both at the default priority, they now
    /// take turns instead of every grab going to qBittorrent. An operator who wants one of them
    /// preferred says so by giving it a lower Priority.
    /// </remarks>
    public class DownloadClientSelector(
        IConfigurationService configurationService,
        IIndexerRepository indexerRepository,
        DownloadClientRoundRobinState roundRobinState,
        ILogger<DownloadClientSelector> logger)
    {
        /// <summary>
        /// Lowest priority value an operator may set. Matches Readarr's validator.
        /// </summary>
        public const int MinimumPriority = 1;

        /// <summary>
        /// Highest priority value an operator may set. Matches Readarr's validator.
        /// </summary>
        public const int MaximumPriority = 50;

        private static readonly string[] TorrentClientTypes = ["qbittorrent", "transmission"];
        private static readonly string[] UsenetClientTypes = ["sabnzbd", "nzbget"];

        /// <summary>
        /// Returns the id of the client that should receive a grab for <paramref name="protocol"/>,
        /// or null when no enabled client can carry it. Null is the single not-found answer; the
        /// automatic-search copy used to return an empty string here instead.
        /// </summary>
        public async Task<string?> GetAppropriateDownloadClientAsync(DownloadProtocol protocol, int? indexerId = null)
        {
            _ = indexerRepository;
            _ = indexerId;

            if (protocol == DownloadProtocol.DirectDownload)
            {
                // Direct downloads are carried by the internal DDL pipeline, not by a
                // configured client. Only the automatic-search copy used to know this.
                logger.LogInformation("Direct download detected, using the internal DDL client");
                return DirectDownloadMetadataKeys.ClientId;
            }

            var wantedTypes = protocol == DownloadProtocol.Torrent ? TorrentClientTypes : UsenetClientTypes;

            var candidates = (await configurationService.GetDownloadClientConfigurationsAsync())
                .Where(c => c.IsEnabled)
                .Where(c => wantedTypes.Contains(c.Type, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (candidates.Count == 0)
            {
                logger.LogWarning(
                    "No enabled {Protocol} download client found. Expected one of: {Expected}",
                    protocol,
                    string.Join(", ", wantedTypes));
                return null;
            }

            var lowestPriority = candidates.Min(c => c.Priority);

            // Ordered by Id and nothing else. Id is the only field on this entity that an
            // edit cannot move: the save path copies the whole posted object over the stored
            // one, and the form does not send CreatedAt, so renaming a client rewrites its
            // CreatedAt and would otherwise shuffle it to the back of the rotation. Readarr
            // orders on its immutable integer id for the same reason
            // (src/NzbDrone.Core/Download/DownloadClientProvider.cs:104-106).
            var group = candidates
                .Where(c => c.Priority == lowestPriority)
                .OrderBy(c => c.Id, StringComparer.Ordinal)
                .ToList();

            var selected = SelectNext(protocol, group);

            logger.LogInformation(
                "Selected {Protocol} client {ClientName} ({ClientType}) from {GroupCount} client(s) at priority {Priority}, out of {CandidateCount} enabled",
                protocol,
                selected.Name,
                selected.Type,
                group.Count,
                lowestPriority,
                candidates.Count);

            return selected.Id;
        }

        /// <summary>
        /// Operator-facing name for a protocol, used in the messages the API returns.
        /// </summary>
        public static string DescribeProtocol(DownloadProtocol protocol) => protocol switch
        {
            DownloadProtocol.Torrent => "torrent",
            DownloadProtocol.Usenet => "NZB",
            DownloadProtocol.DirectDownload => "direct download",
            _ => "NZB"
        };

        private DownloadClientConfiguration SelectNext(DownloadProtocol protocol, List<DownloadClientConfiguration> group)
        {
            if (group.Count == 1)
            {
                roundRobinState.SetLastUsed(protocol, group[0].Id);
                return group[0];
            }

            var lastUsedId = roundRobinState.GetLastUsed(protocol);
            var lastIndex = lastUsedId == null
                ? -1
                : group.FindIndex(c => string.Equals(c.Id, lastUsedId, StringComparison.Ordinal));

            // A last-used client that is gone or no longer in this group leaves lastIndex at -1,
            // which starts the rotation again at the head of the group.
            var next = group[(lastIndex + 1) % group.Count];
            roundRobinState.SetLastUsed(protocol, next.Id);
            return next;
        }
    }
}
