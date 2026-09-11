/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Listenarr.Application.Common.Exceptions;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Metadata.Refresh;

/// <summary>
/// The gate, the counters, the stamping rule and cancellation at a book boundary.
/// </summary>
[Trait("Area", "Metadata")]
[Trait("Name", "MetadataRefreshCoordinatorTests")]
[Trait("Category", "Infrastructure")]
public class MetadataRefreshCoordinatorTests : BaseTests
{
    private sealed class StubRefreshService : IMetadataRefreshService
    {
        private readonly Func<int, MetadataRefreshOutcome> _outcomes;

        public StubRefreshService(Func<int, MetadataRefreshOutcome> outcomes) => _outcomes = outcomes;

        public List<int> Seen { get; } = [];

        public TaskCompletionSource? Gate { get; set; }

        /// <summary>Completes once the loop is inside its first book.</summary>
        public TaskCompletionSource Entered { get; } = new();

        public async Task<MetadataRefreshResult> RefreshAsync(
            int audiobookId,
            IMetadataRefreshBudget budget,
            CancellationToken cancellationToken)
        {
            Seen.Add(audiobookId);
            Entered.TrySetResult();
            if (Gate != null)
            {
                await Gate.Task.WaitAsync(cancellationToken);
            }

            await budget.ChargeAsync(cancellationToken);
            return new MetadataRefreshResult(_outcomes(audiobookId), budget.RequestsSpent);
        }
    }

    private static (MetadataRefreshCoordinator Coordinator, StubRefreshService Service,
        Mock<IAudiobookRepository> Repository) Create(
        IReadOnlyList<MetadataRefreshCandidate> due,
        Func<int, MetadataRefreshOutcome> outcomes)
    {
        var service = new StubRefreshService(outcomes);
        var repository = new Mock<IAudiobookRepository>();
        repository
            .Setup(r => r.GetAudiobooksDueForMetadataRefreshAsync(
                It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(due.ToList());
        repository
            .Setup(r => r.StampMetadataRefreshAsync(
                It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return (
            Coordinator(service, repository.Object, Mock.Of<IMonitoredAuthorRepository>()),
            service,
            repository);
    }

    private static MetadataRefreshCoordinator Coordinator(
        IMetadataRefreshService service,
        IAudiobookRepository repository,
        IMonitoredAuthorRepository authors)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => service);
        services.AddScoped(_ => repository);
        services.AddScoped(_ => authors);
        var provider = services.BuildServiceProvider();

        return new MetadataRefreshCoordinator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            Mock.Of<ILoggerFactory>(factory =>
                factory.CreateLogger(It.IsAny<string>()) == Mock.Of<ILogger>()),
            Mock.Of<ILogger<MetadataRefreshCoordinator>>(),
            new MetadataRefreshOptionsHolder
            {
                Current = new MetadataRefreshOptions(MinimumSpacingMs: 0)
            });
    }

    private static MetadataRefreshCandidate Candidate(int id, string author) =>
        new(id, author, null);

    [Fact]
    [Trait("Scenario", "CountersAndStampingByOutcome")]
    public async Task RunToCompletionAsync_CountsEveryOutcome_AndStampsOnlyTheSettledOnes()
    {
        var (coordinator, _, repository) = Create(
            [Candidate(1, "A"), Candidate(2, "A"), Candidate(3, "B"), Candidate(4, "B"), Candidate(5, "C")],
            id => id switch
            {
                1 => MetadataRefreshOutcome.Updated,
                2 => MetadataRefreshOutcome.Skipped,
                3 => MetadataRefreshOutcome.NotFound,
                4 => MetadataRefreshOutcome.Conflict,
                _ => MetadataRefreshOutcome.Deferred
            });

        var run = await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: false),
            CancellationToken.None);

        Assert.NotNull(run);
        Assert.Equal("Completed", run.Status);
        Assert.Equal(5, run.TotalBooks);
        Assert.Equal(5, run.Processed);
        Assert.Equal(1, run.Updated);
        Assert.Equal(2, run.Skipped);
        // A conflict leaves the timestamp unset and is retried next cycle, which is what
        // deferred means here, so it is counted with the deferrals rather than the skips.
        Assert.Equal(2, run.Deferred);
        Assert.Equal(0, run.Failed);

        foreach (var stamped in new[] { 1, 2, 3 })
        {
            repository.Verify(
                r => r.StampMetadataRefreshAsync(stamped, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        foreach (var unstamped in new[] { 4, 5 })
        {
            repository.Verify(
                r => r.StampMetadataRefreshAsync(unstamped, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }

    [Fact]
    [Trait("Scenario", "ARefusedMutationDefersOneBookOnly")]
    public async Task RunToCompletionAsync_CountsAConflictExceptionAsADeferral_AndKeepsGoing()
    {
        var (coordinator, service, repository) = Create(
            [Candidate(1, "A"), Candidate(2, "A"), Candidate(3, "A")],
            id => id == 2
                ? throw new ApplicationConflictException(
                    "move_unresolved",
                    "An unresolved move owns this book's files. Resolve it before refreshing.")
                : MetadataRefreshOutcome.Updated);

        var run = await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: false),
            CancellationToken.None);

        Assert.NotNull(run);
        Assert.Equal("Completed", run.Status);
        Assert.Equal(3, run.Processed);
        Assert.Equal(2, run.Updated);
        Assert.Equal(1, run.Deferred);
        Assert.Equal(0, run.Failed);
        Assert.Equal([1, 2, 3], service.Seen);

        repository.Verify(
            r => r.StampMetadataRefreshAsync(2, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        foreach (var stamped in new[] { 1, 3 })
        {
            repository.Verify(
                r => r.StampMetadataRefreshAsync(stamped, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Fact]
    [Trait("Scenario", "AnAuthorsBooksAreTakenTogether")]
    public async Task RunToCompletionAsync_GroupsAnAuthorsBooks_SoMembershipRepairsLandInOnePass()
    {
        var (coordinator, service, _) = Create(
            [Candidate(1, "First Author"), Candidate(2, "Second Author"), Candidate(3, "First Author")],
            _ => MetadataRefreshOutcome.Updated);

        await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Scheduled, null, Force: false),
            CancellationToken.None);

        Assert.Equal([1, 3, 2], service.Seen);
    }

    [Fact]
    [Trait("Scenario", "SecondStartIsRefused")]
    public async Task StartAsync_ReturnsTheActiveRun_WhenOneIsAlreadyRunning()
    {
        var (coordinator, service, _) = Create(
            [Candidate(1, "A")],
            _ => MetadataRefreshOutcome.Updated);
        service.Gate = new TaskCompletionSource();

        var first = await coordinator.StartAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: false),
            CancellationToken.None);
        Assert.True(first.Started);

        var second = await coordinator.StartAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: false),
            CancellationToken.None);

        Assert.False(second.Started);
        Assert.Equal(first.Run.RunId, second.Run.RunId);

        service.Gate.SetResult();
        await coordinator.WaitForIdleAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    [Trait("Scenario", "ARefusedStartSeesTheRunBeingAdmitted")]
    public async Task StartAsync_ReportsTheRunStillResolving_WhenASecondCallerArrivesDuringTheLookup()
    {
        var service = new StubRefreshService(_ => MetadataRefreshOutcome.Updated);
        var resolveEntered = new TaskCompletionSource();
        var resolveGate = new TaskCompletionSource();
        var repository = new Mock<IAudiobookRepository>();
        repository
            .Setup(r => r.GetAudiobooksDueForMetadataRefreshAsync(
                It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async Task<List<MetadataRefreshCandidate>> () =>
            {
                resolveEntered.TrySetResult();
                await resolveGate.Task;
                return [Candidate(1, "A")];
            });
        repository
            .Setup(r => r.StampMetadataRefreshAsync(
                It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var coordinator = Coordinator(
            service,
            repository.Object,
            Mock.Of<IMonitoredAuthorRepository>());
        var request = new MetadataRefreshScopeRequest(
            MetadataRefreshRunScope.Library, null, Force: false);

        var first = coordinator.StartAsync(request, CancellationToken.None);
        await resolveEntered.Task;

        // The gate is held and the scope query has not answered yet. On a cold process there
        // is no previous run either, so the refusal has only the run being admitted to report.
        var second = await coordinator.StartAsync(request, CancellationToken.None);
        Assert.False(second.Started);

        var current = coordinator.Current();
        Assert.NotNull(current);
        Assert.Equal("Running", current.Status);
        Assert.Equal(second.Run.RunId, current.RunId);

        resolveGate.SetResult();
        var started = await first;
        Assert.True(started.Started);
        Assert.Equal(second.Run.RunId, started.Run.RunId);
        Assert.Equal(1, started.Run.TotalBooks);

        await coordinator.WaitForIdleAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    [Trait("Scenario", "ABackgroundRunOutlivesItsCaller")]
    public async Task StartAsync_RunsToCompletion_WhenTheCallersTokenIsCancelledAfterAdmission()
    {
        var (coordinator, service, _) = Create(
            [Candidate(1, "A"), Candidate(2, "A")],
            _ => MetadataRefreshOutcome.Updated);
        service.Gate = new TaskCompletionSource();
        using var caller = new CancellationTokenSource();

        var started = await coordinator.StartAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: false),
            caller.Token);
        Assert.True(started.Started);

        // A web request's token is cancelled the moment its response is written, which is
        // before the run it asked for has done anything. The run is not the caller's to end.
        await service.Entered.Task;
        await caller.CancelAsync();
        service.Gate.SetResult();
        await coordinator.WaitForIdleAsync(TimeSpan.FromSeconds(10));

        var final = coordinator.Find(started.Run.RunId);
        Assert.NotNull(final);
        Assert.Equal("Completed", final.Status);
        Assert.Equal([1, 2], service.Seen);
    }

    [Fact]
    [Trait("Scenario", "CancelStopsAtABookBoundary")]
    public async Task Cancel_StopsTheRun_WithoutStartingTheNextBook()
    {
        var (coordinator, service, _) = Create(
            [Candidate(1, "A"), Candidate(2, "A"), Candidate(3, "A")],
            _ => MetadataRefreshOutcome.Updated);
        service.Gate = new TaskCompletionSource();

        var started = await coordinator.StartAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: false),
            CancellationToken.None);

        // Without this the cancel can land before the loop enters its first book, and the
        // assertion below would be measuring the scheduler rather than the book boundary.
        await service.Entered.Task;
        Assert.True(coordinator.Cancel(started.Run.RunId));
        service.Gate.SetResult();
        await coordinator.WaitForIdleAsync(TimeSpan.FromSeconds(10));

        var final = coordinator.Find(started.Run.RunId);
        Assert.NotNull(final);
        Assert.Equal("Cancelled", final.Status);
        Assert.Single(service.Seen);
    }

    [Fact]
    [Trait("Scenario", "UnknownRunIdIsNotFound")]
    public void Find_ReturnsNull_ForARunThisProcessNeverStarted()
    {
        var (coordinator, _, _) = Create([], _ => MetadataRefreshOutcome.Updated);

        // Restart leaves no run behind: the registry is in memory by design, and the next
        // cycle picks up from the per-book timestamps instead.
        Assert.Null(coordinator.Find(Guid.NewGuid()));
        Assert.Null(coordinator.Current());
    }

    [Fact]
    [Trait("Scenario", "AuthorScopeUsesTheMonitoredAuthorRow")]
    public async Task RunToCompletionAsync_ResolvesAnAuthorScope_ThroughTheMonitoredAuthorId()
    {
        var service = new StubRefreshService(_ => MetadataRefreshOutcome.Updated);
        var repository = new Mock<IAudiobookRepository>();
        repository
            .Setup(r => r.GetAudiobookIdsByAuthorNameAsync("Monitored Author", It.IsAny<CancellationToken>()))
            .ReturnsAsync([7, 8]);
        repository
            .Setup(r => r.StampMetadataRefreshAsync(
                It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var authors = new Mock<IMonitoredAuthorRepository>();
        authors
            .Setup(a => a.GetByIdAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonitoredAuthor { Id = 3, AuthorName = "Monitored Author" });

        var coordinator = Coordinator(service, repository.Object, authors.Object);

        var run = await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Author, 3, Force: true),
            CancellationToken.None);

        Assert.NotNull(run);
        Assert.Equal("Author", run.Scope);
        Assert.Equal([7, 8], service.Seen);
    }

    [Fact]
    [Trait("Scenario", "UnknownAuthorIsAnEmptyRun")]
    public async Task RunToCompletionAsync_CompletesWithNothing_WhenTheAuthorIdIsUnknown()
    {
        var (coordinator, service, _) = Create([], _ => MetadataRefreshOutcome.Updated);

        var run = await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Author, 99, Force: false),
            CancellationToken.None);

        Assert.NotNull(run);
        Assert.Equal(0, run.TotalBooks);
        Assert.Equal("Completed", run.Status);
        Assert.Empty(service.Seen);
    }
}
