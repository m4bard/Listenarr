using Listenarr.Api.Features.Discovery;
using Listenarr.Application.Search.Discovery;
using Listenarr.Domain.Search.Discovery;
using Listenarr.Tests.Common;
using Listenarr.Tests.Features.Infrastructure.HostedServices.Search;
using Listenarr.Tests.Features.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Api.Features.Discovery;

/// <summary>
/// The discovery status endpoint and gap acknowledgement, over the real store on a SQLite
/// file database migrated by the real chain.
/// </summary>
[Trait("Area", "Discovery")]
[Trait("Name", "DiscoveryStatusTests")]
[Trait("Category", "Api")]
public sealed class DiscoveryStatusTests : BaseTests
{
    private static readonly DateTime PollUtc = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FirstAcknowledgeUtc = PollUtc.AddHours(2);
    private static readonly DateTime SecondAcknowledgeUtc = PollUtc.AddHours(5);

    [Fact]
    public async Task Status_ShowsTheOpenGapAndTheCountsOfAPolledIndexer()
    {
        var kit = await StatusKit.CreateAsync(FileService.GetTempPath(), [TorznabIndexer(categories: "3030")]);
        await kit.CommitGapPollAsync(kit.Indexers[0].Id);

        var report = Assert.Single(await kit.ReadStatusAsync());

        Assert.Equal(DiscoveryFeedSupport.Supported, report.Feed);
        Assert.Equal(PollUtc, report.LastPollUtc);
        Assert.Equal(PollUtc, report.LastSuccessUtc);
        Assert.Equal(PollUtc.AddHours(-3), report.NewestPublishedUtc);
        Assert.Equal(7, report.FutureDatedItemCount);
        Assert.Equal(11, report.UndatedItemCount);
        var gap = Assert.Single(report.OpenGaps);
        Assert.Equal(PollUtc.AddHours(-9), gap.GapStartUtc);
        Assert.Equal(PollUtc.AddHours(-4), gap.GapEndUtc);
        Assert.Equal(PollUtc, gap.DetectedUtc);
    }

    [Fact]
    public async Task Status_ForAnIndexerNeverPolled_ShowsEmptyTimesAndZeroCounts()
    {
        var kit = await StatusKit.CreateAsync(FileService.GetTempPath(), [TorznabIndexer(categories: "3030")]);

        var report = Assert.Single(await kit.ReadStatusAsync());

        Assert.Null(report.LastPollUtc);
        Assert.Null(report.LastSuccessUtc);
        Assert.Equal(0, report.FutureDatedItemCount + report.UndatedItemCount);
        Assert.Empty(report.OpenGaps);
    }

    [Fact]
    public async Task Acknowledge_ClosesTheGap_AndASecondAcknowledgeKeepsTheFirstTime()
    {
        var kit = await StatusKit.CreateAsync(FileService.GetTempPath(), [TorznabIndexer(categories: "3030")]);
        await kit.CommitGapPollAsync(kit.Indexers[0].Id);
        var gapId = Assert.Single((await kit.ReadStatusAsync())[0].OpenGaps).GapId;

        kit.Clock.UtcNow = FirstAcknowledgeUtc;
        var first = await kit.Controller.AcknowledgeGap(gapId, CancellationToken.None);
        kit.Clock.UtcNow = SecondAcknowledgeUtc;
        var second = await kit.Controller.AcknowledgeGap(gapId, CancellationToken.None);
        var missing = await kit.Controller.AcknowledgeGap(gapId + 1000, CancellationToken.None);

        Assert.IsType<NoContentResult>(first);
        Assert.IsType<NoContentResult>(second);
        Assert.IsType<NotFoundResult>(missing);
        Assert.Empty((await kit.ReadStatusAsync())[0].OpenGaps);
        await using var db = kit.Database.CreateContext();
        var stored = await db.IndexerCoverageGaps.AsNoTracking().SingleAsync();
        Assert.Equal(FirstAcknowledgeUtc, stored.AcknowledgedUtc);
    }

    [Fact]
    public async Task Status_ShowsMyAnonamouseAndInternetArchiveAsUnsupported_WithoutAnyProviderRead()
    {
        var kit = await StatusKit.CreateAsync(
            FileService.GetTempPath(),
            [
                TorznabIndexer(categories: "3030"),
                new Indexer { Name = "Mam", Type = "Torrent", Implementation = "MyAnonamouse", Url = "https://mam.example", Categories = "13" },
                new Indexer { Name = "Archive", Type = "DirectDownload", Implementation = "InternetArchive", Url = "https://archive.example" }
            ]);

        var reports = await kit.ReadStatusAsync();

        Assert.Equal(
            [DiscoveryFeedSupport.Supported, DiscoveryFeedSupport.Unsupported, DiscoveryFeedSupport.Unsupported],
            reports.Select(report => report.Feed));
        Assert.Equal(["Mam", "Archive"], reports.Skip(1).Select(report => report.IndexerName));
        Assert.Equal(0, kit.FeedProvider.FetchCount);
    }

    [Fact]
    public async Task Status_ListsOnlyEnabledIndexersWithRss_AndFlagsAMissingCategoryList()
    {
        var kit = await StatusKit.CreateAsync(
            FileService.GetTempPath(),
            [
                TorznabIndexer(categories: null),
                TorznabIndexer(categories: "3030", isEnabled: false),
                TorznabIndexer(categories: "3030", enableRss: false)
            ]);

        var report = Assert.Single(await kit.ReadStatusAsync());

        Assert.Equal(kit.Indexers[0].Id, report.IndexerId);
        Assert.Equal(DiscoveryFeedSupport.NoCategories, report.Feed);
    }

    private static Indexer TorznabIndexer(string? categories, bool isEnabled = true, bool enableRss = true) => new()
    {
        Name = "Feed",
        Type = "Torrent",
        Implementation = "Torznab",
        Url = "https://feed.example",
        Categories = categories,
        IsEnabled = isEnabled,
        EnableRss = enableRss
    };

    private sealed class StatusKit
    {
        private readonly DiscoveryStatusService _service;

        private StatusKit(
            DiscoveryStoreTestDatabase database,
            IReadOnlyList<Indexer> indexers,
            RecordingFeedProvider feedProvider,
            SettableTimeProvider clock)
        {
            Database = database;
            Indexers = indexers;
            FeedProvider = feedProvider;
            Clock = clock;
            var repository = new Mock<IIndexerRepository>();
            repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(indexers.ToList());
            _service = new DiscoveryStatusService(repository.Object, database.CreateStore(), feedProvider, clock);
            Controller = new DiscoveryController(_service);
        }

        public DiscoveryStoreTestDatabase Database { get; }

        public IReadOnlyList<Indexer> Indexers { get; }

        public RecordingFeedProvider FeedProvider { get; }

        public SettableTimeProvider Clock { get; }

        public DiscoveryController Controller { get; }

        /// <summary>
        /// The database seeds one Torznab indexer per requested id; the indexers under test take
        /// those ids, so a checkpoint written for one is read back for the same indexer.
        /// </summary>
        public static async Task<StatusKit> CreateAsync(string directory, IReadOnlyList<Indexer> indexers)
        {
            var database = await DiscoveryStoreTestDatabase.CreateAsync(directory, indexers.Count);
            for (var index = 0; index < indexers.Count; index++)
            {
                indexers[index].Id = database.IndexerIds[index];
            }

            return new StatusKit(database, indexers, new RecordingFeedProvider(), new SettableTimeProvider(PollUtc));
        }

        public async Task<IReadOnlyList<DiscoveryIndexerStatusReport>> ReadStatusAsync()
        {
            var result = await Controller.GetStatus(CancellationToken.None);
            return Assert.IsAssignableFrom<IReadOnlyList<DiscoveryIndexerStatusReport>>(
                Assert.IsType<OkObjectResult>(result.Result).Value);
        }

        public Task CommitGapPollAsync(int indexerId) =>
            Database.CreateStore().CommitPollAsync(
                indexerId,
                [],
                new DiscoveryCheckpointAdvance(PollUtc.AddHours(-3), "release-0001"),
                new DiscoveryCoverageGapSpan(PollUtc.AddHours(-9), PollUtc.AddHours(-4)),
                futureDatedDelta: 7,
                undatedDelta: 11,
                PollUtc,
                CancellationToken.None);
    }

    private sealed class RecordingFeedProvider : IIndexerFeedProvider
    {
        public int FetchCount { get; private set; }

        public bool Supports(Indexer indexer) =>
            indexer.Implementation is "Torznab" or "Newznab";

        public Task<IndexerFeedPage> FetchPageAsync(
            Indexer indexer,
            int offset,
            IndexerSearchContext context,
            CancellationToken cancellationToken)
        {
            FetchCount++;
            throw new InvalidOperationException("The status endpoint must not read a feed.");
        }
    }
}
