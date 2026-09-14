/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search;

/// <summary>
/// LastSearchTime is what excludes a book from the next six hours of automatic search. A book
/// searched while an indexer was in failure backoff was not really searched, so stamping it would
/// mean a single transient blip silently costs a run of books an entire cycle.
/// </summary>
[Trait("Area", "Search")]
[Trait("Name", "AutomaticSearchDegradedCycleTests")]
[Trait("Category", "BackgroundWorkers")]
public sealed class AutomaticSearchDegradedCycleTests : BaseTests
{
    [Fact]
    [Trait("Method", "RunCycleAsync")]
    [Trait("Scenario", "IndexerInBackoff")]
    public async Task RunCycleAsync_AnIndexerWasSkipped_LeavesTheBookEligibleForTheNextCycle()
    {
        // Given
        var audiobook = Audiobook();
        var audiobookRepository = AudiobookRepository(audiobook);
        var processor = CreateProcessor(audiobookRepository, anyIndexerBlocked: true);

        // When
        await processor.RunCycleAsync(CancellationToken.None);

        // Then: no stamp, so the next cycle picks this book up again rather than treating a
        // degraded indexer set as a completed search.
        Assert.Null(audiobook.LastSearchTime);
        audiobookRepository.Verify(r => r.UpdateAsync(It.IsAny<Audiobook>()), Times.Never);
    }

    [Fact]
    [Trait("Method", "RunCycleAsync")]
    [Trait("Scenario", "EveryIndexerHealthy")]
    public async Task RunCycleAsync_NoIndexerSkipped_StampsLastSearchTimeAsBefore()
    {
        // Given: the control. Without it an implementation that never stamps anything at all
        // passes the test above, and every monitored book would be re-searched every cycle forever.
        var audiobook = Audiobook();
        var audiobookRepository = AudiobookRepository(audiobook);
        var processor = CreateProcessor(audiobookRepository, anyIndexerBlocked: false);

        // When
        await processor.RunCycleAsync(CancellationToken.None);

        // Then
        Assert.NotNull(audiobook.LastSearchTime);
        audiobookRepository.Verify(r => r.UpdateAsync(It.IsAny<Audiobook>()), Times.Once);
    }

    [Fact]
    [Trait("Method", "RunCycleAsync")]
    [Trait("Scenario", "NoSearchRan")]
    public async Task RunCycleAsync_BookSkippedBeforeAnySearch_IsStampedEvenWhileAnIndexerIsBlocked()
    {
        // Given: a book with no quality profile never reaches an indexer at all. Withholding the
        // stamp here would keep it eligible every cycle for as long as the unrelated indexer stayed
        // blocked, which is a different bug from the one this feature is fixing.
        var audiobook = Audiobook();
        audiobook.QualityProfile = null;
        var audiobookRepository = AudiobookRepository(audiobook);
        var processor = CreateProcessor(audiobookRepository, anyIndexerBlocked: true);

        // When
        await processor.RunCycleAsync(CancellationToken.None);

        // Then
        Assert.NotNull(audiobook.LastSearchTime);
    }

    private AutomaticSearchProcessor CreateProcessor(
        Mock<IAudiobookRepository> audiobookRepository,
        bool anyIndexerBlocked)
    {
        var searchService = new Mock<ISearchService>();
        searchService
            .Setup(s => s.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<SearchSortBy>(),
                It.IsAny<SearchSortDirection>(),
                It.IsAny<bool>()))
            .ReturnsAsync(new List<SearchResult>());

        var downloadRepository = new Mock<IDownloadRepository>();
        downloadRepository
            .Setup(r => r.GetByAudiobookIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Download>());

        var fileRepository = new Mock<IAudiobookFileRepository>();
        fileRepository
            .Setup(r => r.GetByAudiobookIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AudiobookFile>());

        var statusService = new Mock<IIndexerStatusService>();
        statusService
            .Setup(s => s.AnyEnabledIndexerBlockedAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(anyIndexerBlocked);

        _services.AddSingleton(audiobookRepository.Object);
        _services.AddSingleton(downloadRepository.Object);
        _services.AddSingleton(fileRepository.Object);
        _services.AddSingleton(searchService.Object);
        _services.AddSingleton(statusService.Object);
        _services.AddSingleton(Mock.Of<IDownloadService>());
        Init();

        return new AutomaticSearchProcessor(
            _provider.GetRequiredService<ILogger<AutomaticSearchProcessor>>(),
            _provider.GetRequiredService<IServiceScopeFactory>());
    }

    private static Mock<IAudiobookRepository> AudiobookRepository(Audiobook audiobook)
    {
        var repository = new Mock<IAudiobookRepository>();
        repository
            .Setup(r => r.GetMonitoredAudiobooksForSearchAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([audiobook]);
        repository
            .Setup(r => r.UpdateAsync(It.IsAny<Audiobook>()))
            .ReturnsAsync(true);
        return repository;
    }

    private static Audiobook Audiobook() => new()
    {
        Id = 1,
        Title = "Alices Adventures in Wonderland",
        Monitored = true,
        QualityProfileId = 1,
        QualityProfile = new QualityProfile { Id = 1, Name = "Any" }
    };
}
