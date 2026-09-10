/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Metadata.Refresh;

/// <summary>
/// The per-book unit of work, with no HttpContext anywhere near it. These pin the
/// outcomes a background caller has to distinguish, and the request count a book costs.
/// </summary>
[Trait("Area", "Metadata")]
[Trait("Name", "MetadataRefreshServiceTests")]
[Trait("Category", "Application")]
public class MetadataRefreshServiceTests : BaseTests
{
    private sealed class CountingBudget : IMetadataRefreshBudget
    {
        public int RequestsSpent { get; private set; }
        public int ThrottleSignals { get; private set; }
        public TimeSpan? LastRetryAfter { get; private set; }
        public int GrantLimit { get; set; } = int.MaxValue;

        public Task<bool> ChargeAsync(CancellationToken cancellationToken)
        {
            if (RequestsSpent >= GrantLimit)
            {
                return Task.FromResult(false);
            }

            RequestsSpent++;
            return Task.FromResult(true);
        }

        public void ApplyThrottleSignal(TimeSpan? retryAfter)
        {
            ThrottleSignals++;
            LastRetryAfter = retryAfter;
        }
    }

    private static Audiobook BookWithAsin(string asin, string? region = "us") => new()
    {
        Id = 1,
        Title = "A Voyage Downriver",
        Authors = ["Test Author"],
        ExternalIdentifiers =
        [
            new AudiobookExternalIdentifier
            {
                Type = AudiobookExternalIdentifierType.Asin,
                ValueRaw = asin,
                ValueNormalized = asin,
                Region = region,
                IsPrimary = true,
                Source = AudiobookExternalIdentifierSource.Manual
            }
        ]
    };

    private static MetadataRefreshService CreateService(
        Audiobook? book,
        Mock<IAudiobookMetadataService> metadata,
        Mock<IAudiobookRepository>? repositoryOverride = null)
    {
        var repository = repositoryOverride ?? new Mock<IAudiobookRepository>();
        if (repositoryOverride == null)
        {
            repository.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync(book);
            repository
                .Setup(r => r.GetByIdSnapshotAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(book);
            repository.Setup(r => r.UpdateAsync(It.IsAny<Audiobook>())).ReturnsAsync(true);
        }

        var coordinator = new Mock<IAudiobookOperationCoordinator>();
        coordinator
            .Setup(c => c.ExecuteExclusiveAsync(
                It.IsAny<int>(),
                It.IsAny<Func<CancellationToken, Task<MetadataRefreshApplyResult>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<int, Func<CancellationToken, Task<MetadataRefreshApplyResult>>, CancellationToken>(
                (_, work, token) => work(token));

        var moveQueue = new Mock<IMoveQueueService>();
        moveQueue
            .Setup(m => m.EnsureFilesystemMutationAllowedAsync(
                It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        return new MetadataRefreshService(
            repository.Object,
            metadata.Object,
            new MetadataConverters(
                imageCacheService: null,
                Mock.Of<ILogger<MetadataConverters>>()),
            Mock.Of<IImageCacheService>(),
            coordinator.Object,
            moveQueue.Object,
            Mock.Of<ILogger<MetadataRefreshService>>(),
            asinLookupService: null);
    }

    [Fact]
    [Trait("Scenario", "FirstRegionHitCostsOneRequest")]
    public async Task RefreshAsync_ChargesOneRequest_WhenTheFirstRegionAnswers()
    {
        var metadata = new Mock<IAudiobookMetadataService>();
        metadata
            .Setup(m => m.GetMetadataAsync("B0FIRSTHIT", "us", false))
            .ReturnsAsync(new AudiobookMetadataEnvelope(
                new AudibleBookResponse { Asin = "B0FIRSTHIT", Title = "Refreshed Title" },
                "Audible",
                "https://example.invalid/product"));
        var budget = new CountingBudget();

        var result = await CreateService(BookWithAsin("B0FIRSTHIT"), metadata)
            .RefreshAsync(1, budget, CancellationToken.None);

        Assert.Equal(MetadataRefreshOutcome.Updated, result.Outcome);
        Assert.Equal(1, result.RequestsSpent);
        Assert.Equal(1, budget.RequestsSpent);
        Assert.Equal("us", result.Region);
    }

    [Fact]
    [Trait("Scenario", "ReportedCostIsPerBookNotPerRun")]
    public async Task RefreshAsync_ReportsWhatThisBookCost_WhenTheRunHasAlreadySpent()
    {
        var metadata = new Mock<IAudiobookMetadataService>();
        metadata
            .Setup(m => m.GetMetadataAsync("B0SECONDBK", "us", false))
            .ReturnsAsync(new AudiobookMetadataEnvelope(
                new AudibleBookResponse { Asin = "B0SECONDBK", Title = "Refreshed Title" },
                "Audible",
                "https://example.invalid/product"));
        var budget = new CountingBudget();
        await budget.ChargeAsync(CancellationToken.None);
        await budget.ChargeAsync(CancellationToken.None);

        var result = await CreateService(BookWithAsin("B0SECONDBK"), metadata)
            .RefreshAsync(1, budget, CancellationToken.None);

        Assert.Equal(MetadataRefreshOutcome.Updated, result.Outcome);
        Assert.Equal(1, result.RequestsSpent);
        Assert.Equal(3, budget.RequestsSpent);
    }

    [Fact]
    [Trait("Scenario", "MultiRegionMissChargesPerRequest")]
    public async Task RefreshAsync_ChargesEveryRegion_WhenEarlierRegionsMiss()
    {
        var metadata = new Mock<IAudiobookMetadataService>();
        metadata
            .Setup(m => m.GetMetadataAsync("B0THIRDHIT", It.IsAny<string>(), false))
            .ReturnsAsync((string _, string region, bool _) => region == "uk"
                ? new AudiobookMetadataEnvelope(
                    new AudibleBookResponse { Asin = "B0THIRDHIT", Title = "Found In The Third Region" },
                    "Audible",
                    "https://example.invalid/product")
                : null);
        var budget = new CountingBudget();

        var result = await CreateService(BookWithAsin("B0THIRDHIT", region: "de"), metadata)
            .RefreshAsync(1, budget, CancellationToken.None);

        Assert.Equal(MetadataRefreshOutcome.Updated, result.Outcome);
        Assert.Equal(3, result.RequestsSpent);
        Assert.Equal("uk", result.Region);
    }

    [Fact]
    [Trait("Scenario", "NoIdentifiersIsSkipped")]
    public async Task RefreshAsync_ReturnsSkipped_AndSpendsNothing_WhenNoIdentifiersExist()
    {
        var book = new Audiobook { Id = 1, Title = "No Identifiers", ExternalIdentifiers = [] };
        var budget = new CountingBudget();

        var result = await CreateService(book, new Mock<IAudiobookMetadataService>())
            .RefreshAsync(1, budget, CancellationToken.None);

        Assert.Equal(MetadataRefreshOutcome.Skipped, result.Outcome);
        Assert.Equal(0, budget.RequestsSpent);
    }

    [Fact]
    [Trait("Scenario", "ExhaustedIdentifiersIsNotFound")]
    public async Task RefreshAsync_ReturnsNotFound_WhenEveryRegionMisses()
    {
        var metadata = new Mock<IAudiobookMetadataService>();
        metadata
            .Setup(m => m.GetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), false))
            .ReturnsAsync((AudiobookMetadataEnvelope?)null);
        var budget = new CountingBudget();

        var result = await CreateService(BookWithAsin("B0NOTHINGX"), metadata)
            .RefreshAsync(1, budget, CancellationToken.None);

        Assert.Equal(MetadataRefreshOutcome.NotFound, result.Outcome);
        Assert.Equal(2, budget.RequestsSpent);
    }

    [Fact]
    [Trait("Scenario", "TransientFailureRetriesTwiceThenDefers")]
    public async Task RefreshAsync_Defers_AfterTwoRetries_AndReportsTheThrottleSignal()
    {
        var metadata = new Mock<IAudiobookMetadataService>();
        metadata
            .Setup(m => m.GetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), false))
            .ThrowsAsync(new HttpRequestException(
                "too many requests",
                inner: null,
                statusCode: System.Net.HttpStatusCode.TooManyRequests));
        var budget = new CountingBudget();

        var result = await CreateService(BookWithAsin("B0THROTTLE"), metadata)
            .RefreshAsync(1, budget, CancellationToken.None);

        Assert.Equal(MetadataRefreshOutcome.Deferred, result.Outcome);
        Assert.Equal(3, budget.ThrottleSignals);
        metadata.Verify(
            m => m.GetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), false),
            Times.Exactly(3));
    }

    [Fact]
    [Trait("Scenario", "OrdinaryTransientFailureSendsNoThrottleSignal")]
    public async Task RefreshAsync_Defers_WithoutSignalling_WhenTheFailureIsNotPushback()
    {
        var metadata = new Mock<IAudiobookMetadataService>();
        metadata
            .Setup(m => m.GetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), false))
            .ThrowsAsync(new HttpRequestException("connection refused"));
        var budget = new CountingBudget();

        var result = await CreateService(BookWithAsin("B0REFUSEDX"), metadata)
            .RefreshAsync(1, budget, CancellationToken.None);

        // Retried and deferred like any transient fault, but the run's allowance is untouched:
        // halving on a DNS failure would starve the walk within a couple of books.
        Assert.Equal(MetadataRefreshOutcome.Deferred, result.Outcome);
        Assert.Equal(0, budget.ThrottleSignals);
        metadata.Verify(
            m => m.GetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), false),
            Times.Exactly(3));
    }

    [Fact]
    [Trait("Scenario", "ExhaustedBudgetDefers")]
    public async Task RefreshAsync_Defers_WhenTheBudgetRefusesTheFirstRequest()
    {
        var metadata = new Mock<IAudiobookMetadataService>();
        var budget = new CountingBudget { GrantLimit = 0 };

        var result = await CreateService(BookWithAsin("B0NOBUDGET"), metadata)
            .RefreshAsync(1, budget, CancellationToken.None);

        Assert.Equal(MetadataRefreshOutcome.Deferred, result.Outcome);
        metadata.Verify(
            m => m.GetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    [Trait("Scenario", "ChangedStateIsAConflict")]
    public async Task RefreshAsync_ReturnsConflict_WhenTheBookChangedUnderneath()
    {
        var metadata = new Mock<IAudiobookMetadataService>();
        metadata
            .Setup(m => m.GetMetadataAsync("B0CONFLICT", "us", false))
            .ReturnsAsync(new AudiobookMetadataEnvelope(
                new AudibleBookResponse { Asin = "B0CONFLICT", Title = "Refreshed Title" },
                "Audible",
                "https://example.invalid/product"));

        var repository = new Mock<IAudiobookRepository>();
        var reads = 0;
        Audiobook ReadBook()
        {
            reads++;
            var book = BookWithAsin("B0CONFLICT");
            book.Title = reads == 1 ? "A Voyage Downriver" : "Edited By Someone Else";
            return book;
        }

        repository.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(ReadBook);
        repository
            .Setup(r => r.GetByIdSnapshotAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReadBook);
        repository.Setup(r => r.UpdateAsync(It.IsAny<Audiobook>())).ReturnsAsync(true);

        var result = await CreateService(null, metadata, repository)
            .RefreshAsync(1, new CountingBudget(), CancellationToken.None);

        Assert.Equal(MetadataRefreshOutcome.Conflict, result.Outcome);
        repository.Verify(r => r.UpdateAsync(It.IsAny<Audiobook>()), Times.Never);
    }
}
