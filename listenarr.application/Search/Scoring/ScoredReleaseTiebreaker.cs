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
    ///                            cannot be dropped. See CompareProtocolGroup.
    ///   CompareIndexerPriority   skipped: Indexer.Priority is not on a scored result, only
    ///                            IndexerId is. See the note below.
    ///   ComparePeersIfTorrent    used.
    ///   CompareBookCount         skipped: Listenarr has no discography or multi-book release
    ///                            concept on a search result.
    ///   CompareAgeIfUsenet       used.
    ///   CompareSize              used.
    ///
    /// EVERY AXIS IS A TOTAL FUNCTION OF A SINGLE RELEASE, once the pair has been narrowed to
    /// one protocol group. That is a requirement, not a style. An axis that consults both
    /// candidates to decide whether it has an opinion, and abstains for some pairs, is enough
    /// to make the whole chain intransitive: a later axis then bridges a pair the earlier one
    /// separated, and x beats y beats z beats x. A sort handed a cycle answers according to
    /// input order, which is the defect this comparer exists to remove. Both cycles found so
    /// far in this file were exactly that shape, one from the protocol step being absent and
    /// one from the age step abstaining on an unparseable date. Anything added here has to be
    /// checked against that rule.
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
    /// as OrderByDescending(score).ThenBy(candidate, ScoredReleaseTiebreaker.ForNow()).
    /// </remarks>
    public sealed class ScoredReleaseTiebreaker : IComparer<QualityScore>
    {
        /// <summary>
        /// Readarr rounds size down to 200 MB before comparing so near-identical sizes do not
        /// decide a tie (DownloadDecisionComparer.CompareSize, Size.Round(200.Megabytes())).
        /// </summary>
        private const long SizeBucketBytes = 200L * 1024L * 1024L;

        private readonly DateTime _now;

        private ScoredReleaseTiebreaker(DateTime now)
        {
            _now = now;
        }

        /// <summary>
        /// A comparer for one sort, with the clock read once up front.
        /// </summary>
        /// <remarks>
        /// The age axis buckets by how old a release is, so it needs a reference instant. Read
        /// the clock per call instead, and a boundary crossed part way through a sort makes two
        /// comparisons of the same pair disagree, which is the intransitivity problem again by a
        /// different route. Reading it once per sort costs one allocation and closes the window.
        /// The instance is immutable, so it is safe to share for the life of that sort.
        /// </remarks>
        public static ScoredReleaseTiebreaker ForNow()
        {
            return new ScoredReleaseTiebreaker(DateTime.UtcNow);
        }

        /// <summary>
        /// Builds a comparer against a fixed instant, for tests that need a stable clock.
        /// </summary>
        public static ScoredReleaseTiebreaker AsOf(DateTime utcNow)
        {
            return new ScoredReleaseTiebreaker(utcNow);
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
        /// preferred protocol (DownloadDecisionComparer.cs:84-93). With a preferred protocol set
        /// to one of the two real ones, exactly one side of any mixed pair matches, so the step
        /// separates every mixed-protocol pair before the protocol-conditional axes below can
        /// see one. That is what keeps Readarr's chain transitive, though not unconditionally:
        /// set PreferredProtocol to Unknown and neither side matches, the step returns 0, and
        /// Readarr has the same cycle.
        ///
        /// Listenarr has no delay profile and nothing configured to compare against, so the
        /// grouping is fixed, and it takes the family's default rather than a taste. Readarr and
        /// Sonarr both seed their delay profile with PreferredProtocol = 1, which is Usenet:
        /// readarr src/NzbDrone.Core/Datastore/Migration/001_initial_setup.cs:344-353, sonarr
        /// src/NzbDrone.Core/Datastore/Migration/070_delay_profile.cs:26-35, both against
        /// src/NzbDrone.Core/Indexers/DownloadProtocol.cs where Usenet = 1 and Torrent = 2.
        ///
        /// Two ways this is not a literal port, both deliberate. Readarr's boolean collapses
        /// everything that is not the preferred protocol into a single group; Listenarr has more
        /// than two protocols, so a direct download would share a group with torrents and
        /// ComparePeersIfTorrent would start abstaining again. Anything that is neither gets its
        /// own rank here. And in the family this preference is a per-tag setting an operator can
        /// see and change, where here it is hard-coded and invisible. That second difference is
        /// worth a maintainer's opinion rather than a contributor's: a mixed-protocol tie now
        /// goes to usenet over a torrent with thousands of seeders, and nobody can tell why or
        /// change it. If Listenarr grows a configurable preferred protocol, this is where it
        /// belongs.
        /// </summary>
        private static int CompareProtocolGroup(SearchResult left, SearchResult right)
        {
            return ProtocolOf(left).CompareTo(ProtocolOf(right));
        }

        /// <summary>
        /// Readarr ComparePeersIfTorrent: only applies when both candidates are torrents, and
        /// prefers more seeders on a log10 scale, then more peers. The protocol group above has
        /// already established that the two share a protocol by the time this runs, so the guard
        /// documents the axis rather than being the thing that keeps it total.
        /// </summary>
        private static int ComparePeersIfTorrent(SearchResult left, SearchResult right)
        {
            if (ProtocolOf(left) != ReleaseProtocol.Torrent || ProtocolOf(right) != ReleaseProtocol.Torrent)
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
        private int CompareAgeIfUsenet(SearchResult left, SearchResult right)
        {
            if (ProtocolOf(left) != ReleaseProtocol.Usenet || ProtocolOf(right) != ReleaseProtocol.Usenet)
            {
                return 0;
            }

            return -(AgeBucket(left).CompareTo(AgeBucket(right)));
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
        /// anything older, plus a fourth case Readarr does not have.
        /// </summary>
        /// <remarks>
        /// A release with no usable PublishedDate gets bucket 0, so it sorts after every dated
        /// one, rather than the axis abstaining for that pair. Abstaining is what made this axis
        /// intransitive: put a dateless release between a fresh one and an old one, and age
        /// separates the two dated releases while the identity fallback orders each of them
        /// against the dateless one, which is a cycle. Both shipping parsers store string.Empty
        /// when a feed omits or mangles pubDate
        /// (Search/Indexers/Torznab/TorznabResponseParser.cs:89-91 and
        /// Search/Indexers/MyAnonamouse/MyAnonamouseResponseParser.cs:351), so one dateless NZB
        /// in a result set is all it takes.
        /// </remarks>
        private int AgeBucket(SearchResult result)
        {
            if (!TryGetPublishedUtc(result, out var publishedUtc))
            {
                return 0;
            }

            var age = _now - publishedUtc;

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
        /// Protocol groups, in the order they rank. Usenet first, per the family's default
        /// preferred protocol; anything that is neither usenet nor torrent last.
        /// </summary>
        private enum ReleaseProtocol
        {
            Usenet = 0,
            Torrent = 1,
            Other = 2
        }

        /// <summary>
        /// Classifies a release the way the submission path does, branch for branch, from
        /// TrustedDownloadCandidateFactory.ResolveProtocol
        /// (listenarr.application/Downloads/Submission/TrustedDownloadCandidateFactory.cs:80-109),
        /// which is private there. Its DirectDownload and Unknown outcomes both land on Other,
        /// since neither the peers nor the age axis applies to either.
        /// </summary>
        /// <remarks>
        /// Anchoring on the submission path matters because this decides a grab. If the tiebreak
        /// and the submitter disagree about what a release is, a result the submitter would hand
        /// to a torrent client can be classified as neither, ranked behind everything, and have
        /// its seeders ignored. The trailing TorrentUrl branch is the one that catches this; an
        /// earlier version of this file left it out and claimed to mirror the method anyway.
        ///
        /// This is a third protocol classifier in the codebase and the other two do not agree
        /// with it. SearchResultScorer.IsNzbResult
        /// (listenarr.application/Search/Scoring/SearchResultScorer.cs:438-453) also sniffs
        /// IndexerImplementation, Source and URLs. Reconciling the three is worth doing and is
        /// more than a tiebreak patch should carry.
        /// </remarks>
        private static ReleaseProtocol ProtocolOf(SearchResult result)
        {
            if (!string.IsNullOrWhiteSpace(result.NzbUrl) ||
                string.Equals(result.DownloadType, "Usenet", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(result.DownloadType, "NZB", StringComparison.OrdinalIgnoreCase))
            {
                return ReleaseProtocol.Usenet;
            }

            if (result.TorrentFileContent is { Length: > 0 } ||
                !string.IsNullOrWhiteSpace(result.MagnetLink) ||
                (string.Equals(result.DownloadType, "Torrent", StringComparison.OrdinalIgnoreCase) &&
                 !string.IsNullOrWhiteSpace(result.TorrentUrl)))
            {
                return ReleaseProtocol.Torrent;
            }

            if (string.Equals(result.DownloadType, DirectDownloadMetadataKeys.ClientId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(result.IndexerImplementation, "InternetArchive", StringComparison.OrdinalIgnoreCase))
            {
                return ReleaseProtocol.Other;
            }

            if (!string.IsNullOrWhiteSpace(result.TorrentUrl))
            {
                return ReleaseProtocol.Torrent;
            }

            return ReleaseProtocol.Other;
        }
    }
}
