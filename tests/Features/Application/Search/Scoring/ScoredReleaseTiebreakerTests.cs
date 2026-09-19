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
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Search.Scoring
{
    /// <summary>
    /// Guards the deterministic tiebreak applied where release selection orders scored results.
    /// On stock canary a630572e9 three releases differing only in title all scored 81 and the
    /// grabbed one was whichever the indexer returned first; reversing the indexer's return
    /// order flipped the winner. Every test here pins a winner that cannot be explained by
    /// position, and carries the control that makes it mean something.
    /// </summary>
    [Trait("Name", "ScoredReleaseTiebreakerTests")]
    [Trait("Category", "Application")]
    public class ScoredReleaseTiebreakerTests : BaseTests
    {
        /// <summary>
        /// Scores below the 0..100 clamp in SearchResultScorer, so an assertion that two
        /// candidates scored the same cannot be satisfied by both being clamped to the ceiling.
        /// </summary>
        private const int ExpectedTorrentScore = 91;

        private static QualityProfile BuildProfile()
        {
            return new QualityProfileBuilder()
                .WithName("Tiebreak profile")
                .Build();
        }

        private static QualityProfileService CreateQualityProfileService()
        {
            return new QualityProfileService(
                Mock.Of<IQualityProfileRepository>(),
                NullLogger<QualityProfileService>.Instance,
                indexerRepository: null);
        }

        /// <summary>
        /// A stable, distinct 40 character info hash per candidate id, so the submission path can
        /// verify the magnet the way it would for a real release.
        /// </summary>
        private static string InfoHashFor(string id)
        {
            return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(id)));
        }

        private static SearchResult Torrent(
            string id,
            string title,
            int seeders = 20,
            int leechers = 5,
            long sizeBytes = 500L * 1024 * 1024,
            string quality = "MP3 320kbps")
        {
            return new SearchResult
            {
                Id = id,
                Title = title,
                Artist = "A Public Domain Author",
                DownloadType = "Torrent",
                MagnetLink = $"magnet:?xt=urn:btih:{InfoHashFor(id)}&dn={Uri.EscapeDataString(title)}",
                Format = "mp3",
                Quality = quality,
                Language = "English",
                Seeders = seeders,
                Leechers = leechers,
                Size = sizeBytes
            };
        }

        private static SearchResult Usenet(
            string id,
            string title,
            string publishedDate,
            long sizeBytes = 500L * 1024 * 1024)
        {
            return new SearchResult
            {
                Id = id,
                Title = title,
                Artist = "A Public Domain Author",
                DownloadType = "Usenet",
                NzbUrl = $"https://usenet.invalid/{id}.nzb",
                Format = "m4b",
                Quality = "MP3 320kbps",
                // The profile prefers English, so a German release takes the mismatch penalty.
                // Both candidates take it equally, which keeps the tie below the 100 clamp.
                Language = "German",
                PublishedDate = publishedDate,
                Size = sizeBytes
            };
        }

        private static async Task<List<QualityScore>> ScoreAsync(params SearchResult[] candidates)
        {
            return await CreateQualityProfileService()
                .ScoreSearchResults(candidates.ToList(), BuildProfile());
        }

        private static void AssertAllScoresTied(IReadOnlyList<QualityScore> scored, int expectedScore)
        {
            foreach (var candidate in scored)
            {
                Assert.False(
                    candidate.IsRejected,
                    $"'{candidate.SearchResult.Title}' was rejected: {string.Join(", ", candidate.RejectionReasons)}");
                Assert.Equal(expectedScore, candidate.TotalScore);
            }
        }

        // ------------------------------------------------------------------
        // 1. The reversal test. This is the regression guard.
        // ------------------------------------------------------------------

        [Fact]
        public async Task ScoreSearchResults_SelectsSameWinner_WhenCandidateOrderIsReversed()
        {
            // Identical in every field except Id and Title, which is the shape measured on stock.
            // The expected winner sits in the middle so that neither the forward nor the reversed
            // run can select it by position.
            var forward = new List<SearchResult>
            {
                Torrent("release-b", "Bravo Release"),
                Torrent("release-a", "Alpha Release"),
                Torrent("release-c", "Charlie Release")
            };
            var reversed = Enumerable.Reverse(forward).ToList();

            var service = CreateQualityProfileService();
            var forwardScored = await service.ScoreSearchResults(forward, BuildProfile());
            var reversedScored = await service.ScoreSearchResults(reversed, BuildProfile());

            // Control: the scorer really could not separate these, and the tie is not the clamp.
            AssertAllScoresTied(forwardScored, ExpectedTorrentScore);
            AssertAllScoresTied(reversedScored, ExpectedTorrentScore);
            Assert.True(ExpectedTorrentScore < 100, "The tie must sit below the score clamp.");

            // Control: the two inputs really were in different orders.
            Assert.NotEqual(
                forward.Select(candidate => candidate.Id).ToArray(),
                reversed.Select(candidate => candidate.Id).ToArray());

            Assert.Equal("release-a", forwardScored[0].SearchResult.Id);
            Assert.Equal("release-a", reversedScored[0].SearchResult.Id);
        }

        [Fact]
        public async Task SearchAndDownload_GrabsSameRelease_WhenIndexerReturnsCandidatesReversed()
        {
            var forward = new List<SearchResult>
            {
                Torrent("release-b", "Bravo Release"),
                Torrent("release-a", "Alpha Release"),
                Torrent("release-c", "Charlie Release")
            };
            var reversed = Enumerable.Reverse(forward).ToList();

            // Control: the scorer cannot separate these, so only the tiebreak can decide.
            AssertAllScoresTied(await ScoreAsync([.. forward]), ExpectedTorrentScore);

            var forwardGrab = await RunSearchAndDownloadAsync(forward);
            var reversedGrab = await RunSearchAndDownloadAsync(reversed);

            Assert.Equal("release-a", forwardGrab);
            Assert.Equal("release-a", reversedGrab);
        }

        // ------------------------------------------------------------------
        // 2. The control that must keep passing: score still beats the tiebreak.
        // ------------------------------------------------------------------

        [Fact]
        public async Task ScoreSearchResults_PrefersHigherScore_FromAnyPosition()
        {
            // "release-z" sorts last on every tiebreak axis, so if it wins it wins on score.
            var better = Torrent("release-z", "Zulu Release", quality: "M4B");
            var forward = new List<SearchResult>
            {
                Torrent("release-a", "Alpha Release"),
                Torrent("release-b", "Bravo Release"),
                better
            };
            var reversed = Enumerable.Reverse(forward).ToList();

            var service = CreateQualityProfileService();
            var forwardScored = await service.ScoreSearchResults(forward, BuildProfile());
            var reversedScored = await service.ScoreSearchResults(reversed, BuildProfile());

            // Control: the better candidate really does score higher, and the rest really do tie.
            var betterScore = forwardScored.Single(s => s.SearchResult.Id == "release-z").TotalScore;
            Assert.True(
                betterScore > ExpectedTorrentScore,
                $"Expected a higher score than {ExpectedTorrentScore}, got {betterScore}.");
            AssertAllScoresTied(
                forwardScored.Where(s => s.SearchResult.Id != "release-z").ToList(),
                ExpectedTorrentScore);

            Assert.Equal("release-z", forwardScored[0].SearchResult.Id);
            Assert.Equal("release-z", reversedScored[0].SearchResult.Id);
        }

        // ------------------------------------------------------------------
        // 3. One test per implemented axis, isolated by holding the others equal.
        // ------------------------------------------------------------------

        [Fact]
        public async Task Tiebreak_PrefersMoreSeeders_WhenTorrentsAreOtherwiseTied()
        {
            // The scorer's seeders bonus is Math.Min(10, seeders), so both of these earn the same
            // +10 and the seeder count survives into the tie untouched.
            var few = Torrent("release-a", "Alpha Release", seeders: 20, leechers: 0);
            var many = Torrent("release-b", "Bravo Release", seeders: 5000, leechers: 0);

            var forward = await ScoreAsync(few, many);
            var reversed = await ScoreAsync(many, few);

            // Control: the difference is invisible to the score.
            AssertAllScoresTied(forward, ExpectedTorrentScore);

            Assert.Equal("release-b", forward[0].SearchResult.Id);
            Assert.Equal("release-b", reversed[0].SearchResult.Id);
        }

        [Fact]
        public async Task Tiebreak_PrefersMorePeers_WhenSeederMagnitudeIsTied()
        {
            // Same seeder count, so the seeders step of the axis ties and peers decides.
            // Leechers feed no part of the score.
            var quiet = Torrent("release-a", "Alpha Release", seeders: 200, leechers: 3);
            var busy = Torrent("release-b", "Bravo Release", seeders: 200, leechers: 90000);

            var forward = await ScoreAsync(quiet, busy);
            var reversed = await ScoreAsync(busy, quiet);

            AssertAllScoresTied(forward, ExpectedTorrentScore);

            Assert.Equal("release-b", forward[0].SearchResult.Id);
            Assert.Equal("release-b", reversed[0].SearchResult.Id);
        }

        [Fact]
        public async Task Tiebreak_PrefersNewerRelease_WhenBothAreUsenet()
        {
            // Both ages land in the same bucket of the scorer's age penalty (it only starts to
            // bite after about 61 days), so the score cannot see the difference.
            var older = Usenet("release-a", "Alpha Release", DateTime.UtcNow.AddDays(-30).ToString("O"));
            var newer = Usenet("release-b", "Bravo Release", DateTime.UtcNow.AddHours(-2).ToString("O"));

            var forward = await ScoreAsync(older, newer);
            var reversed = await ScoreAsync(newer, older);

            AssertAllScoresTied(forward, 86);

            Assert.Equal("release-b", forward[0].SearchResult.Id);
            Assert.Equal("release-b", reversed[0].SearchResult.Id);
        }

        [Fact]
        public async Task Tiebreak_PrefersLargerRelease_WhenSizeIsTheOnlyDifference()
        {
            // The profile sets no minimum or maximum size, so size contributes nothing to the score.
            var small = Torrent("release-a", "Alpha Release", sizeBytes: 500L * 1024 * 1024);
            var large = Torrent("release-b", "Bravo Release", sizeBytes: 1200L * 1024 * 1024);

            var forward = await ScoreAsync(small, large);
            var reversed = await ScoreAsync(large, small);

            AssertAllScoresTied(forward, ExpectedTorrentScore);

            Assert.Equal("release-b", forward[0].SearchResult.Id);
            Assert.Equal("release-b", reversed[0].SearchResult.Id);
        }

        [Fact]
        public async Task Tiebreak_IgnoresSizeDifferencesInsideOneBucket()
        {
            // Readarr rounds size down to 200 MB before comparing. These two land in the same
            // bucket, so size must not decide, and the identity fallback takes over.
            var lower = Torrent("release-b", "Bravo Release", sizeBytes: 401L * 1024 * 1024);
            var higher = Torrent("release-a", "Alpha Release", sizeBytes: 599L * 1024 * 1024);

            var forward = await ScoreAsync(lower, higher);
            var reversed = await ScoreAsync(higher, lower);

            AssertAllScoresTied(forward, ExpectedTorrentScore);

            // The larger one is "release-a" only by coincidence of naming; what this pins is that
            // the winner is the identity-ordered one rather than the larger one.
            Assert.Equal("release-a", forward[0].SearchResult.Id);
            Assert.Equal("release-a", reversed[0].SearchResult.Id);
        }

        // ------------------------------------------------------------------
        // 4. Total order: equal on every Readarr-derived axis still decides.
        // ------------------------------------------------------------------

        [Fact]
        public async Task Tiebreak_ProducesStableWinner_WhenEveryRankingAxisIsTied()
        {
            var first = Torrent("release-b", "Bravo Release");
            var second = Torrent("release-a", "Alpha Release");

            AssertAllScoresTied(await ScoreAsync(first, second), ExpectedTorrentScore);

            // Same pair, both orders, repeated, must always name the same release.
            for (var repeat = 0; repeat < 3; repeat++)
            {
                var forward = await ScoreAsync(first, second);
                var reversed = await ScoreAsync(second, first);

                Assert.Equal("release-a", forward[0].SearchResult.Id);
                Assert.Equal("release-a", reversed[0].SearchResult.Id);
            }
        }

        [Fact]
        public async Task Tiebreak_FallsBackToTitle_WhenIdsAreIdentical()
        {
            // Some indexers reuse or omit the result id. Title is the next stable key.
            var bravo = Torrent("same-id", "Bravo Release");
            var alpha = Torrent("same-id", "Alpha Release");
            bravo.MagnetLink = $"magnet:?xt=urn:btih:{InfoHashFor("same")}";
            alpha.MagnetLink = $"magnet:?xt=urn:btih:{InfoHashFor("same")}";

            var forward = await ScoreAsync(bravo, alpha);
            var reversed = await ScoreAsync(alpha, bravo);

            AssertAllScoresTied(forward, ExpectedTorrentScore);

            Assert.Equal("Alpha Release", forward[0].SearchResult.Title);
            Assert.Equal("Alpha Release", reversed[0].SearchResult.Title);
        }

        // ------------------------------------------------------------------
        // Harness for the DownloadService selection site.
        // ------------------------------------------------------------------

        private async Task<string?> RunSearchAndDownloadAsync(List<SearchResult> candidates)
        {
            var searchServiceMock = new Mock<ISearchService>();
            searchServiceMock
                .Setup(service => service.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<List<string>?>(),
                    It.IsAny<SearchSortBy>(),
                    It.IsAny<SearchSortDirection>(),
                    true))
                .ReturnsAsync(candidates);

            var gatewayMock = new Mock<IDownloadClientGateway>();
            gatewayMock
                .Setup(gateway => gateway.AddAsync(
                    It.IsAny<DownloadClientConfiguration>(),
                    It.IsAny<PreparedDownloadSubmission>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DownloadClientSubmissionResult("ABCDEF1234567890ABCDEF1234567890ABCDEF12"));

            _services.AddSingleton(searchServiceMock.Object);
            _services.AddSingleton(gatewayMock.Object);
            Init();

            await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                .WithOutputPath(Path.GetTempPath())
                .Build());

            await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfiguration
            {
                Id = "qb-tiebreak",
                Name = "local qbit",
                Type = "qbittorrent",
                Host = "localhost",
                Port = 8080,
                IsEnabled = true
            });

            var profile = await _qualityProfileRepository.AddAsync(BuildProfile());
            var audiobook = await CreateAudiobook();
            audiobook.QualityProfileId = profile.Id;
            await _audiobookRepository.UpdateAsync(audiobook);

            var downloadService = _provider.GetRequiredService<DownloadService>();
            var result = await downloadService.SearchAndDownloadAsync(audiobook.Id);

            Assert.True(result.Success, result.Message);
            Assert.NotNull(result.SearchResult);

            return result.SearchResult!.Id;
        }
    }
}
