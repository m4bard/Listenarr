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
using System.Runtime.CompilerServices;
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

        /// <summary>
        /// The score both the usenet shape and <see cref="TorrentMatchingUsenetScore"/> land on,
        /// also below the clamp.
        /// </summary>
        private const int ExpectedCrossProtocolScore = 86;

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
            string quality = "MP3 320kbps",
            string format = "mp3",
            string language = "English")
        {
            return new SearchResult
            {
                Id = id,
                Title = title,
                Artist = "A Public Domain Author",
                DownloadType = "Torrent",
                MagnetLink = $"magnet:?xt=urn:btih:{InfoHashFor(id)}&dn={Uri.EscapeDataString(title)}",
                Format = format,
                Quality = quality,
                Language = language,
                Seeders = seeders,
                Leechers = leechers,
                Size = sizeBytes
            };
        }

        /// <summary>
        /// A torrent built to land on the same score as the usenet shape above, so the two can
        /// be compared without the score deciding. Both take the language mismatch penalty; the
        /// torrent's quality deduction and its seeders bonus cancel out, and usenet takes
        /// neither. Both come out at <see cref="ExpectedCrossProtocolScore"/>.
        /// </summary>
        private static SearchResult TorrentMatchingUsenetScore(string id, string title, int seeders = 20)
        {
            return Torrent(id, title, seeders: seeders, leechers: 0, quality: "M4B", format: "m4b", language: "German");
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

        /// <summary>
        /// The same reversal, driven through DownloadService.SearchAndDownloadAsync so the
        /// selection expression at that site is the thing under test.
        /// </summary>
        /// <remarks>
        /// The scoring service is mocked to hand back scored results in the order the indexer
        /// returned them. That matters: the real QualityProfileService now returns an already
        /// tiebroken list, and LINQ ordering is stable, so with a real scoring service this test
        /// passes whether or not DownloadService does any tiebreaking of its own. Measured, by
        /// removing only the DownloadService tiebreak and watching all ten tests still pass.
        /// Feeding the site an unordered list is what makes it discriminate.
        /// </remarks>
        [Fact]
        public async Task SearchAndDownload_GrabsSameRelease_WhenScoredResultsArriveInIndexerOrder()
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

            AssertAllScoresTied(forward, ExpectedCrossProtocolScore);

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

        [Fact]
        public async Task Tiebreak_PrefersUsenet_WhenProtocolsDifferAndScoresTie()
        {
            // Matches the family's default preferred protocol. Readarr and Sonarr both seed
            // their delay profile with PreferredProtocol = 1, which is Usenet.
            var torrent = TorrentMatchingUsenetScore("release-a", "Alpha Release");
            var usenet = Usenet("release-b", "Bravo Release", DateTime.UtcNow.AddDays(-30).ToString("O"));

            var forward = await ScoreAsync(torrent, usenet);
            var reversed = await ScoreAsync(usenet, torrent);

            // Control: the two protocols really did land on the same score, below the clamp.
            AssertAllScoresTied(forward, ExpectedCrossProtocolScore);
            Assert.True(ExpectedCrossProtocolScore < 100, "The tie must sit below the score clamp.");

            Assert.Equal("release-b", forward[0].SearchResult.Id);
            Assert.Equal("release-b", reversed[0].SearchResult.Id);
        }

        /// <summary>
        /// The protocol grouping is not decoration. Without it the chain is not transitive and a
        /// sort handed a cycle answers differently depending on input order. This is that cycle:
        /// peers puts the well seeded torrent ahead of the quiet one, while the identity fallback
        /// puts the usenet release between them.
        /// </summary>
        [Fact]
        public async Task Tiebreak_IsTransitive_AcrossAMixedProtocolCycle()
        {
            var quietTorrent = TorrentMatchingUsenetScore("release-a", "Alpha Release", seeders: 20);
            var usenet = Usenet("release-b", "Bravo Release", DateTime.UtcNow.AddDays(-30).ToString("O"));
            var busyTorrent = TorrentMatchingUsenetScore("release-c", "Charlie Release", seeders: 5000);

            var candidates = new[] { quietTorrent, usenet, busyTorrent };

            // Control: all three tie on score, so only the tiebreak can order them.
            AssertAllScoresTied(await ScoreAsync(candidates), ExpectedCrossProtocolScore);

            foreach (var permutation in Permutations(candidates))
            {
                var scored = await ScoreAsync(permutation);
                Assert.Equal("release-b", scored[0].SearchResult.Id);
            }
        }

        private static IEnumerable<SearchResult[]> Permutations(SearchResult[] candidates)
        {
            for (var first = 0; first < candidates.Length; first++)
            {
                for (var second = 0; second < candidates.Length; second++)
                {
                    if (second == first)
                    {
                        continue;
                    }

                    var third = 3 - first - second;
                    yield return [candidates[first], candidates[second], candidates[third]];
                }
            }
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

        /// <summary>
        /// The third site with the same expression: the automatic search cycle. Driven the same
        /// way, with scored results handed back in indexer order, so the ordering in
        /// AutomaticSearchProcessor is what decides.
        /// </summary>
        [Fact]
        public async Task AutomaticSearch_QueuesSameRelease_WhenScoredResultsArriveInIndexerOrder()
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

            var forwardGrab = await RunAutomaticSearchCycleAsync(forward);
            var reversedGrab = await RunAutomaticSearchCycleAsync(reversed);

            Assert.Equal("Alpha Release", forwardGrab);
            Assert.Equal("Alpha Release", reversedGrab);
        }

        // ------------------------------------------------------------------
        // Harnesses that drive the real selection sites.
        //
        // Each one mocks IQualityProfileService so scored results arrive in the order the
        // indexer returned them. That is what makes these tests discriminate: the real
        // QualityProfileService now returns an already tiebroken list, and LINQ ordering is
        // stable, so with a real scoring service they would pass whether or not the consuming
        // site tiebreaks at all. Measured, by removing only the DownloadService tiebreak and
        // watching every test still pass.
        // ------------------------------------------------------------------

        /// <summary>
        /// Registers the search, gateway and scoring doubles, rebuilds the provider, and seeds the
        /// settings and download client the submission path needs. The returned holder is filled
        /// with the list the scoring double handed back, for the ordering control.
        /// </summary>
        private async Task<StrongBox<List<QualityScore>?>> ArrangeSelectionHarnessAsync(List<SearchResult> candidates)
        {
            var handedBack = new StrongBox<List<QualityScore>?>(null);

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

            var realScorer = CreateQualityProfileService();
            var scoringMock = new Mock<IQualityProfileService>();
            scoringMock
                .Setup(service => service.ScoreSearchResults(
                    It.IsAny<List<SearchResult>>(),
                    It.IsAny<QualityProfile>()))
                .Returns(async (List<SearchResult> results, QualityProfile profileArgument) =>
                {
                    var scored = new List<QualityScore>();
                    foreach (var result in results)
                    {
                        scored.Add(await realScorer.ScoreSearchResult(result, profileArgument));
                    }

                    handedBack.Value = scored;
                    return scored;
                });

            _services.AddSingleton(searchServiceMock.Object);
            _services.AddSingleton(gatewayMock.Object);
            _services.AddSingleton(scoringMock.Object);
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

            return handedBack;
        }

        private async Task<Audiobook> ArrangeMonitoredAudiobookAsync()
        {
            var profile = await _qualityProfileRepository.AddAsync(BuildProfile());
            var audiobook = await CreateAudiobook();
            audiobook.QualityProfileId = profile.Id;
            audiobook.Monitored = true;
            audiobook.LastSearchTime = null;
            await _audiobookRepository.UpdateAsync(audiobook);
            return audiobook;
        }

        private static void AssertHandedBackInIndexerOrder(
            List<SearchResult> candidates,
            StrongBox<List<QualityScore>?> handedBack)
        {
            Assert.NotNull(handedBack.Value);
            Assert.Equal(
                candidates.Select(candidate => candidate.Id).ToArray(),
                handedBack.Value!.Select(score => score.SearchResult.Id).ToArray());
        }

        private async Task<string?> RunSearchAndDownloadAsync(List<SearchResult> candidates)
        {
            var handedBack = await ArrangeSelectionHarnessAsync(candidates);
            var audiobook = await ArrangeMonitoredAudiobookAsync();

            var downloadService = _provider.GetRequiredService<DownloadService>();
            var result = await downloadService.SearchAndDownloadAsync(audiobook.Id);

            Assert.True(result.Success, result.Message);
            Assert.NotNull(result.SearchResult);

            // Control: the list DownloadService had to order really was in indexer order, so a
            // pass cannot be explained by something upstream having sorted it already.
            AssertHandedBackInIndexerOrder(candidates, handedBack);

            return result.SearchResult!.Id;
        }

        private async Task<string?> RunAutomaticSearchCycleAsync(List<SearchResult> candidates)
        {
            var handedBack = await ArrangeSelectionHarnessAsync(candidates);
            var audiobook = await ArrangeMonitoredAudiobookAsync();

            var processor = new AutomaticSearchProcessor(
                _provider.GetRequiredService<ILogger<AutomaticSearchProcessor>>(),
                _provider.GetRequiredService<IServiceScopeFactory>());

            await processor.RunCycleAsync(CancellationToken.None);

            var queued = await _downloadRepository.GetByAudiobookIdAsync(audiobook.Id);
            var download = Assert.Single(queued);

            // Control: the list the cycle had to order really was in indexer order.
            AssertHandedBackInIndexerOrder(candidates, handedBack);

            return download.Title;
        }
    }
}
