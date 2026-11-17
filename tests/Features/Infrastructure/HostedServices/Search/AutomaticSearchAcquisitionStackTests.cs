/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Application.Search.Scheduling;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Listenarr.Tests.Features.Infrastructure.HostedServices.Search.Sweep;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search
{
    // The grab rules the sweep carried before its rewrite, driven through the acquisition the
    // rewrite moved them into: the indexer's client binding, the shutdown token on the download
    // start, one ranking for the log and the grab, and the walk past candidates that are no upgrade.
    [Trait("Name", "AutomaticSearchAcquisitionStackTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class AutomaticSearchAcquisitionStackTests : BaseTests
    {
        private static readonly string[] LadderOrder =
        {
            "AAC 320kbps", "AAC 256kbps", "AAC 192kbps", "AAC 128kbps", "AAC 64kbps",
            "MP3 320kbps", "MP3 256kbps", "MP3 VBR", "MP3 192kbps", "MP3 128kbps", "MP3 64kbps",
        };

        private readonly Mock<IDownloadService> _downloadService = new();
        private readonly Mock<IQualityProfileService> _qualityProfileService = new();
        private readonly RecordingLogger<AutomaticSearchAcquisition> _log = new();
        private readonly Dictionary<string, int> _scoreByTitle = new();

        [Theory]
        [InlineData("qb-archive", "qb-archive")]
        [InlineData(null, "qb-local")]
        public async Task TorrentFromAnIndexer_GoesToItsBoundClient_OtherwiseByPriority(string? boundClient, string expectedClient)
        {
            // The second row is the control: the same two clients with no binding, so priority decides.
            // The priority-10 client is saved first and sorts first by id, so only its priority keeps
            // it from being picked.
            var book = await ArrangeAsync(new QualityProfile { Name = "Any" });
            await SaveTorrentClientAsync("qb-archive", priority: 10);
            await SaveTorrentClientAsync("qb-local", priority: 1);
            var tracker = await _indexerRepository.AddAsync(new IndexerBuilder()
                .WithName("Private Tracker")
                .WithUrl("https://tracker.invalid")
                .WithEnabled()
                .Build());
            tracker.DownloadClientId = boundClient;
            await _indexerRepository.UpdateAsync(tracker);

            var candidate = Candidate("M4B", score: 100);
            candidate.DownloadType = "Torrent";
            candidate.MagnetLink = "magnet:?xt=urn:btih:0000000000000000000000000000000000000000";
            candidate.IndexerId = tracker.Id;
            var result = await AcquireAsync(book, CancellationToken.None, candidate);

            Assert.Equal(BookSearchOutcome.Satisfied, result.Outcome);
            _downloadService.Verify(d => d.StartDownloadAsync(
                It.IsAny<SearchResult>(), expectedClient, book.Id, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DownloadStart_IsGivenTheCycleToken()
        {
            // The download service tells a shutdown from a client timeout only by this token; without
            // it a timeout is taken for a shutdown and the grab leaves no trace.
            var book = await ArrangeAsync(new QualityProfile { Name = "Any" });
            using var cycle = new CancellationTokenSource();

            var result = await AcquireAsync(book, cycle.Token, Candidate("M4B", score: 100));

            Assert.Equal(BookSearchOutcome.Satisfied, result.Outcome);
            _downloadService.Verify(d => d.StartDownloadAsync(
                It.IsAny<SearchResult>(), DirectDownloadMetadataKeys.ClientId, book.Id, cycle.Token), Times.Once);
        }

        [Fact]
        public async Task ScoredLog_ListsCandidatesInTheOrderTheGrabConsidersThem()
        {
            // The MP3 has the higher score and the profile prefers the AAC: the grab takes the AAC, so
            // a log sorted by score alone would list first a release the grab passed over.
            var book = await ArrangeAsync(LadderProfile());

            var result = await AcquireAsync(book, CancellationToken.None,
                Candidate("MP3 320kbps", score: 200),
                Candidate("AAC 320kbps", score: 100));

            Assert.Equal(BookSearchOutcome.Satisfied, result.Outcome);
            _downloadService.Verify(d => d.StartDownloadAsync(
                It.Is<SearchResult>(r => r.Quality == "AAC 320kbps"), It.IsAny<string>(), book.Id, It.IsAny<CancellationToken>()), Times.Once);
            var listed = _log.Entries
                .Select(entry => entry.Message)
                .Where(message => message.Contains("] Score: ", StringComparison.Ordinal))
                .ToList();
            Assert.Equal(2, listed.Count);
            Assert.Contains("Quality: AAC 320kbps", listed[0], StringComparison.Ordinal);
        }

        [Fact]
        public async Task TopCandidateIsNoUpgrade_TheBestOneThatIsAnUpgradeIsGrabbed()
        {
            // A bare "AAC" is ranked by codec but cannot be resolved by the upgrade check's exact
            // profile name, so it heads the ranking and is no upgrade. The MP3 320kbps behind it is.
            var book = await ArrangeAsync(LadderProfile());
            var held = new DownloadBuilder()
                .WithId("held")
                .WithAudiobookId(book.Id)
                .WithStatus(DownloadStatus.Completed)
                .Build();
            held.Metadata = new Dictionary<string, object> { ["Quality"] = "MP3 128kbps" };
            await _downloadRepository.AddAsync(held);

            var result = await AcquireAsync(book, CancellationToken.None,
                Candidate("AAC", score: 100),
                Candidate("MP3 320kbps", score: 100));

            Assert.Equal(BookSearchOutcome.Satisfied, result.Outcome);
            _downloadService.Verify(d => d.StartDownloadAsync(
                It.Is<SearchResult>(r => r.Quality == "MP3 320kbps"), It.IsAny<string>(), book.Id, It.IsAny<CancellationToken>()), Times.Once);
        }

        private async Task<Audiobook> ArrangeAsync(QualityProfile profile)
        {
            _downloadService
                .Setup(d => d.StartDownloadAsync(It.IsAny<SearchResult>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("download-1");
            _qualityProfileService
                .Setup(q => q.ScoreSearchResults(It.IsAny<List<SearchResult>>(), It.IsAny<QualityProfile>(), It.IsAny<bool>()))
                .ReturnsAsync((List<SearchResult> results, QualityProfile _, bool _) =>
                    results.Select(r => new QualityScore { SearchResult = r, TotalScore = _scoreByTitle[r.Title] }).ToList());
            Init(builder =>
            {
                builder.WithSingleton(_downloadService.Object);
                builder.WithSingleton(_qualityProfileService.Object);
                builder.WithSingleton<ILogger<AutomaticSearchAcquisition>>(_log);
            });
            return await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle("A Princess of Mars")
                .WithAuthor("Edgar Rice Burroughs")
                .WithMonitored()
                .WithQualityProfile(profile)
                .Build());
        }

        private Task<DownloadClientConfiguration> SaveTorrentClientAsync(string id, int priority) =>
            _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfiguration
            {
                Id = id,
                Name = id,
                Type = "qbittorrent",
                Host = "localhost",
                Port = 8080,
                Priority = priority,
                IsEnabled = true
            });

        private async Task<AcquisitionResult> AcquireAsync(Audiobook book, CancellationToken ct, params IndexerSearchResult[] candidates)
        {
            using var scope = _provider.CreateScope();
            var acquisition = scope.ServiceProvider.GetRequiredService<IAutomaticSearchAcquisition>();
            var loaded = await scope.ServiceProvider.GetRequiredService<IAudiobookRepository>().GetByIdAsync(book.Id);
            return await acquisition.AcquireAsync(loaded!, candidates, SearchOrigin.Sweep, ct);
        }

        private static QualityProfile LadderProfile() => new()
        {
            Name = "Ladder",
            CutoffQuality = "AAC 320kbps",
            Qualities = LadderOrder
                .Select((quality, index) => new QualityDefinition { Quality = quality, Allowed = true, Priority = index })
                .ToList()
        };

        private IndexerSearchResult Candidate(string quality, int score)
        {
            var candidate = new IndexerSearchResult
            {
                Title = $"Edgar Rice Burroughs - A Princess of Mars - {quality}",
                Quality = quality,
                DownloadType = DirectDownloadMetadataKeys.ClientId
            };
            _scoreByTitle[candidate.Title] = score;
            return candidate;
        }
    }
}
