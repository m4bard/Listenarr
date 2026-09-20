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

namespace Listenarr.Application.Search.Scoring
{
    /// <summary>
    /// Breaks ties between scored releases that already carry an identical TotalScore.
    /// </summary>
    /// <remarks>
    /// This comparer never looks at TotalScore and never changes it. Callers order by score
    /// first and only reach this comparer for candidates the scorer could not separate.
    /// Without it, LINQ ordering is stable, so equal scores keep the order the indexer
    /// returned them in and the grab depends on indexer response order.
    ///
    /// The axes and their directions follow Readarr's DownloadDecisionComparer
    /// (src/NzbDrone.Core/DecisionEngine/DownloadDecisionComparer.cs:26-41), restricted to the
    /// fields a Listenarr QualityScore actually carries:
    ///
    ///   CompareQuality           skipped: TotalScore already folds the profile's quality
    ///                            verdict in, and a scored result carries no separate quality
    ///                            index to re-compare.
    ///   CompareCustomFormatScore skipped: Listenarr has no custom formats.
    ///   CompareProtocol          used, in a fixed form. Listenarr has no delay profile, so
    ///                            there is nothing configured to compare against, but the step
    ///                            cannot simply be dropped. See CompareProtocolGroup.
    ///   CompareIndexerPriority   skipped: Indexer.Priority is not on a scored result, only
    ///                            IndexerId is. See the note below.
    ///   ComparePeersIfTorrent    used.
    ///   CompareBookCount         skipped: Listenarr has no discography or multi-book release
    ///                            concept on a search result.
    ///   CompareAgeIfUsenet       used.
    ///   CompareSize              used.
    ///
    /// Indexer priority is deliberately absent. Listenarr's own convention is that a lower
    /// Indexer.Priority wins (listenarr.domain/Search/Indexer.cs:94-96 says so, CompositeScorer
    /// inverts it as (51 - priority) at listenarr.application/Search/Scoring/CompositeScorer.cs:46,
    /// and IndexersController orders ascending), which matches Readarr's CompareByReverse on
    /// IndexerPriority. Reading that value at a selection site would mean either a new repository
    /// dependency on DownloadService or a new field on the domain QualityScore plus a policy for
    /// results whose indexer no longer resolves. Both are design calls for the maintainer, so the
    /// axis is left out rather than guessed at.
    ///
    /// Compare returns a negative number when x should be selected over y, so call sites read
    /// as OrderByDescending(score).ThenBy(candidate, Instance).
    /// </remarks>
    public sealed class ScoredReleaseTiebreaker : IComparer<QualityScore>
    {
        /// <summary>
        /// Shared stateless instance.
        /// </summary>
        public static ScoredReleaseTiebreaker Instance { get; } = new ScoredReleaseTiebreaker();

        /// <summary>
        /// Readarr rounds size down to 200 MB before comparing so near-identical sizes do not
        /// decide a tie (DownloadDecisionComparer.CompareSize, Size.Round(200.Megabytes())).
        /// </summary>
        private const long SizeBucketBytes = 200L * 1024L * 1024L;

        private ScoredReleaseTiebreaker()
        {
        }

        /// <summary>
        /// Orders two equally scored candidates. Negative means x is preferred.
        /// </summary>
        public int Compare(QualityScore? x, QualityScore? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return 1;
            }

            if (y is null)
            {
                return -1;
            }

            var left = x.SearchResult;
            var right = y.SearchResult;

            var byProtocol = CompareProtocolGroup(left, right);
            if (byProtocol != 0)
            {
                return byProtocol;
            }

            var byPeers = ComparePeersIfTorrent(left, right);
            if (byPeers != 0)
            {
                return byPeers;
            }

            var byAge = CompareAgeIfUsenet(left, right);
            if (byAge != 0)
            {
                return byAge;
            }

            var bySize = CompareSize(left, right);
            if (bySize != 0)
            {
                return bySize;
            }

            return CompareIdentity(left, right);
        }

        /// <summary>
        /// Readarr's CompareProtocol asks whether each candidate matches the delay profile's
        /// preferred protocol (DownloadDecisionComparer.cs:84-93). Exactly one of the two
        /// protocols is the preferred one, so that step separates every mixed-protocol pair
        /// before the protocol-conditional axes below can see one. That is what keeps Readarr's
        /// chain transitive, and it is not optional.
        ///
        /// Drop it and ComparePeersIfTorrent fires for some pairs and not others, which is
        /// enough to break transitivity outright. Take a usenet release y, a torrent x with few
        /// seeders and a torrent z with many, ids "a", "b" and "c" respectively. Peers puts z
        /// ahead of x, identity puts y ahead of z and x ahead of y, and the three together are a
        /// cycle. A sort handed a cycle gives an answer that depends on input order, which is
        /// the whole thing this comparer exists to stop.
        ///
        /// Listenarr has no delay profile and so nothing configured to compare against, so the
        /// grouping is fixed, and it takes the family's default rather than a taste. Readarr and
        /// Sonarr both seed their delay profile with PreferredProtocol = 1, which is Usenet:
        /// readarr src/NzbDrone.Core/Datastore/Migration/001_initial_setup.cs:344-353, sonarr
        /// src/NzbDrone.Core/Datastore/Migration/070_delay_profile.cs:26-35, both against
        /// src/NzbDrone.Core/Indexers/DownloadProtocol.cs where Usenet = 1 and Torrent = 2.
        /// Anything that is neither, a direct download for instance, sorts after both.
        ///
        /// This is the one place in the comparer that expresses a preference rather than a
        /// mechanical rule. If Listenarr grows a configurable preferred protocol, it belongs
        /// here. If a protocol preference is unwanted altogether, the alternative is to drop
        /// ComparePeersIfTorrent and CompareAgeIfUsenet as well and rank on size and identity
        /// alone, which is protocol neutral and transitive but throws both axes away.
        /// </summary>
        private static int CompareProtocolGroup(SearchResult left, SearchResult right)
        {
            return ProtocolRank(left).CompareTo(ProtocolRank(right));
        }

        private static int ProtocolRank(SearchResult result)
        {
            if (IsUsenet(result))
            {
                return 0;
            }

            return IsTorrent(result) ? 1 : 2;
        }

        /// <summary>
        /// Readarr ComparePeersIfTorrent: only applies when both candidates are torrents, and
        /// prefers more seeders on a log10 scale, then more peers.
        /// </summary>
        private static int ComparePeersIfTorrent(SearchResult left, SearchResult right)
        {
            if (!IsTorrent(left) || !IsTorrent(right))
            {
                return 0;
            }

            var bySeeders = PeerMagnitude(left.Seeders).CompareTo(PeerMagnitude(right.Seeders));
            if (bySeeders != 0)
            {
                return -bySeeders;
            }

            return -(PeerMagnitude(PeerCount(left)).CompareTo(PeerMagnitude(PeerCount(right))));
        }

        /// <summary>
        /// Readarr CompareAgeIfUsenet: only applies when both candidates are usenet, and prefers
        /// the newer release using Readarr's own age buckets.
        /// </summary>
        private static int CompareAgeIfUsenet(SearchResult left, SearchResult right)
        {
            if (!IsUsenet(left) || !IsUsenet(right))
            {
                return 0;
            }

            if (!TryGetPublishedUtc(left, out var leftPublished) ||
                !TryGetPublishedUtc(right, out var rightPublished))
            {
                return 0;
            }

            return -(AgeBucket(leftPublished).CompareTo(AgeBucket(rightPublished)));
        }

        /// <summary>
        /// Readarr CompareSize: prefers the larger release once both sizes are rounded down to a
        /// 200 MB bucket.
        /// </summary>
        private static int CompareSize(SearchResult left, SearchResult right)
        {
            return -(SizeBucket(left.Size).CompareTo(SizeBucket(right.Size)));
        }

        /// <summary>
        /// Not a Readarr axis. Readarr's chain can still tie, and a tie there falls back to the
        /// source order, which is the behaviour this comparer exists to remove. Finishing on keys
        /// derived from the release itself keeps the winner independent of the order the indexer
        /// returned. Two candidates that match on all of these are the same release as far as
        /// anything downstream can observe.
        /// </summary>
        private static int CompareIdentity(SearchResult left, SearchResult right)
        {
            var byId = string.CompareOrdinal(left.Id, right.Id);
            if (byId != 0)
            {
                return byId;
            }

            var byTitle = string.CompareOrdinal(left.Title, right.Title);
            if (byTitle != 0)
            {
                return byTitle;
            }

            return string.CompareOrdinal(DownloadReference(left), DownloadReference(right));
        }

        private static string DownloadReference(SearchResult result)
        {
            if (!string.IsNullOrEmpty(result.MagnetLink))
            {
                return result.MagnetLink;
            }

            if (!string.IsNullOrEmpty(result.TorrentUrl))
            {
                return result.TorrentUrl;
            }

            if (!string.IsNullOrEmpty(result.NzbUrl))
            {
                return result.NzbUrl;
            }

            return result.DownloadReference ?? string.Empty;
        }

        /// <summary>
        /// Readarr compares Math.Round(Math.Log10(count)) so that only order-of-magnitude
        /// differences in swarm size count.
        /// </summary>
        private static double PeerMagnitude(int? count)
        {
            return count is > 0 ? Math.Round(Math.Log10(count.Value)) : 0d;
        }

        /// <summary>
        /// Readarr's TorrentInfo carries Peers as its own field. Listenarr splits the swarm into
        /// Seeders and Leechers, so peers is their sum.
        /// </summary>
        private static int PeerCount(SearchResult result)
        {
            return (result.Seeders ?? 0) + (result.Leechers ?? 0);
        }

        /// <summary>
        /// Readarr's CompareAgeIfUsenet buckets: under an hour, under a day, under a week,
        /// anything older.
        /// </summary>
        private static int AgeBucket(DateTime publishedUtc)
        {
            var age = DateTime.UtcNow - publishedUtc;

            if (age.TotalHours < 1)
            {
                return 1000;
            }

            if (age.TotalHours <= 24)
            {
                return 100;
            }

            if (age.TotalDays <= 7)
            {
                return 10;
            }

            return 1;
        }

        /// <summary>
        /// Parses PublishedDate invariantly and normalises to UTC. The existing scorers call the
        /// current-culture DateTime.TryParse overload; a tiebreak has to give the same answer on
        /// every host, so this one pins the culture and treats an offset-free timestamp as UTC.
        /// </summary>
        private static bool TryGetPublishedUtc(SearchResult result, out DateTime publishedUtc)
        {
            publishedUtc = default;

            if (string.IsNullOrWhiteSpace(result.PublishedDate))
            {
                return false;
            }

            return DateTime.TryParse(
                result.PublishedDate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out publishedUtc);
        }

        private static long SizeBucket(long sizeBytes)
        {
            return sizeBytes <= 0 ? 0L : sizeBytes / SizeBucketBytes * SizeBucketBytes;
        }

        /// <summary>
        /// Mirrors the usenet branch of TrustedDownloadCandidateFactory.ResolveProtocol
        /// (listenarr.application/Downloads/Submission/TrustedDownloadCandidateFactory.cs:82-88),
        /// which is private there.
        /// </summary>
        private static bool IsUsenet(SearchResult result)
        {
            return !string.IsNullOrWhiteSpace(result.NzbUrl) ||
                   string.Equals(result.DownloadType, "Usenet", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(result.DownloadType, "NZB", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Mirrors the torrent branch of TrustedDownloadCandidateFactory.ResolveProtocol
        /// (listenarr.application/Downloads/Submission/TrustedDownloadCandidateFactory.cs:90-96),
        /// including its precedence: a result that also looks like usenet is not a torrent.
        /// </summary>
        private static bool IsTorrent(SearchResult result)
        {
            if (IsUsenet(result))
            {
                return false;
            }

            return result.TorrentFileContent is { Length: > 0 } ||
                   !string.IsNullOrWhiteSpace(result.MagnetLink) ||
                   (string.Equals(result.DownloadType, "Torrent", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(result.TorrentUrl));
        }
    }
}
