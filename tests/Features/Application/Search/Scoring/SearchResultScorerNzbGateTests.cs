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
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Search.Scoring
{
    /// <summary>
    /// The operator's size bounds and quality settings apply to a Usenet result, the same as to a
    /// torrent. Only the penalties for metadata a Usenet indexer does not report stay exempt.
    /// </summary>
    /// <remarks>
    /// Two controls run alongside the assertions, and they have to come out differently from each
    /// other for any of this to mean anything.
    ///
    /// <see cref="TheSeedersGateStillFiresForTheTorrentAndNotTheNzb"/> is a gate that genuinely is
    /// protocol-specific. It has to reject the torrent and pass the NZB, which is what shows the
    /// scorer can tell the two protocols apart at all. If the hoist had flattened the protocol
    /// distinction rather than moved three gates out of it, this test would fail.
    ///
    /// <see cref="AForbiddenWordRejectsBothProtocols"/> is a gate that never was protocol
    /// conditional. It has to reject both. This is the one whose shape differs from every other
    /// assertion here, and it rules out the reading that a Usenet result simply never reaches the
    /// scorer, which would make the rest of this file an artifact rather than a measurement.
    ///
    /// Every profile neutralises what is not under test, the same way tools/profile_gate_probe.py
    /// does: no preferred formats and no preferred languages, since a missing one of either is
    /// worth points to a torrent and nothing to an NZB, no minimum seeders unless that is the
    /// subject, and no published date so neither side takes an age penalty.
    /// </remarks>
    [Trait("Area", "Scoring")]
    [Trait("Name", "SearchResultScorerNzbGateTests")]
    [Trait("Category", "Application")]
    public sealed class SearchResultScorerNzbGateTests : BaseTests
    {
        private const int BaseScore = 100;
        private const long MB = 1024L * 1024L;

        /// <summary>An indexer repository holding one indexer, for the detection-signal tests.</summary>
        private sealed class SingleIndexerRepository : IIndexerRepository
        {
            private readonly Indexer _indexer;

            public SingleIndexerRepository(string type, int retention = 0)
            {
                _indexer = new Indexer { Id = 1, Name = "stub", Type = type, Retention = retention };
            }

            public Task<Indexer?> GetByIdAsync(int id, CancellationToken ct = default)
                => Task.FromResult<Indexer?>(id == _indexer.Id ? _indexer : null);

            public Task<Indexer?> GetByNameAsync(string name, CancellationToken ct = default)
                => Task.FromResult<Indexer?>(null);

            public Task<List<Indexer>> GetAllAsync(CancellationToken ct = default)
                => Task.FromResult(new List<Indexer> { _indexer });

            public Task<List<Indexer>> GetEnabledAsync(bool isAutomaticSearch, CancellationToken ct = default)
                => Task.FromResult(new List<Indexer> { _indexer });

            public Task<Indexer> AddAsync(Indexer indexer, CancellationToken ct = default)
                => throw new NotSupportedException();

            public Task UpdateAsync(Indexer indexer, CancellationToken ct = default)
                => throw new NotSupportedException();

            public Task DeleteAsync(int id, CancellationToken ct = default)
                => throw new NotSupportedException();

            // Added by the stack, not by the branch this fake arrived on. Nothing here exercises
            // the indexer backoff state, so the stub only has to satisfy the interface.
            public Task UpdateBackoffStateAsync(int indexerId, IndexerBackoffState state, CancellationToken ct = default)
                => Task.CompletedTask;
        }

        private static SearchResultScorer CreateScorer(IIndexerRepository? indexers = null)
        {
            return new SearchResultScorer(indexers, NullLogger.Instance);
        }

        private static QualityProfile CreateProfile(
            bool allow128 = true,
            int minimumSizeMb = 0,
            int maximumSizeMb = 0,
            int minimumSeeders = 0,
            params string[] forbidden)
        {
            return new QualityProfile
            {
                Name = "nzb gate test",
                Qualities = new List<QualityDefinition>
                {
                    new() { Quality = "MP3 320kbps", Allowed = true, Priority = 0 },
                    new() { Quality = "MP3 128kbps", Allowed = allow128, Priority = 1 },
                    new() { Quality = "MP3 64kbps", Allowed = true, Priority = 2 },
                },
                CutoffQuality = "MP3 320kbps",
                MinimumSize = minimumSizeMb,
                MaximumSize = maximumSizeMb,
                PreferredFormats = new List<string>(),
                PreferredLanguages = new List<string>(),
                PreferredWords = new List<string>(),
                MustContain = new List<string>(),
                MustNotContain = new List<string>(forbidden),
                MinimumSeeders = minimumSeeders,
                MinimumScore = 0,
                MaximumAge = 0,
                IsDefault = false,
            };
        }

        private static SearchResult Release(
            string quality,
            string downloadType,
            int sizeMb = 300,
            string? title = null,
            int seeders = 0,
            int? indexerId = null)
        {
            return new SearchResult
            {
                Id = $"{quality}-{downloadType}-{sizeMb}",
                Title = title ?? $"Some Author - Some Book {quality} {downloadType}",
                Quality = quality,
                DownloadType = downloadType,
                Size = sizeMb * MB,
                Seeders = seeders,
                PublishedDate = string.Empty,
                Format = string.Empty,
                Language = string.Empty,
                IndexerId = indexerId,
            };
        }

        [Fact]
        public async Task TheSizeCeilingRejectsAnOversizedNzb()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile(maximumSizeMb: 100);

            var torrent = await scorer.Score(Release("MP3 320kbps", "torrent", sizeMb: 500), profile);
            var nzb = await scorer.Score(Release("MP3 320kbps", "usenet", sizeMb: 500), profile);

            Assert.True(torrent.IsRejected);
            Assert.True(nzb.IsRejected, "a 500 MB NZB passed a 100 MB ceiling");
            Assert.Contains(nzb.RejectionReasons, reason => reason.Contains("too large"));
        }

        [Fact]
        public async Task TheSizeFloorRejectsAnUndersizedNzb()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile(minimumSizeMb: 200);

            var torrent = await scorer.Score(Release("MP3 320kbps", "torrent", sizeMb: 50), profile);
            var nzb = await scorer.Score(Release("MP3 320kbps", "usenet", sizeMb: 50), profile);

            Assert.True(torrent.IsRejected);
            Assert.True(nzb.IsRejected, "a 50 MB NZB passed a 200 MB floor");
            Assert.Contains(nzb.RejectionReasons, reason => reason.Contains("too small"));
        }

        [Fact]
        public async Task AnNzbWithinTheSizeBoundsIsStillAccepted()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile(minimumSizeMb: 100, maximumSizeMb: 500);

            var nzb = await scorer.Score(Release("MP3 320kbps", "usenet", sizeMb: 300), profile);

            Assert.False(nzb.IsRejected);
        }

        [Fact]
        public async Task TheQualityDeductionSeparatesTwoNzbsTheSameWayItSeparatesTwoTorrents()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile();

            var torrentTop = await scorer.Score(Release("MP3 320kbps", "torrent"), profile);
            var torrentBottom = await scorer.Score(Release("MP3 64kbps", "torrent"), profile);
            var nzbTop = await scorer.Score(Release("MP3 320kbps", "usenet"), profile);
            var nzbBottom = await scorer.Score(Release("MP3 64kbps", "usenet"), profile);

            // MP3 320kbps is 80 on the ladder and MP3 64kbps is 40, so the deductions are 20 and 60.
            Assert.Equal(BaseScore - 20, torrentTop.TotalScore);
            Assert.Equal(BaseScore - 60, torrentBottom.TotalScore);
            Assert.Equal(torrentTop.TotalScore, nzbTop.TotalScore);
            Assert.Equal(torrentBottom.TotalScore, nzbBottom.TotalScore);
            Assert.True(
                nzbTop.TotalScore > nzbBottom.TotalScore,
                $"the two NZBs both scored {nzbTop.TotalScore}, so the deduction did not run");
        }

        [Fact]
        public async Task AnNzbThatReportsALowQualityNoLongerOutranksAHigherQualityTorrent()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile();

            var bestTorrent = await scorer.Score(Release("MP3 320kbps", "torrent"), profile);
            var worstNzb = await scorer.Score(Release("MP3 64kbps", "usenet"), profile);

            Assert.True(
                bestTorrent.TotalScore > worstNzb.TotalScore,
                $"MP3 64kbps over Usenet scored {worstNzb.TotalScore} against {bestTorrent.TotalScore} "
                + "for MP3 320kbps over torrent, so the protocol is still deciding before the content");
        }

        [Fact]
        public async Task AnNzbThatReportsNoQualityStillOutranksATorrentThatReportsOne()
        {
            // The gap this change does NOT close, asserted so that it is visible in the suite and
            // not only in a commit message. A release that reports no quality takes no deduction,
            // so it stays on the base score and outranks a release that honestly reported a low
            // one. That is the ladder's shape, not a protocol carve-out: the same inversion holds
            // between two torrents on canary today, where a torrent with no quality takes only the
            // flat missing-quality penalty. Hoisting the deduction makes Usenet behave like
            // torrent here rather than introducing an asymmetry.
            //
            // The real answer is a bottom rung for unparsed releases instead of an exemption,
            // which is what Readarr did with UnknownAudio, keyed on the indexer category
            // (src/NzbDrone.Core/Parser/QualityParser.cs:116-123, Qualities/Quality.cs:81,
            // Profiles/Qualities/QualityProfileService.cs:107-112). That is a separate change.
            var scorer = CreateScorer();
            var profile = CreateProfile();

            var unlabelledNzb = await scorer.Score(Release(string.Empty, "usenet"), profile);
            var labelledTorrent = await scorer.Score(Release("MP3 64kbps", "torrent"), profile);
            var unlabelledTorrent = await scorer.Score(Release(string.Empty, "torrent"), profile);

            Assert.True(unlabelledNzb.TotalScore > labelledTorrent.TotalScore);
            Assert.True(unlabelledTorrent.TotalScore > labelledTorrent.TotalScore);
        }

        [Fact]
        public async Task ASizeCeilingAboveTwoGigabytesStillRejectsOnlyWhatIsOverIt()
        {
            // The bounds are int megabytes multiplied out against a long byte count. Above
            // 2047 MB that multiplication wrapped negative, so a ceiling rejected everything and
            // a floor rejected nothing. Reachable for torrents before this change and for every
            // Usenet result after it, and a multi-gigabyte audiobook is ordinary.
            var scorer = CreateScorer();

            // CONTROL, a bound below the wrap where the arithmetic was always right. These two
            // have to come out opposite ways, or the apparatus is not testing the bound at all.
            var belowTheWrap = CreateProfile(maximumSizeMb: 2000);
            var smallUnderLowCeiling = await scorer.Score(Release("MP3 320kbps", "usenet", sizeMb: 300), belowTheWrap);
            var largeOverLowCeiling = await scorer.Score(Release("MP3 320kbps", "usenet", sizeMb: 3000), belowTheWrap);
            Assert.False(smallUnderLowCeiling.IsRejected);
            Assert.True(largeOverLowCeiling.IsRejected);

            var aboveTheWrap = CreateProfile(maximumSizeMb: 3000);
            var small = await scorer.Score(Release("MP3 320kbps", "usenet", sizeMb: 300), aboveTheWrap);
            var large = await scorer.Score(Release("MP3 320kbps", "usenet", sizeMb: 4000), aboveTheWrap);

            Assert.False(small.IsRejected, "a 300 MB release was rejected against a 3000 MB ceiling");
            Assert.True(large.IsRejected);
        }

        [Fact]
        public async Task ASizeFloorAboveTwoGigabytesStillRejectsWhatIsUnderIt()
        {
            var scorer = CreateScorer();

            var belowTheWrap = CreateProfile(minimumSizeMb: 2000);
            Assert.True((await scorer.Score(Release("MP3 320kbps", "usenet", sizeMb: 300), belowTheWrap)).IsRejected);
            Assert.False((await scorer.Score(Release("MP3 320kbps", "usenet", sizeMb: 3000), belowTheWrap)).IsRejected);

            var aboveTheWrap = CreateProfile(minimumSizeMb: 3000);
            var under = await scorer.Score(Release("MP3 320kbps", "usenet", sizeMb: 300), aboveTheWrap);
            var over = await scorer.Score(Release("MP3 320kbps", "usenet", sizeMb: 4000), aboveTheWrap);

            Assert.True(under.IsRejected, "a 300 MB release passed a 3000 MB floor");
            Assert.False(over.IsRejected);
        }

        [Fact]
        public async Task AQualityTheOperatorSwitchedOffIsRefusedOverUsenetToo()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile(allow128: false);

            var torrent = await scorer.Score(Release("MP3 128kbps", "torrent"), profile);
            var nzb = await scorer.Score(Release("MP3 128kbps", "usenet"), profile);

            Assert.True(torrent.IsRejected);
            Assert.True(nzb.IsRejected, "a switched-off quality was downloadable over Usenet");
            Assert.Contains(nzb.RejectionReasons, reason => reason.Contains("not allowed by profile"));

            // The veto records a reason and a -20 penalty rather than the -1 sentinel, and
            // QualityScore.IsRejected reads the reasons (QualityProfile.cs:180). A positive total
            // on a rejected release is the pre-existing shape here, not something this change
            // introduced, and every caller filters on IsRejected before the score.
            Assert.Equal(BaseScore - 50 - 20, nzb.TotalScore);
        }

        [Fact]
        public async Task AnAllowedQualityOverUsenetIsNotVetoed()
        {
            var scorer = CreateScorer();
            var profile = CreateProfile(allow128: false);

            var nzb = await scorer.Score(Release("MP3 320kbps", "usenet"), profile);

            Assert.False(nzb.IsRejected);
            Assert.DoesNotContain(nzb.RejectionReasons, reason => reason.Contains("not allowed by profile"));
        }

        [Fact]
        public async Task IndexerTypeUsenetTakesTheSameGatesAsADeclaredNzb()
        {
            // The indexer's Type is one of the NZB detection signals, and the repository lookup
            // that supplies it used to run between the size gate and the quality gates. A release
            // carrying only this signal was therefore gated on size and exempted from the quality
            // gates, while a release carrying any other signal was exempted from both. Whatever
            // the right answer is, it cannot depend on which signal arrived.
            var scorer = CreateScorer(new SingleIndexerRepository("Usenet"));
            var profile = CreateProfile(allow128: false, maximumSizeMb: 100);

            var oversized = await scorer.Score(
                Release("MP3 320kbps", "torrent", sizeMb: 500, indexerId: 1), profile);
            var refusedQuality = await scorer.Score(
                Release("MP3 128kbps", "torrent", sizeMb: 50, indexerId: 1), profile);

            Assert.True(oversized.IsRejected);
            Assert.Contains(oversized.RejectionReasons, reason => reason.Contains("too large"));
            Assert.True(refusedQuality.IsRejected);
            Assert.Contains(refusedQuality.RejectionReasons, reason => reason.Contains("not allowed by profile"));
        }

        [Fact]
        public async Task AMissingQualityIsStillNotPenalisedForAnNzb()
        {
            // The retained exemption, asserted rather than assumed. A Usenet indexer often reports
            // no quality at all, and this penalty charges a release for that absence rather than
            // for what it contains. It sits with the missing-language and missing-format
            // exemptions, none of which is an operator setting.
            var scorer = CreateScorer();
            var profile = CreateProfile();

            var torrent = await scorer.Score(Release(string.Empty, "torrent"), profile);
            var nzb = await scorer.Score(Release(string.Empty, "usenet"), profile);

            Assert.Equal(BaseScore - 10, torrent.TotalScore);
            Assert.Equal(BaseScore, nzb.TotalScore);
        }

        [Fact]
        public async Task TheSeedersGateStillFiresForTheTorrentAndNotTheNzb()
        {
            // CONTROL. A gate that IS protocol-specific, and stays that way. It proves the scorer
            // still distinguishes the two protocols, so the assertions above show three gates
            // moving rather than the protocol distinction collapsing.
            var scorer = CreateScorer();
            var profile = CreateProfile(minimumSeeders: 5);

            var torrent = await scorer.Score(Release("MP3 320kbps", "torrent", seeders: 0), profile);
            var nzb = await scorer.Score(Release("MP3 320kbps", "usenet", seeders: 0), profile);

            Assert.True(torrent.IsRejected);
            Assert.Contains(torrent.RejectionReasons, reason => reason.Contains("seeders"));
            Assert.False(nzb.IsRejected);
        }

        [Fact]
        public async Task AForbiddenWordRejectsBothProtocols()
        {
            // CONTROL. A gate that was never inside the protocol condition. It has to reject both,
            // which is the one outcome in this file whose shape differs from the findings, and it
            // is what rules out "an NZB never reaches the scorer" as an explanation for any of them.
            const string word = "listenarrprobeforbidden";
            var scorer = CreateScorer();
            var profile = CreateProfile(forbidden: word);

            var torrent = await scorer.Score(
                Release("MP3 320kbps", "torrent", title: $"Some Book {word} torrent"), profile);
            var nzb = await scorer.Score(
                Release("MP3 320kbps", "usenet", title: $"Some Book {word} usenet"), profile);

            Assert.True(torrent.IsRejected);
            Assert.True(nzb.IsRejected);
            Assert.Contains(nzb.RejectionReasons, reason => reason.Contains("forbidden word"));
        }
    }
}
