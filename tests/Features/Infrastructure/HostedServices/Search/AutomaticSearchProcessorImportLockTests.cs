/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search
{
    // #1218: a manual import holds the audiobook operation lock while it copies. The automatic
    // search used to read the book's files before a long indexer search and grab on that stale
    // read, so a book mid-import looked empty and was downloaded a second time.
    [Trait("Name", "AutomaticSearchProcessorImportLockTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class AutomaticSearchProcessorImportLockTests : BaseTests
    {
        private const string Title = "Artemis";
        private const string Author = "Andy Weir";
        private const string ReleaseTitle = "Andy Weir - Artemis - 2017 - M4B";

        [Fact]
        public async Task RunCycleAsync_FileRegisteredWhileImportHoldsBook_DoesNotGrab()
        {
            var download = InitWithAcceptedRelease();
            var audiobook = await SeedMonitoredAudiobookAsync();
            var coordinator = _provider.GetRequiredService<IAudiobookOperationCoordinator>();
            var importStarted = new TaskCompletionSource();
            var importMayFinish = new TaskCompletionSource();
            var import = coordinator.ExecuteExclusiveAsync(audiobook.Id, async _ =>
            {
                importStarted.SetResult();
                await importMayFinish.Task;
            });
            await importStarted.Task;

            var cycle = RunCycleAsync();
            await Task.Delay(200);
            Assert.False(cycle.IsCompleted, "the cycle must wait for the import to release the book");
            await AddCutoffMeetingFileAsync(audiobook);
            importMayFinish.SetResult();
            await import;
            await cycle;

            VerifyGrabCount(download, Times.Never());
        }

        [Fact]
        public async Task RunCycleAsync_NoImportHoldsBook_Grabs()
        {
            var download = InitWithAcceptedRelease();
            await SeedMonitoredAudiobookAsync();

            await RunCycleAsync();

            VerifyGrabCount(download, Times.Once());
        }

        [Fact]
        public async Task RunCycleAsync_FileAlreadyRegistered_DoesNotGrab()
        {
            var download = InitWithAcceptedRelease();
            var audiobook = await SeedMonitoredAudiobookAsync();
            await AddCutoffMeetingFileAsync(audiobook);

            await RunCycleAsync();

            VerifyGrabCount(download, Times.Never());
        }

        private Mock<IDownloadService> InitWithAcceptedRelease()
        {
            var search = new Mock<ISearchService>();
            search.Setup(s => s.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<List<string>?>(),
                    It.IsAny<SearchSortBy>(),
                    It.IsAny<SearchSortDirection>(),
                    It.IsAny<bool>()))
                .ReturnsAsync(new List<SearchResult>
                {
                    new() { Title = ReleaseTitle, DownloadType = DirectDownloadMetadataKeys.ClientId }
                });
            var download = new Mock<IDownloadService>();
            download.Setup(d => d.StartDownloadAsync(
                    It.IsAny<SearchResult>(), It.IsAny<string>(), It.IsAny<int?>()))
                .ReturnsAsync("download-1");
            var quality = new Mock<IQualityProfileService>();
            quality.Setup(q => q.ScoreSearchResults(
                    It.IsAny<List<SearchResult>>(), It.IsAny<QualityProfile>()))
                .ReturnsAsync((List<SearchResult> results, QualityProfile _) =>
                    results.Select(r => new QualityScore { SearchResult = r, TotalScore = 100 }).ToList());
            Init(builder =>
            {
                builder.WithSingleton(search.Object);
                builder.WithSingleton(download.Object);
                builder.WithSingleton(quality.Object);
            });
            return download;
        }

        private static void VerifyGrabCount(Mock<IDownloadService> download, Times times) =>
            download.Verify(d => d.StartDownloadAsync(
                It.IsAny<SearchResult>(), It.IsAny<string>(), It.IsAny<int?>()), times);

        private async Task<Audiobook> SeedMonitoredAudiobookAsync() =>
            await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle(Title)
                .WithAuthor(Author)
                .WithMonitored()
                .WithQualityProfile(new QualityProfile
                {
                    Name = "AAC",
                    CutoffQuality = "AAC 256kbps",
                    Qualities = new List<QualityDefinition>
                    {
                        new() { Quality = "AAC 256kbps", Codec = "AAC", Bitrate = 256, Priority = 0, Allowed = true }
                    }
                })
                .Build());

        private async Task AddCutoffMeetingFileAsync(Audiobook audiobook) =>
            await _audiobookFileRepository.AddAsync(new AudiobookFileBuilder()
                .WithAudiobook(audiobook)
                .WithPath(@"C:\books\artemis\book.m4b")
                .WithFormat("m4b")
                .WithCoded("aac")
                .WithBitrate(256_000)
                .WithSize(123_456)
                .Build());

        private async Task RunCycleAsync()
        {
            var processor = new AutomaticSearchProcessor(
                NullLogger<AutomaticSearchProcessor>.Instance,
                _provider.GetRequiredService<IServiceScopeFactory>());
            await processor.RunCycleAsync(CancellationToken.None);
        }
    }
}
