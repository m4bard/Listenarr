/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Metadata.Refresh;

/// <summary>
/// One scheduled cycle over three books: one that the provider answers on the first region, one
/// with no identifiers at all, and one the provider has never heard of. Real repository, real
/// budget, real coordinator; only the provider and the clock are ours.
/// </summary>
[Trait("Area", "Metadata")]
[Trait("Name", "MetadataRefreshCycleTests")]
[Trait("Category", "Infrastructure")]
public class MetadataRefreshCycleTests : BaseTests, IDisposable
{
    private const string AnsweringAsin = "B0ANSWERED";
    private const string SilentAsin = "B0SILENTXX";

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan amount) => _now += amount;
    }

    private sealed class PassThroughOperationCoordinator : IAudiobookOperationCoordinator
    {
        public Task ExecuteExclusiveAsync(
            int audiobookId,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken = default) => operation(cancellationToken);

        public Task<T> ExecuteExclusiveAsync<T>(
            int audiobookId,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) => operation(cancellationToken);

        public Task ExecuteExclusiveAsync(
            IEnumerable<int> audiobookIds,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken = default) => operation(cancellationToken);

        public Task<T> ExecuteExclusiveAsync<T>(
            IEnumerable<int> audiobookIds,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) => operation(cancellationToken);
    }

    private readonly SqliteConnection _connection;
    private readonly ManualClock _clock = new();
    private readonly List<DateTimeOffset> _providerCalls = [];

    public MetadataRefreshCycleTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private ListenArrDbContext NewContext() => new(
        new DbContextOptionsBuilder<ListenArrDbContext>().UseSqlite(_connection).Options);

    private static AudiobookExternalIdentifier Asin(string value) => new()
    {
        Type = AudiobookExternalIdentifierType.Asin,
        ValueRaw = value,
        ValueNormalized = value,
        Region = "us",
        IsPrimary = true,
        Source = AudiobookExternalIdentifierSource.Manual
    };

    private void SeedLibrary()
    {
        using var db = NewContext();
        db.Database.EnsureCreated();
        db.Audiobooks.AddRange(
            new Audiobook
            {
                Title = "The Answered Book",
                Authors = ["Corpus Author"],
                ExternalIdentifiers = [Asin(AnsweringAsin)],
                // The operator moved the primary off the provider's default. A refresh must
                // repair the missing rows without reverting this.
                SeriesMemberships =
                [
                    new AudiobookSeriesMembership
                    {
                        SeriesName = "The Chosen Sequence",
                        SeriesNumber = "2",
                        IsPrimary = true,
                        SortOrder = 0
                    }
                ]
            },
            new Audiobook
            {
                Title = "The Book With No Identifiers",
                Authors = ["Corpus Author"],
                ExternalIdentifiers = []
            },
            new Audiobook
            {
                Title = "The Book The Provider Forgot",
                Authors = ["Other Author"],
                ExternalIdentifiers = [Asin(SilentAsin)]
            });
        db.SaveChanges();
    }

    private IAudiobookMetadataService FakeProvider()
    {
        var provider = new Mock<IAudiobookMetadataService>();
        provider
            .Setup(p => p.GetMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), false))
            .ReturnsAsync((string asin, string region, bool _) =>
            {
                _providerCalls.Add(_clock.GetUtcNow());
                if (asin != AnsweringAsin || region != "us")
                {
                    return null;
                }

                return new AudiobookMetadataEnvelope(
                    new AudibleBookResponse
                    {
                        Asin = AnsweringAsin,
                        Title = "The Answered Book",
                        Series =
                        [
                            new AudibleSeries
                            {
                                Asin = "B0SERIESAA",
                                Name = "The Provider Default",
                                Position = "1"
                            },
                            new AudibleSeries
                            {
                                Asin = "B0SERIESBB",
                                Name = "The Chosen Sequence",
                                Position = "2"
                            }
                        ]
                    },
                    "Audible",
                    "https://example.invalid/product");
            });
        return provider.Object;
    }

    private MetadataRefreshCoordinator BuildCoordinator()
    {
        var services = new ServiceCollection();
        services.AddSingleton(FakeProvider());
        services.AddSingleton<IAudiobookOperationCoordinator, PassThroughOperationCoordinator>();
        services.AddSingleton(Mock.Of<IImageCacheService>());
        services.AddSingleton(Mock.Of<IMoveQueueService>());
        services.AddSingleton(new MetadataConverters(
            imageCacheService: null,
            Mock.Of<ILogger<MetadataConverters>>()));
        services.AddSingleton(Mock.Of<ILogger<MetadataRefreshService>>());
        services.AddScoped(_ => NewContext());
        services.AddScoped<IAudiobookRepository, AudiobookRepository>();
        services.AddScoped<IMonitoredAuthorRepository>(_ => Mock.Of<IMonitoredAuthorRepository>());
        services.AddScoped<IMetadataRefreshService, MetadataRefreshService>();
        var provider = services.BuildServiceProvider();

        return new MetadataRefreshCoordinator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _clock,
            Mock.Of<ILoggerFactory>(factory =>
                factory.CreateLogger(It.IsAny<string>()) == Mock.Of<ILogger>()),
            Mock.Of<ILogger<MetadataRefreshCoordinator>>(),
            new MetadataRefreshOptionsHolder
            {
                Current = new MetadataRefreshOptions(
                    RequestsPerHour: 60,
                    MinimumSpacingMs: 1000,
                    IntervalHours: 24)
            },
            (delay, _) =>
            {
                _clock.Advance(delay);
                return Task.CompletedTask;
            });
    }

    [Fact]
    [Trait("Scenario", "OneCycleOverASmallLibrary")]
    public async Task ScheduledCycle_SpendsOneRequestPerRegionTried_AndKeepsTheSpacingFloor()
    {
        SeedLibrary();
        var coordinator = BuildCoordinator();

        var run = await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Scheduled, null, Force: false),
            CancellationToken.None);

        Assert.NotNull(run);
        Assert.Equal("Completed", run.Status);
        Assert.Equal(3, run.TotalBooks);
        Assert.Equal(3, run.Processed);
        Assert.Equal(1, run.Updated);
        // The book with no identifiers and the book the provider forgot both settle.
        Assert.Equal(2, run.Skipped);
        Assert.Equal(0, run.Deferred);
        Assert.Equal(0, run.Failed);

        // One request for the book that answered on its first region, two for the book whose
        // us and uk lookups both missed, none for the book with nothing to ask about.
        Assert.Equal(3, _providerCalls.Count);
        Assert.Equal(3, run.RequestsSpent);

        for (var i = 1; i < _providerCalls.Count; i++)
        {
            Assert.True(
                _providerCalls[i] - _providerCalls[i - 1] >= TimeSpan.FromSeconds(1),
                $"request {i} came {_providerCalls[i] - _providerCalls[i - 1]} after the previous one");
        }
    }

    [Fact]
    [Trait("Scenario", "MembershipsRepairedPrimaryPreserved")]
    public async Task ScheduledCycle_RepairsMembershipsWithTheirIdentifiers_AndKeepsTheChosenPrimary()
    {
        SeedLibrary();
        var coordinator = BuildCoordinator();

        await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Scheduled, null, Force: false),
            CancellationToken.None);

        using var db = NewContext();
        var book = await db.Audiobooks
            .Include(candidate => candidate.SeriesMemberships)
            .SingleAsync(candidate => candidate.Title == "The Answered Book");

        Assert.NotNull(book.SeriesMemberships);
        Assert.Equal(2, book.SeriesMemberships.Count);
        Assert.All(
            book.SeriesMemberships,
            membership => Assert.False(string.IsNullOrWhiteSpace(membership.SeriesAsin)));

        var primary = Assert.Single(book.SeriesMemberships, membership => membership.IsPrimary);
        Assert.Equal("The Chosen Sequence", primary.SeriesName);
        Assert.Equal("B0SERIESBB", primary.SeriesAsin);
        Assert.Equal("The Chosen Sequence", book.Series);
    }

    [Fact]
    [Trait("Scenario", "TimestampsSettleTheQueue")]
    public async Task ScheduledCycle_StampsEverySettledBook_SoASecondCycleSpendsNothing()
    {
        SeedLibrary();
        var coordinator = BuildCoordinator();

        await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Scheduled, null, Force: false),
            CancellationToken.None);
        var afterFirstCycle = _providerCalls.Count;

        var second = await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Scheduled, null, Force: false),
            CancellationToken.None);

        Assert.NotNull(second);
        Assert.Equal(0, second.TotalBooks);
        Assert.Equal(afterFirstCycle, _providerCalls.Count);

        using var db = NewContext();
        Assert.All(
            await db.Audiobooks.AsNoTracking().ToListAsync(),
            book => Assert.NotNull(book.LastMetadataRefreshAt));
    }
}
