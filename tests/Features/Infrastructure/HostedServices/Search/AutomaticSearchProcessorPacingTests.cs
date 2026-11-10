/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Collections.Concurrent;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search
{
    // The per-book delay paces indexer requests. A book skipped before its search (here: quality
    // cutoff already met) sends none, so it must not wait the delay, while consecutive real
    // searches still do. The delay goes through a recording TimeProvider, so no test sleeps.
    [Trait("Name", "AutomaticSearchProcessorPacingTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class AutomaticSearchProcessorPacingTests : BaseTests
    {
        private const int BookDelaySeconds = 180;

        private readonly RecordingTimeProvider _timeProvider = new();
        private readonly Mock<ISearchService> _search = new();

        [Fact]
        public async Task RunCycleAsync_CutoffMetBooksAndOneWantedBook_SearchesTheWantedBookWithoutWaiting()
        {
            await InitAsync();
            await SeedCutoffMetAudiobookAsync("A Princess of Mars", "Edgar Rice Burroughs");
            await SeedCutoffMetAudiobookAsync("Pellucidar", "Edgar Rice Burroughs");
            await SeedCutoffMetAudiobookAsync("Tarzan of the Apes", "Edgar Rice Burroughs");
            await SeedWantedAudiobookAsync("The Wonderful Wizard of Oz", "L. Frank Baum");

            await RunCycleAsync();

            VerifySearched("The Wonderful Wizard of Oz L. Frank Baum", Times.Once());
            VerifySearchedOnlyFor("The Wonderful Wizard of Oz");
            Assert.Empty(_timeProvider.Delays);
        }

        [Fact]
        public async Task RunCycleAsync_TwoWantedBooks_StillWaitsTheBookDelayBetweenSearches()
        {
            await InitAsync();
            await SeedWantedAudiobookAsync("The Wonderful Wizard of Oz", "L. Frank Baum");
            await SeedWantedAudiobookAsync("The Jungle Book", "Rudyard Kipling");

            await RunCycleAsync();

            VerifySearched("The Wonderful Wizard of Oz L. Frank Baum", Times.Once());
            VerifySearched("The Jungle Book Rudyard Kipling", Times.Once());
            Assert.Equal(new[] { TimeSpan.FromSeconds(BookDelaySeconds) }, _timeProvider.Delays);
        }

        [Fact]
        public async Task RunCycleAsync_CutoffMetBookAmongWantedBooks_WaitsOncePerSearchNotOncePerBook()
        {
            await InitAsync();
            await SeedWantedAudiobookAsync("The Wonderful Wizard of Oz", "L. Frank Baum");
            await SeedCutoffMetAudiobookAsync("A Princess of Mars", "Edgar Rice Burroughs");
            await SeedWantedAudiobookAsync("The Jungle Book", "Rudyard Kipling");

            await RunCycleAsync();

            VerifySearchedOnlyFor("The Wonderful Wizard of Oz", "The Jungle Book");
            Assert.Equal(new[] { TimeSpan.FromSeconds(BookDelaySeconds) }, _timeProvider.Delays);
        }

        [Fact]
        public async Task RunCycleAsync_ActiveDownloadAndOneWantedBook_SearchesTheWantedBookWithoutWaiting()
        {
            await InitAsync();
            var downloading = await SeedWantedAudiobookAsync("The Three Musketeers", "Alexandre Dumas");
            await AddDownloadAsync(downloading, DownloadStatus.Queued);
            await SeedWantedAudiobookAsync("The Wonderful Wizard of Oz", "L. Frank Baum");

            await RunCycleAsync();

            VerifySearchedOnlyFor("The Wonderful Wizard of Oz");
            Assert.Empty(_timeProvider.Delays);
        }

        [Fact]
        public async Task RunCycleAsync_DownloadStartsDuringTheWait_DoesNotGrabTheBookAgain()
        {
            var download = new Mock<IDownloadService>();
            var grabbedIds = new ConcurrentQueue<int>();
            download.Setup(d => d.StartDownloadAsync(
                    It.IsAny<SearchResult>(), It.IsAny<string>(), It.IsAny<int?>()))
                .Callback((SearchResult _, string _, int? audiobookId) => grabbedIds.Enqueue(audiobookId!.Value))
                .ReturnsAsync("download-1");
            // Accept every result with a positive score, so each searched book would be grabbed.
            var quality = new Mock<IQualityProfileService>();
            quality.Setup(q => q.ScoreSearchResults(
                    It.IsAny<List<SearchResult>>(), It.IsAny<QualityProfile>()))
                .ReturnsAsync((List<SearchResult> results, QualityProfile _) =>
                    results.Select(r => new QualityScore { SearchResult = r, TotalScore = 100 }).ToList());
            await InitAsync(
                new SearchResult { Title = "M4B", DownloadType = DirectDownloadMetadataKeys.ClientId },
                builder =>
                {
                    builder.WithSingleton(download.Object);
                    builder.WithSingleton(quality.Object);
                });
            var books = new[]
            {
                await SeedWantedAudiobookAsync("The Wonderful Wizard of Oz", "L. Frank Baum"),
                await SeedWantedAudiobookAsync("The Jungle Book", "Rudyard Kipling")
            };
            // While the cycle waits before the second search, something else (a manual grab) starts a
            // download for the book that has not been grabbed yet.
            _timeProvider.OnDelay = () =>
            {
                foreach (var book in books.Where(b => !grabbedIds.Contains(b.Id)))
                {
                    AddDownloadAsync(book, DownloadStatus.Queued).GetAwaiter().GetResult();
                }
            };

            await RunCycleAsync();

            Assert.Single(_timeProvider.Delays);
            Assert.Single(grabbedIds);
        }

        private async Task InitAsync(
            SearchResult? searchResult = null,
            Action<ServiceCollectionBuilder>? configureBuilder = null)
        {
            _search.Setup(s => s.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<List<string>?>(),
                    It.IsAny<SearchSortBy>(),
                    It.IsAny<SearchSortDirection>(),
                    It.IsAny<bool>()))
                .ReturnsAsync(searchResult == null ? new List<SearchResult>() : new List<SearchResult> { searchResult });
            Init(builder =>
            {
                builder.WithSingleton(_search.Object);
                configureBuilder?.Invoke(builder);
            });

            var settings = new ApplicationSettingsBuilder().Build();
            settings.AutomaticSearchBookDelaySeconds = BookDelaySeconds;
            await _applicationSettingsRepository.SaveAsync(settings);
        }

        private async Task<Audiobook> SeedWantedAudiobookAsync(string title, string author) =>
            await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle(title)
                .WithAuthor(author)
                .WithMonitored()
                .WithQualityProfile(CreateMp3Profile())
                .Build());

        private async Task SeedCutoffMetAudiobookAsync(string title, string author)
        {
            var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                .WithTitle(title)
                .WithAuthor(author)
                .WithMonitored()
                .WithQualityProfile(CreateMp3Profile())
                .Build());
            await AddDownloadAsync(audiobook, DownloadStatus.Completed, quality: "MP3");

            // Precondition: the book is skipped by the cutoff check, not by some other guard.
            Assert.True(await AudiobookQualityCutoffEvaluator.IsQualityCutoffMetAsync(
                audiobook, _downloadRepository, _audiobookFileRepository));
        }

        private async Task AddDownloadAsync(Audiobook audiobook, DownloadStatus status, string? quality = null)
        {
            var download = new DownloadBuilder()
                .WithId($"{status}-{audiobook.Id}")
                .WithAudiobookId(audiobook.Id)
                .WithStatus(status)
                .WithTitle(audiobook.Title!)
                .Build();
            if (quality != null)
            {
                download.Metadata["Quality"] = quality;
            }
            await _downloadRepository.AddAsync(download);
        }

        private static QualityProfile CreateMp3Profile() => new()
        {
            Name = "MP3",
            CutoffQuality = "MP3",
            Qualities = new List<QualityDefinition> { new() { Quality = "MP3", Priority = 1 } }
        };

        private void VerifySearched(string query, Times times) =>
            _search.Verify(s => s.SearchAsync(
                    query,
                    It.IsAny<string?>(),
                    It.IsAny<List<string>?>(),
                    It.IsAny<SearchSortBy>(),
                    It.IsAny<SearchSortDirection>(),
                    It.IsAny<bool>()),
                times);

        private void VerifySearchedOnlyFor(params string[] titles) =>
            _search.Verify(s => s.SearchAsync(
                    It.Is<string>(q => !titles.Any(t => q.StartsWith(t))),
                    It.IsAny<string?>(),
                    It.IsAny<List<string>?>(),
                    It.IsAny<SearchSortBy>(),
                    It.IsAny<SearchSortDirection>(),
                    It.IsAny<bool>()),
                Times.Never);

        private async Task RunCycleAsync()
        {
            var processor = new AutomaticSearchProcessor(
                NullLogger<AutomaticSearchProcessor>.Instance,
                _provider.GetRequiredService<IServiceScopeFactory>(),
                _timeProvider);
            await processor.RunCycleAsync(CancellationToken.None);
        }

        // Records every requested delay and completes it at once, so a 180 s pacing delay costs
        // nothing in the test but stays observable. OnDelay runs inside the wait, before it ends.
        private sealed class RecordingTimeProvider : TimeProvider
        {
            private readonly ConcurrentQueue<TimeSpan> _delays = new();

            public IReadOnlyCollection<TimeSpan> Delays => _delays.ToArray();

            public Action? OnDelay { get; set; }

            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                _delays.Enqueue(dueTime);
                OnDelay?.Invoke();
                ThreadPool.QueueUserWorkItem(_ => callback(state));
                return new CompletedTimer();
            }

            private sealed class CompletedTimer : ITimer
            {
                public bool Change(TimeSpan dueTime, TimeSpan period) => false;

                public void Dispose()
                {
                }

                public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            }
        }
    }
}
