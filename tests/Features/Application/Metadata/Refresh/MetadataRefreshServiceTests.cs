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
        Mock<IAudiobookRepository>? repositoryOverride = null,
        IAsinLookupService? asinLookupService = null)
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
            asinLookupService);
    }

    private static Audiobook BookWithIsbn(string isbn) => new()
    {
        Id = 1,
        Title = "An Isbn And Nothing Else",
        Authors = ["Test Author"],
        ExternalIdentifiers =
        [
            new AudiobookExternalIdentifier
            {
                Type = AudiobookExternalIdentifierType.Isbn,
                ValueRaw = isbn,
                ValueNormalized = isbn,
                IsPrimary = true,
                Source = AudiobookExternalIdentifierSource.Manual
            }
        ]
    };

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

        // NotFound and not Deferred: every region answered, and what they answered was that
        // they have never heard of it. Nothing failed, so the book is stamped and stops
        // monopolising the head of the queue.
        Assert.Equal(MetadataRefreshOutcome.NotFound, result.Outcome);
        Assert.Equal(2, budget.RequestsSpent);

        // The count the caller stamps on. Reporting the outcome alone left the coordinator
        // unable to tell this from a walk nothing ever answered.
        Assert.Equal(2, result.ProviderAnswers);
    }

    [Fact]
    [Trait("Scenario", "AnUnaskableBookIsSkippedNotMissing")]
    public async Task RefreshAsync_ReturnsSkipped_WhenNoIdentifierCouldBeAskedAbout()
    {
        // An ISBN and no resolver wired: there is an identifier, so this is not the no-identifier
        // case, but nothing is ever asked. Reporting NotFound claimed a provider verdict that no
        // provider gave.
        var book = new Audiobook
        {
            Id = 1,
            Title = "Only An Isbn",
            ExternalIdentifiers =
            [
                new AudiobookExternalIdentifier
                {
                    Type = AudiobookExternalIdentifierType.Isbn,
                    ValueRaw = "9780000000001",
                    ValueNormalized = "9780000000001",
                    IsPrimary = true,
                    Source = AudiobookExternalIdentifierSource.Manual
                }
            ]
        };
        var metadata = new Mock<IAudiobookMetadataService>();
        var budget = new CountingBudget();

        var result = await CreateService(book, metadata).RefreshAsync(1, budget, CancellationToken.None);

        Assert.Equal(MetadataRefreshOutcome.Skipped, result.Outcome);
        Assert.Equal(0, result.ProviderAnswers);
        Assert.Equal(0, budget.RequestsSpent);
    }

    [Fact]
    [Trait("Scenario", "SilenceIsNotAMiss")]
    public async Task RefreshAsync_Defers_WhenEveryRequestWasMadeAndNoneWasAnswered()
    {
        var metadata = new Mock<IAudiobookMetadataService>();
        metadata
            .Setup(m => m.GetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), false))
            .ThrowsAsync(new HttpRequestException("connection refused"));
        var budget = new CountingBudget();

        var result = await CreateService(BookWithAsin("B0SILENTXX"), metadata)
            .RefreshAsync(1, budget, CancellationToken.None);

        // The walk ran out of regions the same way the NotFound case does. What separates them
        // is that nothing ever answered, so there is no evidence the book is gone.
        Assert.Equal(MetadataRefreshOutcome.Deferred, result.Outcome);
        Assert.Equal(0, result.ProviderAnswers);
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

        // Pushback stops the book where it stands rather than moving to the next region: the
        // provider asked for less traffic, and uk is the same provider.
        Assert.Equal(MetadataRefreshOutcome.Deferred, result.Outcome);

        // Once, at the ceiling. Signalling on every attempt halved the run's allowance three
        // times over one book: sixty an hour became seven before the second book was reached,
        // and the design says the budget halves for the rest of the cycle, singular.
        Assert.Equal(1, budget.ThrottleSignals);
        Assert.Null(budget.LastRetryAfter);
        metadata.Verify(
            m => m.GetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), false),
            Times.Exactly(3));
    }

    [Fact]
    [Trait("Scenario", "ThrottledExceptionCarriesTheRetryAfter")]
    public async Task RefreshAsync_PassesTheProvidersRetryAfter_ToTheBudget()
    {
        var metadata = new Mock<IAudiobookMetadataService>();
        metadata
            .Setup(m => m.GetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), false))
            .ThrowsAsync(new MetadataProviderThrottledException(
                "slow down",
                TimeSpan.FromSeconds(30)));
        var budget = new CountingBudget();

        var result = await CreateService(BookWithAsin("B0RETRYAFT"), metadata)
            .RefreshAsync(1, budget, CancellationToken.None);

        // The wait the provider named is the one thing an HttpRequestException cannot carry,
        // which is why the signal has a type of its own.
        Assert.Equal(MetadataRefreshOutcome.Deferred, result.Outcome);
        Assert.Equal(1, budget.ThrottleSignals);
        Assert.Equal(TimeSpan.FromSeconds(30), budget.LastRetryAfter);
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

        // Three attempts on us, which is the per-book retry ceiling, then one on uk: the
        // ceiling counts retries of the book, so the region it moves on to gets the single
        // attempt the old endpoint always gave it.
        metadata.Verify(
            m => m.GetMetadataAsync("B0REFUSEDX", "us", false),
            Times.Exactly(3));
        metadata.Verify(
            m => m.GetMetadataAsync("B0REFUSEDX", "uk", false),
            Times.Once);
    }

    [Fact]
    [Trait("Scenario", "ATransientFailureFallsThroughToTheNextRegion")]
    public async Task RefreshAsync_AnswersFromTheNextRegion_WhenTheFirstOneKeepsFailing()
    {
        var metadata = new Mock<IAudiobookMetadataService>();
        metadata
            .Setup(m => m.GetMetadataAsync("B0REGIONFB", "us", false))
            .ThrowsAsync(new HttpRequestException("connection refused"));
        metadata
            .Setup(m => m.GetMetadataAsync("B0REGIONFB", "uk", false))
            .ReturnsAsync(new AudiobookMetadataEnvelope(
                new AudibleBookResponse { Asin = "B0REGIONFB", Title = "Answered By The Second Region" },
                "Audible",
                "https://example.invalid/product"));
        var budget = new CountingBudget();

        var result = await CreateService(BookWithAsin("B0REGIONFB"), metadata)
            .RefreshAsync(1, budget, CancellationToken.None);

        // The endpoint this replaced moved to the next region on a transient failure. A book
        // whose uk lookup answers must not come back 503 because a us connection would not
        // open, which is what retrying one region to the ceiling and then deferring did.
        Assert.Equal(MetadataRefreshOutcome.Updated, result.Outcome);
        Assert.Equal("uk", result.Region);
        Assert.Equal(4, budget.RequestsSpent);
        metadata.Verify(
            m => m.GetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), false),
            Times.Exactly(4));
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
    [Trait("Scenario", "IsbnConvertsToAnAsinAndTheWalkContinues")]
    public async Task RefreshAsync_ResolvesTheIsbn_ThenLooksTheAsinUp()
    {
        var metadata = new Mock<IAudiobookMetadataService>();
        metadata
            .Setup(m => m.GetMetadataAsync("B0FROMISBN", It.IsAny<string>(), false))
            .ReturnsAsync(new AudiobookMetadataEnvelope(
                new AudibleBookResponse { Asin = "B0FROMISBN", Title = "Found Through The Isbn" },
                "Audible",
                "https://example.invalid/product"));
        var isbn = new Mock<IAsinLookupService>();
        isbn
            .Setup(a => a.GetAsinFromIsbnAsync("9780000000001", It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, "B0FROMISBN", (string?)null));
        var budget = new CountingBudget();

        var result = await CreateService(
                BookWithIsbn("9780000000001"),
                metadata,
                asinLookupService: isbn.Object)
            .RefreshAsync(1, budget, CancellationToken.None);

        // The whole fallback had no coverage: every other test passes a null lookup service, so
        // the conversion, its charge and the lookup that follows it were never run.
        Assert.Equal(MetadataRefreshOutcome.Updated, result.Outcome);
        Assert.Equal("B0FROMISBN", result.Asin);

        // The conversion is a provider request and is charged like one, then the ASIN lookup.
        Assert.Equal(2, budget.RequestsSpent);
        Assert.Equal(2, result.ProviderAnswers);
    }

    [Fact]
    [Trait("Scenario", "OneUnconvertibleIsbnDoesNotEndTheWalk")]
    public async Task RefreshAsync_TriesTheNextIsbn_WhenTheFirstOneResolvesToNothing()
    {
        var book = BookWithIsbn("9780000000001");
        book.ExternalIdentifiers.Add(new AudiobookExternalIdentifier
        {
            Type = AudiobookExternalIdentifierType.Isbn,
            ValueRaw = "9780000000002",
            ValueNormalized = "9780000000002",
            IsPrimary = false,
            Source = AudiobookExternalIdentifierSource.Manual
        });

        var metadata = new Mock<IAudiobookMetadataService>();
        metadata
            .Setup(m => m.GetMetadataAsync("B0SECONDIS", It.IsAny<string>(), false))
            .ReturnsAsync(new AudiobookMetadataEnvelope(
                new AudibleBookResponse { Asin = "B0SECONDIS", Title = "Found Through The Second Isbn" },
                "Audible",
                "https://example.invalid/product"));
        var isbn = new Mock<IAsinLookupService>();
        isbn
            .Setup(a => a.GetAsinFromIsbnAsync("9780000000001", It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, (string?)null, "ASIN not found for ISBN"));
        isbn
            .Setup(a => a.GetAsinFromIsbnAsync("9780000000002", It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, "B0SECONDIS", (string?)null));

        var result = await CreateService(book, metadata, asinLookupService: isbn.Object)
            .RefreshAsync(1, new CountingBudget(), CancellationToken.None);

        Assert.Equal(MetadataRefreshOutcome.Updated, result.Outcome);
        Assert.Equal("B0SECONDIS", result.Asin);
    }

    [Fact]
    [Trait("Scenario", "AThrottledIsbnConversionDefersOnce")]
    public async Task RefreshAsync_Defers_AndSignalsOnce_WhenTheIsbnConversionIsThrottled()
    {
        var isbn = new Mock<IAsinLookupService>();
        isbn
            .Setup(a => a.GetAsinFromIsbnAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MetadataProviderThrottledException("slow down", TimeSpan.FromSeconds(45)));
        var budget = new CountingBudget();

        var result = await CreateService(
                BookWithIsbn("9780000000001"),
                new Mock<IAudiobookMetadataService>(),
                asinLookupService: isbn.Object)
            .RefreshAsync(1, budget, CancellationToken.None);

        Assert.Equal(MetadataRefreshOutcome.Deferred, result.Outcome);
        Assert.Equal(1, budget.ThrottleSignals);
        Assert.Equal(TimeSpan.FromSeconds(45), budget.LastRetryAfter);
        Assert.Equal(0, result.ProviderAnswers);
    }

    [Fact]
    [Trait("Scenario", "TheCoverIsChargedToo")]
    public async Task RefreshAsync_ChargesTheCoverDownload_AndSkipsIt_WhenTheBudgetRefuses()
    {
        var metadata = new Mock<IAudiobookMetadataService>();
        metadata
            .Setup(m => m.GetMetadataAsync("B0HASCOVER", "us", false))
            .ReturnsAsync(new AudiobookMetadataEnvelope(
                new AudibleBookResponse
                {
                    Asin = "B0HASCOVER",
                    Title = "Refreshed Title",
                    ImageUrl = "https://example.invalid/cover.jpg"
                },
                "Audible",
                "https://example.invalid/product"));

        var images = new Mock<IImageCacheService>();
        images
            .Setup(i => i.MoveToLibraryStorageAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("images/library/cover.jpg");

        // One token: the lookup takes it, so the cover download is refused.
        var tight = new CountingBudget { GrantLimit = 1 };
        var refused = await CreateServiceWithImages(BookWithAsin("B0HASCOVER"), metadata, images)
            .RefreshAsync(1, tight, CancellationToken.None);

        Assert.Equal(MetadataRefreshOutcome.Updated, refused.Outcome);
        Assert.Equal(1, tight.RequestsSpent);
        images.Verify(
            i => i.MoveToLibraryStorageAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);

        // With room for both, the cover goes out and it is counted. A run at sixty an hour that
        // updated every book it touched was making a hundred and twenty requests an hour.
        var roomy = new CountingBudget();
        var allowed = await CreateServiceWithImages(BookWithAsin("B0HASCOVER"), metadata, images)
            .RefreshAsync(1, roomy, CancellationToken.None);

        Assert.Equal(MetadataRefreshOutcome.Updated, allowed.Outcome);
        Assert.Equal(2, roomy.RequestsSpent);
        images.Verify(
            i => i.MoveToLibraryStorageAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Once);
    }

    private static MetadataRefreshService CreateServiceWithImages(
        Audiobook book,
        Mock<IAudiobookMetadataService> metadata,
        Mock<IImageCacheService> images)
    {
        var repository = new Mock<IAudiobookRepository>();
        repository.Setup(r => r.GetByIdAsync(It.IsAny<int>())).ReturnsAsync(book);
        repository
            .Setup(r => r.GetByIdSnapshotAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(book);
        repository.Setup(r => r.UpdateAsync(It.IsAny<Audiobook>())).ReturnsAsync(true);
        repository
            .Setup(r => r.TryUpdateImageUrlAsync(
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

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
            new MetadataConverters(imageCacheService: null, Mock.Of<ILogger<MetadataConverters>>()),
            images.Object,
            coordinator.Object,
            moveQueue.Object,
            Mock.Of<ILogger<MetadataRefreshService>>());
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
