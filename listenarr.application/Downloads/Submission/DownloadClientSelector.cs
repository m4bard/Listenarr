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
    /// The policy runs in the same order as Readarr's DownloadClientProvider.GetDownloadClient
    /// (src/NzbDrone.Core/Download/DownloadClientProvider.cs:39-115), each step narrowing what
    /// the next one sees:
    /// <list type="number">
    /// <item>An indexer bound to a client gets that client, or an error if the client cannot
    /// take the grab. There is no silent fallback to another client.</item>
    /// <item>Otherwise keep the enabled clients that speak the requested protocol, take the
    /// group with the lowest Priority value, and rotate within that group.</item>
    /// </list>
    /// Lower Priority wins, which matches Readarr, Sonarr and Prowlarr. There is deliberately no
    /// vendor preference any more: with qBittorrent and Transmission both enabled and both at the
    /// default priority, they take turns instead of every grab going to qBittorrent. An operator
    /// who wants one of them preferred says so by giving it a lower Priority.
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
        /// <param name="protocol">The protocol of the release being grabbed.</param>
        /// <param name="indexerId">The indexer the release came from, when known. Only consulted
        /// for its download client binding; an unbound or unknown indexer changes nothing.</param>
        /// <exception cref="DownloadClientUnavailableException">The indexer is bound to a client
        /// that does not exist, is disabled, or cannot carry <paramref name="protocol"/>.</exception>
        public async Task<string?> GetAppropriateDownloadClientAsync(DownloadProtocol protocol, int? indexerId = null)
        {
            if (protocol == DownloadProtocol.DirectDownload)
            {
                // Direct downloads are carried by the internal DDL pipeline, not by a
                // configured client, so an indexer binding has nothing to say about them.
                logger.LogInformation("Direct download detected, using the internal DDL client");
                return DirectDownloadMetadataKeys.ClientId;
            }

            var wantedTypes = ClientTypesFor(protocol);
            var clients = await configurationService.GetDownloadClientConfigurationsAsync();

            var boundClient = await ResolveIndexerBindingAsync(protocol, indexerId, wantedTypes, clients);
            if (boundClient != null)
            {
                return boundClient.Id;
            }

            var candidates = clients
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

            return SelectByPriority(protocol, candidates).Id;
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

        /// <summary>
        /// The client types that can carry <paramref name="protocol"/>. Anything that is not a
        /// torrent is treated as usenet, as the old bool-based selector did.
        /// </summary>
        public static IReadOnlyList<string> ClientTypesFor(DownloadProtocol protocol) =>
            protocol == DownloadProtocol.Torrent ? TorrentClientTypes : UsenetClientTypes;

        /// <summary>
        /// The indexer's own client, when it names one. Returns null for an unbound indexer, and
        /// for an indexer id that no longer resolves (a release from an indexer deleted since),
        /// both of which fall through to the ordinary policy.
        /// </summary>
        /// <remarks>
        /// Readarr's equivalent is DownloadClientProvider.cs:63-83. Its candidate list is already
        /// narrowed to enabled clients of the right protocol, so a disabled or wrong-protocol
        /// client reads there as "does not exist". The three cases are told apart here so the
        /// message names the actual fault. The binding is checked before the empty-candidate
        /// test, where Readarr checks it after: a tracker bound to the only client of its
        /// protocol, which is then disabled, would otherwise report "no client configured" and
        /// never mention the indexer setting that has to change.
        ///
        /// A bound grab deliberately does not touch the rotation cursor, as in Readarr, which
        /// returns the bound client before it records a last-used id.
        /// </remarks>
        private async Task<DownloadClientConfiguration?> ResolveIndexerBindingAsync(
            DownloadProtocol protocol,
            int? indexerId,
            IReadOnlyList<string> wantedTypes,
            IReadOnlyList<DownloadClientConfiguration> clients)
        {
            if (indexerId is not int id || id <= 0)
            {
                return null;
            }

            var indexer = await indexerRepository.GetByIdAsync(id);
            if (indexer == null || string.IsNullOrWhiteSpace(indexer.DownloadClientId))
            {
                return null;
            }

            var client = clients.FirstOrDefault(c => string.Equals(c.Id, indexer.DownloadClientId, StringComparison.Ordinal));

            if (client == null)
            {
                throw new DownloadClientUnavailableException(
                    $"Indexer '{indexer.Name}' is set to use a download client that does not exist any more. " +
                    "Choose another client for this indexer, or set it back to Any.");
            }

            if (!client.IsEnabled)
            {
                throw new DownloadClientUnavailableException(
                    $"Indexer '{indexer.Name}' is set to use download client '{client.Name}', which is disabled. " +
                    "Enable that client, or choose another one for this indexer.");
            }

            if (!wantedTypes.Contains(client.Type, StringComparer.OrdinalIgnoreCase))
            {
                throw new DownloadClientUnavailableException(
                    $"Indexer '{indexer.Name}' is set to use download client '{client.Name}', which cannot take a " +
                    $"{DescribeProtocol(protocol)} download. Choose a {DescribeProtocol(protocol)} client for this indexer, or set it back to Any.");
            }

            logger.LogInformation(
                "Indexer {IndexerName} is bound to {Protocol} client {ClientName} ({ClientType}); priority selection skipped",
                indexer.Name,
                protocol,
                client.Name,
                client.Type);

            return client;
        }

        private DownloadClientConfiguration SelectByPriority(DownloadProtocol protocol, List<DownloadClientConfiguration> candidates)
        {
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

            return selected;
        }

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
