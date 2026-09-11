/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Listenarr.Application.Common.Exceptions;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Hosting;

namespace Listenarr.Tests.Features.Infrastructure.Metadata.Refresh;

/// <summary>
/// The gate, the counters, the stamping rule, cancellation at a book boundary, and what becomes
/// of a run the host is shutting down underneath.
/// </summary>
[Trait("Area", "Metadata")]
[Trait("Name", "MetadataRefreshCoordinatorTests")]
[Trait("Category", "Infrastructure")]
public class MetadataRefreshCoordinatorTests : BaseTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private sealed class StubRefreshService : IMetadataRefreshService
    {
        private readonly Func<int, MetadataRefreshOutcome> _outcomes;

        public StubRefreshService(Func<int, MetadataRefreshOutcome> outcomes) => _outcomes = outcomes;

        public List<int> Seen { get; } = [];

        public TaskCompletionSource? Gate { get; set; }

        /// <summary>
        /// How many provider requests came back with a verdict for a given book. One by default,
        /// because most of these tests are about the gate rather than about the stamping rule.
        /// </summary>
        public Func<int, int> ProviderAnswers { get; set; } = _ => 1;

        /// <summary>Completes once the loop is inside its first book.</summary>
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
            return new MetadataRefreshResult(
                _outcomes(audiobookId),
                budget.RequestsSpent,
                ProviderAnswers: ProviderAnswers(audiobookId));
        }
    }

    /// <summary>
    /// Stands in for the host's own lifetime so a test can pull ApplicationStopping without a
    /// host. The token is a real one; only the thing that owns it is ours.
    /// </summary>
    private sealed class StubApplicationLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => _stopping.Cancel();

        public void Dispose() => _stopping.Dispose();
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan amount) => _now += amount;
    }

    private static (MetadataRefreshCoordinator Coordinator, StubRefreshService Service,
        Mock<IAudiobookRepository> Repository) Create(
        IReadOnlyList<MetadataRefreshCandidate> due,
        Func<int, MetadataRefreshOutcome> outcomes)
    {
        var service = new StubRefreshService(outcomes);
        var repository = DueRepository(due);

        return (
            Coordinator(service, repository.Object, Mock.Of<IMonitoredAuthorRepository>()),
            service,
            repository);
    }

    private static Mock<IAudiobookRepository> DueRepository(IReadOnlyList<MetadataRefreshCandidate> due)
    {
        var repository = new Mock<IAudiobookRepository>();
        repository
            .Setup(r => r.GetAudiobooksDueForMetadataRefreshAsync(
                It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(due.ToList());
        repository
            .Setup(r => r.FilterAudiobookIdsDueForMetadataRefreshAsync(
                It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<int> ids, DateTime _, CancellationToken _) => ids.ToList());
        repository
            .Setup(r => r.StampMetadataRefreshAsync(
                It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return repository;
    }

    /// <summary>
    /// A coordinator over a real budget. <paramref name="clock"/> and the delay hook are taken
    /// together on purpose, and the helper offers no way to separate them: a frozen clock with
    /// the real Task.Delay makes the budget's wait loop spin for ever. It would not show up
    /// until some test grew past what a sixty-token bucket covers, and it would hang CI with no
    /// xUnit timeout to stop it.
    /// </summary>
    private static MetadataRefreshCoordinator Coordinator(
        IMetadataRefreshService service,
        IAudiobookRepository repository,
        IMonitoredAuthorRepository authors,
        MetadataRefreshOptionsHolder? holder = null,
        IConfigurationService? configuration = null,
        ManualClock? clock = null,
        IHostApplicationLifetime? lifetime = null,
        ILogger<MetadataRefreshCoordinator>? logger = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => service);
        services.AddScoped(_ => repository);
        services.AddScoped(_ => authors);
        if (configuration != null)
        {
            services.AddScoped(_ => configuration);
        }

        var provider = services.BuildServiceProvider();

        return new MetadataRefreshCoordinator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            (TimeProvider?)clock ?? TimeProvider.System,
            Mock.Of<ILoggerFactory>(factory =>
                factory.CreateLogger(It.IsAny<string>()) == Mock.Of<ILogger>()),
            logger ?? Mock.Of<ILogger<MetadataRefreshCoordinator>>(),
            holder ?? new MetadataRefreshOptionsHolder
            {
                Current = new MetadataRefreshOptions(MinimumSpacingMs: 0)
            },
            lifetime ?? new StubApplicationLifetime(),
            clock == null
                ? null
                : (delay, _) =>
                {
                    clock.Advance(delay);
                    return Task.CompletedTask;
                });
    }

    private static MetadataRefreshCandidate Candidate(int id, string author) =>
        new(id, author, null);

    private static TaskCompletionSource Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

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
    [Trait("Scenario", "OneUnexpectedFailureCostsOneBook")]
    public async Task RunToCompletionAsync_CountsAnUnexpectedFailureAgainstTheBook_NotTheRun()
    {
        var (coordinator, service, repository) = Create(
            [Candidate(1, "A"), Candidate(2, "A"), Candidate(3, "A")],
            id => id == 2
                ? throw new InvalidOperationException("the provider client returned something impossible")
                : MetadataRefreshOutcome.Updated);

        var run = await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: false),
            CancellationToken.None);

        // The run-level catch ended the walk here, and left every book behind the bad one
        // untouched until the next cycle, which on a large library is most of them.
        Assert.NotNull(run);
        Assert.Equal("Completed", run.Status);
        Assert.Equal(3, run.Processed);
        Assert.Equal(2, run.Updated);
        Assert.Equal(1, run.Failed);
        Assert.Equal([1, 2, 3], service.Seen);

        repository.Verify(
            r => r.StampMetadataRefreshAsync(2, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
    [Trait("Scenario", "ManySimultaneousStartsAdmitExactlyOne")]
    public async Task StartAsync_AdmitsExactlyOneRun_WhenEveryCallerArrivesAtOnce()
    {
        // The one-run gate is the invariant the whole coordinator is built around, and every
        // other test of it starts one run and then another. That shape cannot fail for a gate
        // that only works because the two calls were sequential; this one can.
        var (coordinator, service, repository) = Create(
            [Candidate(1, "A"), Candidate(2, "A")],
            _ => MetadataRefreshOutcome.Updated);
        service.Gate = Signal();

        var request = new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: false);
        var release = Signal();
        var starts = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(async () =>
            {
                await release.Task;
                return await coordinator.StartAsync(request, CancellationToken.None);
            }))
            .ToArray();

        release.SetResult();
        var results = await Task.WhenAll(starts).WaitAsync(TestTimeout);

        var admitted = results.Where(result => result.Started).ToList();
        Assert.Single(admitted);

        // Every refused caller is told about the run that is actually going, not about a run
        // of its own that never started.
        Assert.All(results, result => Assert.Equal(admitted[0].Run.RunId, result.Run.RunId));

        // And the scope query ran once. Sixteen callers admitted behind a gate that only
        // narrowed afterwards would each have loaded the due set.
        repository.Verify(
            r => r.GetAudiobooksDueForMetadataRefreshAsync(
                It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);

        service.Gate.SetResult();
        await coordinator.WaitForIdleAsync(TestTimeout);
        Assert.Equal([1, 2], service.Seen);
    }

    [Fact]
    [Trait("Scenario", "SecondStartIsRefused")]
    public async Task StartAsync_ReturnsTheActiveRun_WhenOneIsAlreadyRunning()
    {
        var (coordinator, service, _) = Create(
            [Candidate(1, "A")],
            _ => MetadataRefreshOutcome.Updated);
        service.Gate = Signal();

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
        await coordinator.WaitForIdleAsync(TestTimeout);
    }

    [Fact]
    [Trait("Scenario", "ARefusedStartSeesTheRunBeingAdmitted")]
    public async Task StartAsync_ReportsTheRunStillResolving_WhenASecondCallerArrivesDuringTheLookup()
    {
        var service = new StubRefreshService(_ => MetadataRefreshOutcome.Updated);
        var resolveEntered = Signal();
        var resolveGate = Signal();
        var repository = DueRepository([]);
        repository
            .Setup(r => r.GetAudiobooksDueForMetadataRefreshAsync(
                It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async Task<List<MetadataRefreshCandidate>> () =>
            {
                resolveEntered.TrySetResult();
                await resolveGate.Task;
                return [Candidate(1, "A")];
            });
        var coordinator = Coordinator(
            service,
            repository.Object,
            Mock.Of<IMonitoredAuthorRepository>());
        var request = new MetadataRefreshScopeRequest(
            MetadataRefreshRunScope.Library, null, Force: false);

        var first = coordinator.StartAsync(request, CancellationToken.None);
        await resolveEntered.Task.WaitAsync(TestTimeout);

        // The gate is held and the scope query has not answered yet. On a cold process there
        // is no previous run either, so the refusal has only the run being admitted to report.
        var second = await coordinator.StartAsync(request, CancellationToken.None);
        Assert.False(second.Started);

        var current = coordinator.Current();
        Assert.NotNull(current);
        Assert.Equal("Running", current.Status);
        Assert.Equal(second.Run.RunId, current.RunId);

        resolveGate.SetResult();
        var started = await first.WaitAsync(TestTimeout);
        Assert.True(started.Started);
        Assert.Equal(second.Run.RunId, started.Run.RunId);
        Assert.Equal(1, started.Run.TotalBooks);

        await coordinator.WaitForIdleAsync(TestTimeout);
    }

    [Fact]
    [Trait("Scenario", "ABackgroundRunOutlivesItsCaller")]
    public async Task StartAsync_RunsToCompletion_WhenTheCallersTokenIsCancelledAfterAdmission()
    {
        var (coordinator, service, _) = Create(
            [Candidate(1, "A"), Candidate(2, "A")],
            _ => MetadataRefreshOutcome.Updated);
        service.Gate = Signal();
        using var caller = new CancellationTokenSource();

        var started = await coordinator.StartAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: false),
            caller.Token);
        Assert.True(started.Started);

        // A web request's token is cancelled the moment its response is written, which is
        // before the run it asked for has done anything. The run is not the caller's to end.
        await service.Entered.Task.WaitAsync(TestTimeout);
        await caller.CancelAsync();
        service.Gate.SetResult();
        await coordinator.WaitForIdleAsync(TestTimeout);

        var final = coordinator.Find(started.Run.RunId);
        Assert.NotNull(final);
        Assert.Equal("Completed", final.Status);
        Assert.Equal([1, 2], service.Seen);
    }

    [Fact]
    [Trait("Scenario", "ShutdownStopsABackgroundRun")]
    public async Task StartAsync_StopsTheRun_WhenTheHostBeginsShuttingDown()
    {
        var service = new StubRefreshService(_ => MetadataRefreshOutcome.Updated);
        using var lifetime = new StubApplicationLifetime();
        var coordinator = Coordinator(
            service,
            DueRepository([Candidate(1, "A"), Candidate(2, "A")]).Object,
            Mock.Of<IMonitoredAuthorRepository>(),
            lifetime: lifetime);
        service.Gate = Signal();

        var started = await coordinator.StartAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: false),
            CancellationToken.None);
        Assert.True(started.Started);
        await service.Entered.Task.WaitAsync(TestTimeout);

        // A run triggered over HTTP carried a source linked to nothing at all, so it kept
        // resolving scopes out of a provider the host was in the middle of tearing down.
        lifetime.StopApplication();
        await coordinator.WaitForIdleAsync(TestTimeout);

        var final = coordinator.Find(started.Run.RunId);
        Assert.NotNull(final);
        Assert.Equal("Cancelled", final.Status);
        Assert.Single(service.Seen);
    }

    [Fact]
    [Trait("Scenario", "DisposalDrainsTheRunInFlight")]
    public async Task DisposeAsync_ReturnsAfterTheRunEnds_AndTheRunsFinallyDoesNotThrow()
    {
        var (coordinator, service, _) = Create(
            [Candidate(1, "A"), Candidate(2, "A")],
            _ => MetadataRefreshOutcome.Updated);
        service.Gate = Signal();

        var started = await coordinator.StartAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: false),
            CancellationToken.None);
        await service.Entered.Task.WaitAsync(TestTimeout);

        // Captured before disposal so the run's own task is observed on both sides of it. The
        // old Dispose took the gate and the source away from underneath a live run, and what
        // came back out of the run's finally was ObjectDisposedException on a semaphore.
        var inFlight = coordinator.WaitForIdleAsync(TestTimeout);
        await coordinator.DisposeAsync();

        await inFlight;
        await coordinator.WaitForIdleAsync(TestTimeout);

        var final = coordinator.Find(started.Run.RunId);
        Assert.NotNull(final);
        Assert.Equal("Cancelled", final.Status);
        Assert.Single(service.Seen);

        // Idempotent, because a host that disposes twice is not a host that should crash.
        await coordinator.DisposeAsync();
    }

    [Fact]
    [Trait("Scenario", "CancelStopsAtABookBoundary")]
    public async Task Cancel_StopsTheRun_WithoutStartingTheNextBook()
    {
        var (coordinator, service, _) = Create(
            [Candidate(1, "A"), Candidate(2, "A"), Candidate(3, "A")],
            _ => MetadataRefreshOutcome.Updated);
        service.Gate = Signal();

        var started = await coordinator.StartAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: false),
            CancellationToken.None);

        // Without this the cancel can land before the loop enters its first book, and the
        // assertion below would be measuring the scheduler rather than the book boundary.
        await service.Entered.Task.WaitAsync(TestTimeout);
        Assert.True(coordinator.Cancel(started.Run.RunId));
        service.Gate.SetResult();
        await coordinator.WaitForIdleAsync(TestTimeout);

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
        var repository = DueRepository([]);
        repository
            .Setup(r => r.GetAudiobookIdsByAuthorNameAsync("Monitored Author", It.IsAny<CancellationToken>()))
            .ReturnsAsync([7, 8]);
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
    [Trait("Scenario", "AuthorScopeFiltersInTheDatabase")]
    public async Task RunToCompletionAsync_AsksTheDatabaseWhichOfTheAuthorsBooksAreDue()
    {
        var service = new StubRefreshService(_ => MetadataRefreshOutcome.Updated);
        var repository = DueRepository([]);
        repository
            .Setup(r => r.GetAudiobookIdsByAuthorNameAsync("Monitored Author", It.IsAny<CancellationToken>()))
            .ReturnsAsync([7, 8, 9]);
        repository
            .Setup(r => r.FilterAudiobookIdsDueForMetadataRefreshAsync(
                It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([8]);
        var authors = new Mock<IMonitoredAuthorRepository>();
        authors
            .Setup(a => a.GetByIdAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonitoredAuthor { Id = 3, AuthorName = "Monitored Author" });

        var coordinator = Coordinator(service, repository.Object, authors.Object);

        var run = await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Author, 3, Force: false),
            CancellationToken.None);

        Assert.NotNull(run);
        Assert.Equal([8], service.Seen);

        // The staleness predicate goes to the database with the author's ids. Pulling the whole
        // library's due set back to intersect it read every row's author JSON to answer a
        // question about three books.
        repository.Verify(
            r => r.FilterAudiobookIdsDueForMetadataRefreshAsync(
                It.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { 7, 8, 9 })),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        repository.Verify(
            r => r.GetAudiobooksDueForMetadataRefreshAsync(
                It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    [Trait("Scenario", "ASilentProviderDoesNotStampAMiss")]
    public async Task RunToCompletionAsync_LeavesTheTimestampUnset_WhenNothingAnsweredForTheBook()
    {
        var (coordinator, service, repository) = Create(
            [Candidate(1, "A"), Candidate(2, "A")],
            _ => MetadataRefreshOutcome.NotFound);

        // Book 1's walk was answered and came back empty; book 2's was never answered at all.
        // Both report NotFound, and the outcome on its own cannot tell them apart.
        service.ProviderAnswers = id => id == 1 ? 2 : 0;

        var run = await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: false),
            CancellationToken.None);

        Assert.NotNull(run);

        // Stamping the second would hide it for the whole staleness window on the strength of a
        // provider that never said anything, which is what a throttled sweep looks like.
        repository.Verify(
            r => r.StampMetadataRefreshAsync(1, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        repository.Verify(
            r => r.StampMetadataRefreshAsync(2, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    [Trait("Scenario", "UnknownAuthorIsNotAnEmptyRun")]
    public async Task RunToCompletionAsync_Raises_WhenTheAuthorIdIsUnknown()
    {
        // The author repository answers null and the due repository has books in it, so an
        // empty result here cannot be mistaken for the queue simply being empty. The old shape
        // of this test passed with the unknown-author guard deleted.
        var repository = DueRepository([Candidate(1, "A")]);
        var service = new StubRefreshService(_ => MetadataRefreshOutcome.Updated);
        var coordinator = Coordinator(
            service,
            repository.Object,
            Mock.Of<IMonitoredAuthorRepository>());

        var thrown = await Assert.ThrowsAsync<ApplicationNotFoundException>(
            () => coordinator.RunToCompletionAsync(
                new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Author, 99, Force: false),
                CancellationToken.None));

        // Accepting the id handed the caller a run, a total of zero and a Completed status,
        // which reads as "that author is up to date" rather than "there is no such author".
        Assert.Equal("monitored_author_not_found", thrown.Code);
        Assert.Empty(service.Seen);

        // And the gate it took to get as far as the scope query is released again.
        var after = await coordinator.StartAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: true),
            CancellationToken.None);
        await coordinator.WaitForIdleAsync(TestTimeout);
        Assert.True(after.Started);
    }

    [Fact]
    [Trait("Scenario", "AKnownAuthorWithNothingDueIsAnEmptyRun")]
    public async Task RunToCompletionAsync_CompletesWithNothing_WhenTheAuthorHasNoDueBooks()
    {
        var authors = new Mock<IMonitoredAuthorRepository>();
        authors
            .Setup(a => a.GetByIdAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonitoredAuthor { Id = 7, AuthorName = "Up To Date" });
        var repository = DueRepository([]);
        repository
            .Setup(r => r.GetAudiobookIdsByAuthorNameAsync("Up To Date", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var service = new StubRefreshService(_ => MetadataRefreshOutcome.Updated);
        var coordinator = Coordinator(service, repository.Object, authors.Object);

        var run = await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Author, 7, Force: false),
            CancellationToken.None);

        // The control for the test above: an author who exists and has nothing due is still a
        // completed run of zero books, and must not be confused with an id that names nobody.
        Assert.NotNull(run);
        Assert.Equal(0, run.TotalBooks);
        Assert.Equal("Completed", run.Status);
        Assert.Empty(service.Seen);
    }

    [Fact]
    [Trait("Scenario", "LibraryScopeReachesUnmonitoredAuthors")]
    public async Task RunToCompletionAsync_TakesEveryDueBook_IncludingAuthorsNobodyMonitors()
    {
        // The per-author trigger keys on MonitoredAuthor rows, so this is the only path that
        // reaches the rest of the library, which is where the stalest metadata tends to be.
        var (coordinator, service, repository) = Create(
            [Candidate(1, "Monitored Author"), Candidate(2, "Nobody Monitors This One")],
            _ => MetadataRefreshOutcome.Updated);

        var run = await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: true),
            CancellationToken.None);

        Assert.NotNull(run);
        Assert.Equal("Library", run.Scope);
        Assert.Equal([1, 2], service.Seen);

        // An operator asking for the library gets far more than one cycle's worth, but not an
        // unbounded list. The run is windowed like every other scope, so past some point it
        // would be materialising rows it can never reach; a limit of int.MaxValue meant a
        // library-wide trigger loaded the whole library and held it for the run.
        repository.Verify(
            r => r.GetAudiobooksDueForMetadataRefreshAsync(
                It.IsAny<DateTime>(),
                It.Is<int>(limit => limit >= 5000 && limit < int.MaxValue),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    [Trait("Scenario", "AScheduledCycleAsksForWhatItCanPayFor")]
    public async Task RunToCompletionAsync_LimitsAScheduledCycle_ToTheRequestsItsWindowAllows()
    {
        var service = new StubRefreshService(_ => MetadataRefreshOutcome.Updated);
        var repository = DueRepository([]);
        var coordinator = Coordinator(
            service,
            repository.Object,
            Mock.Of<IMonitoredAuthorRepository>(),
            new MetadataRefreshOptionsHolder
            {
                Current = new MetadataRefreshOptions(
                    IntervalHours: 24,
                    RequestsPerHour: 60,
                    MinimumSpacingMs: 0)
            });

        await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Scheduled, null, Force: false),
            CancellationToken.None);

        // TotalBooks is what the cycle means to reach. Asking for the whole due set made every
        // cycle over a library bigger than one window's budget report itself as unfinished.
        repository.Verify(
            r => r.GetAudiobooksDueForMetadataRefreshAsync(
                It.IsAny<DateTime>(), 60 * 24, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    [Trait("Scenario", "AWindowThatClosesEarlyIsTruncated")]
    public async Task RunToCompletionAsync_EndsTruncated_WhenTheWindowClosesWithBooksLeft()
    {
        var clock = new ManualClock();
        var service = new StubRefreshService(_ => MetadataRefreshOutcome.Updated);
        var coordinator = Coordinator(
            service,
            DueRepository(
                [Candidate(1, "A"), Candidate(2, "A"), Candidate(3, "A"), Candidate(4, "A")]).Object,
            Mock.Of<IMonitoredAuthorRepository>(),
            new MetadataRefreshOptionsHolder
            {
                Current = new MetadataRefreshOptions(
                    IntervalHours: 1,
                    RequestsPerHour: 1,
                    MinimumSpacingMs: 0)
            },
            clock: clock);

        var run = await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Scheduled, null, Force: false),
            CancellationToken.None);

        // One request an hour inside a one-hour window pays for the second book by waiting out
        // the rest of the window; the third falls past the end of it and the fourth is never
        // reached. Completed would have said the cycle got through its list.
        Assert.NotNull(run);
        Assert.Equal("Truncated", run.Status);
        Assert.Equal(4, run.TotalBooks);
        Assert.True(
            run.Processed < run.TotalBooks,
            $"processed {run.Processed} of {run.TotalBooks}, so there was nothing for Truncated to mean");
    }

    [Fact]
    [Trait("Scenario", "SettingsApplyToAnApiStartedRun")]
    public async Task AdmitAsync_ReadsTheOperatorsSettings_BeforeItDecidesWhatIsStale()
    {
        var clock = new ManualClock();
        var service = new StubRefreshService(_ => MetadataRefreshOutcome.Updated);
        var repository = DueRepository([Candidate(1, "A")]);
        var configuration = new Mock<IConfigurationService>();
        configuration
            .Setup(c => c.GetApplicationSettingsAsync())
            .ReturnsAsync(new ApplicationSettings
            {
                MetadataRefreshStaleAfterDays = 1,
                MetadataRefreshMinimumSpacingMs = 0
            });
        var holder = new MetadataRefreshOptionsHolder();
        var coordinator = Coordinator(
            service,
            repository.Object,
            Mock.Of<IMonitoredAuthorRepository>(),
            holder,
            configuration.Object,
            clock: clock);

        await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: false),
            CancellationToken.None);

        // Only the scheduled processor wrote the holder, and its first cycle is ten minutes
        // after start, or never at all when hosted services are off. An operator who set one
        // day and then triggered a refresh got the record's thirty.
        Assert.Equal(1, holder.Current.StaleAfterDays);
        repository.Verify(
            r => r.GetAudiobooksDueForMetadataRefreshAsync(
                clock.GetUtcNow().UtcDateTime.AddDays(-1),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    [Trait("Scenario", "AScopeWithNoSettingsKeepsWhatItHas")]
    public async Task AdmitAsync_KeepsTheOptionsInUse_WhenTheScopeHasNoConfigurationService()
    {
        var holder = new MetadataRefreshOptionsHolder
        {
            Current = new MetadataRefreshOptions(StaleAfterDays: 7, MinimumSpacingMs: 0)
        };
        var service = new StubRefreshService(_ => MetadataRefreshOutcome.Updated);
        var coordinator = Coordinator(
            service,
            DueRepository([Candidate(1, "A")]).Object,
            Mock.Of<IMonitoredAuthorRepository>(),
            holder);

        var run = await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: false),
            CancellationToken.None);

        // Yesterday's budget beats no budget, so a settings service that will not resolve is a
        // Debug line rather than a refusal to run.
        Assert.NotNull(run);
        Assert.Equal("Completed", run.Status);
        Assert.Equal(7, holder.Current.StaleAfterDays);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    [Trait("Scenario", "OneBucketAcrossRuns")]
    public async Task ExecuteAsync_KeepsSpendingTheSameBucket_WhenARunIsRestarted()
    {
        var clock = new ManualClock();
        var service = new StubRefreshService(_ => MetadataRefreshOutcome.Updated);
        var coordinator = Coordinator(
            service,
            DueRepository([Candidate(1, "A"), Candidate(2, "A")]).Object,
            Mock.Of<IMonitoredAuthorRepository>(),
            new MetadataRefreshOptionsHolder
            {
                Current = new MetadataRefreshOptions(
                    IntervalHours: 24,
                    RequestsPerHour: 2,
                    MinimumSpacingMs: 0)
            },
            clock: clock);

        await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: true),
            CancellationToken.None);

        // Two books at two an hour empties the bucket and costs no time: it started full.
        var afterTheFirstRun = clock.GetUtcNow();

        await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: true),
            CancellationToken.None);

        // The second run has to wait half an hour for its first token. Building the bucket
        // inside the run gave every trigger a full one, so POST, DELETE, POST again spent the
        // operator's hourly ceiling as many times as the caller cared to ask.
        Assert.True(
            clock.GetUtcNow() - afterTheFirstRun >= TimeSpan.FromMinutes(29),
            $"the second run waited {clock.GetUtcNow() - afterTheFirstRun}, so it got a fresh allowance");
    }

    [Fact]
    [Trait("Scenario", "ALibraryRunIsWindowedToo")]
    public async Task RunToCompletionAsync_EndsTruncated_WhenALibraryRunOutlivesItsWindow()
    {
        var clock = new ManualClock();
        var service = new StubRefreshService(_ => MetadataRefreshOutcome.Updated);
        var coordinator = Coordinator(
            service,
            DueRepository(
                [Candidate(1, "A"), Candidate(2, "A"), Candidate(3, "A"), Candidate(4, "A")]).Object,
            Mock.Of<IMonitoredAuthorRepository>(),
            new MetadataRefreshOptionsHolder
            {
                Current = new MetadataRefreshOptions(
                    IntervalHours: 1,
                    RequestsPerHour: 1,
                    MinimumSpacingMs: 0)
            },
            clock: clock);

        var run = await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: true),
            CancellationToken.None);

        // An unwindowed library run over a real library walks for days holding the single gate,
        // and every scheduled cycle in that time finds it taken and does nothing. The books it
        // did not reach keep their unset timestamps and stay at the head of the queue.
        Assert.NotNull(run);
        Assert.Equal("Truncated", run.Status);
        Assert.True(
            run.Processed < run.TotalBooks,
            $"processed {run.Processed} of {run.TotalBooks}, so the window never closed");
    }

    [Fact]
    [Trait("Scenario", "ASkippedCycleIsVisibleAtTheDefaultLevel")]
    public async Task RunToCompletionAsync_LogsTheSkippedCycle_AtInformation()
    {
        var logger = new CapturingLogger<MetadataRefreshCoordinator>();
        var service = new StubRefreshService(_ => MetadataRefreshOutcome.Updated);
        var coordinator = Coordinator(
            service,
            DueRepository([Candidate(1, "A")]).Object,
            Mock.Of<IMonitoredAuthorRepository>(),
            logger: logger);
        service.Gate = Signal();

        var started = await coordinator.StartAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: true),
            CancellationToken.None);
        Assert.True(started.Started);
        await service.Entered.Task.WaitAsync(TestTimeout);

        var skipped = await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Scheduled, null, Force: false),
            CancellationToken.None);

        service.Gate.SetResult();
        await coordinator.WaitForIdleAsync(TestTimeout);

        // At Debug this said nothing an operator would ever see, so a library run that held the
        // gate for days looked exactly like a scheduled walk that had nothing to do.
        Assert.Null(skipped);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Information
                && entry.Message.StartsWith("Metadata refresh cycle skipped", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Scenario", "AFailedAdmissionReleasesTheGate")]
    public async Task StartAsync_ReleasesTheGate_WhenTheScopeQueryThrows()
    {
        var repository = DueRepository([]);
        repository
            .Setup(r => r.GetAudiobooksDueForMetadataRefreshAsync(
                It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("the database is not there"));
        var service = new StubRefreshService(_ => MetadataRefreshOutcome.Updated);
        var coordinator = Coordinator(
            service,
            repository.Object,
            Mock.Of<IMonitoredAuthorRepository>());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.StartAsync(
                new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: true),
                CancellationToken.None));

        // The gate is taken before the scope query, so a repository that throws while a run is
        // being admitted is the one path that can wedge it for the life of the process and
        // answer 409 to everything until a restart.
        repository
            .Setup(r => r.GetAudiobooksDueForMetadataRefreshAsync(
                It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MetadataRefreshCandidate(1, "A", null)]);

        var second = await coordinator.StartAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: true),
            CancellationToken.None);
        await coordinator.WaitForIdleAsync(TestTimeout);

        Assert.True(second.Started);
        Assert.Equal([1], service.Seen);
    }

    [Fact]
    [Trait("Scenario", "ProgressAdvancesWhileRunning")]
    public async Task Current_ReportsPartialProgress_WhileTheRunIsStillGoing()
    {
        var (coordinator, service, _) = Create(
            [Candidate(1, "A"), Candidate(2, "A"), Candidate(3, "A")],
            _ => MetadataRefreshOutcome.Updated);
        service.Gate = Signal();

        var started = await coordinator.StartAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: true),
            CancellationToken.None);
        Assert.True(started.Started);
        Assert.Equal(3, started.Run.TotalBooks);

        // StartAsync returns before the background loop is scheduled, so without this the
        // status read below races the loop rather than observing a run in flight.
        await service.Entered.Task.WaitAsync(TestTimeout);

        var running = coordinator.Current();
        Assert.NotNull(running);
        Assert.Equal("Running", running.Status);
        Assert.Equal(3, running.TotalBooks);
        // Inside the first book and not yet out of it. A counter already reading three would
        // mean the snapshot was of a finished run and the name of this test was a lie.
        Assert.Equal(0, running.Processed);

        service.Gate.SetResult();
        await coordinator.WaitForIdleAsync(TestTimeout);

        var finished = coordinator.Current();
        Assert.NotNull(finished);
        Assert.Equal("Completed", finished.Status);
        Assert.Equal(3, finished.Processed);
    }

    [Fact]
    [Trait("Scenario", "AStrayTimeoutIsAFailureNotACancellation")]
    public async Task RunToCompletionAsync_ReportsFailed_WhenACancellationCameFromSomethingElse()
    {
        // A refresh that throws OperationCanceledException with nobody having cancelled the
        // run: an HttpClient timeout, or a linked token inside some dependency. Reported as
        // Cancelled it reads as "the operator stopped this", and an operator looking at the
        // status has no way to know that something in fact went wrong.
        var service = new ThrowingRefreshService(new OperationCanceledException("a timeout elsewhere"));
        var coordinator = Coordinator(
            service,
            DueRepository([Candidate(1, "A")]).Object,
            Mock.Of<IMonitoredAuthorRepository>());

        var run = await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: true),
            CancellationToken.None);

        Assert.NotNull(run);
        Assert.Equal("Failed", run.Status);
    }

    [Fact]
    [Trait("Scenario", "ARealCancellationIsStillCancelled")]
    public async Task StartAsync_ReportsCancelled_WhenTheRunItselfWasCancelled()
    {
        // The control for the test above. Gating the Cancelled state on the run's own token
        // must not lose the state for the case it exists to report.
        var (coordinator, service, _) = Create(
            [Candidate(1, "A"), Candidate(2, "A")],
            _ => MetadataRefreshOutcome.Updated);
        service.Gate = Signal();

        var started = await coordinator.StartAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: true),
            CancellationToken.None);
        await service.Entered.Task.WaitAsync(TestTimeout);

        Assert.True(coordinator.Cancel(started.Run.RunId));
        service.Gate.SetResult();
        await coordinator.WaitForIdleAsync(TestTimeout);

        Assert.Equal("Cancelled", coordinator.Find(started.Run.RunId)!.Status);
    }

    [Fact]
    [Trait("Scenario", "CancellingAFinishedRunIsAnAnswerNotAFault")]
    public async Task Cancel_ReturnsFalse_WhenTheRunsTokenSourceIsAlreadyGone()
    {
        var (coordinator, _, _) = Create(
            [Candidate(1, "A")],
            _ => MetadataRefreshOutcome.Updated);

        var started = await coordinator.StartAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Library, null, Force: true),
            CancellationToken.None);
        await coordinator.WaitForIdleAsync(TestTimeout);

        // A run's token source is disposed when the next run is admitted, and again on
        // shutdown. Cancel captures the source under the state lock and cancels it outside, so
        // in the gap it can find one already disposed; that used to leave an
        // ObjectDisposedException to become a 500 for a run that had simply already ended.
        //
        // This pins the contract rather than the race: cancelling a run that is over is a
        // false, not a fault. The race itself is a two-thread window that cannot be made
        // deterministic without a seam in the coordinator, and it did not seem worth one.
        await coordinator.DisposeAsync();

        Assert.False(coordinator.Cancel(started.Run.RunId));
    }

    [Fact]
    [Trait("Scenario", "TheScheduledCycleIsDrainedToo")]
    public async Task RunToCompletionAsync_IsVisibleToTheDrain_WhileItIsStillInsideABook()
    {
        // The scheduled walk runs in its caller's context rather than on a task the coordinator
        // owns, so it has to publish itself for the shutdown drain. Without that, a shutdown
        // during the cycle waits on whatever the last StartAsync left behind, finds it
        // complete, and tears the root provider down under a run still resolving scopes.
        var (coordinator, service, _) = Create(
            [Candidate(1, "A"), Candidate(2, "A")],
            _ => MetadataRefreshOutcome.Updated);
        service.Gate = Signal();

        var cycle = coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Scheduled, null, Force: true),
            CancellationToken.None);
        await service.Entered.Task.WaitAsync(TestTimeout);

        await Assert.ThrowsAsync<TimeoutException>(
            () => coordinator.WaitForIdleAsync(TimeSpan.FromMilliseconds(250)));

        service.Gate.SetResult();
        Assert.NotNull(await cycle.WaitAsync(TestTimeout));
        await coordinator.WaitForIdleAsync(TestTimeout);
    }

    /// <summary>A refresh service that fails the same way for every book.</summary>
    private sealed class ThrowingRefreshService(Exception thrown) : IMetadataRefreshService
    {
        public Task<MetadataRefreshResult> RefreshAsync(
            int audiobookId,
            IMetadataRefreshBudget budget,
            CancellationToken cancellationToken) => throw thrown;
    }
}
