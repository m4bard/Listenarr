/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Listenarr.Application.Common.Exceptions;
using Listenarr.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Listenarr.Tests.Features.Api.Features.Library;

/// <summary>The transport shapes: 202 on accept, 409 on overlap, 404 on an unknown run.</summary>
[Trait("Area", "Library")]
[Trait("Name", "LibraryMetadataRefreshWorkflowTests")]
[Trait("Category", "Api")]
public class LibraryMetadataRefreshWorkflowTests : BaseTests
{
    private static MetadataRefreshRunSnapshot Snapshot(
        Guid runId,
        string scope = "Author",
        string status = "Running",
        int totalBooks = 4) => new(
        runId,
        scope,
        status,
        totalBooks,
        0,
        0,
        0,
        0,
        0,
        0,
        new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc),
        null);

    private static IConfigurationService Settings(bool enabled) =>
        Mock.Of<IConfigurationService>(configuration =>
            configuration.GetApplicationSettingsAsync() ==
                Task.FromResult(new ApplicationSettings { MetadataRefreshEnabled = enabled }));

    private static IConfigurationService EnabledSettings() => Settings(enabled: true);

    [Fact]
    [Trait("Scenario", "ATriggerRespectsTheSetting")]
    public async Task StartAsync_Returns409_AndAsksTheCoordinatorForNothing_WhenRefreshIsTurnedOff()
    {
        var coordinator = new Mock<IMetadataRefreshCoordinator>();

        var result = await new LibraryMetadataRefreshWorkflow(coordinator.Object, Settings(enabled: false))
            .StartAsync(new MetadataRefreshRequest(), httpContext: null, CancellationToken.None);

        // The setting gated the scheduled walk only, so turning the feature off left a button
        // that started a library-wide run over every book in the library.
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("metadata_refresh_disabled", conflict.Value!.ToString(), StringComparison.Ordinal);
        coordinator.Verify(
            c => c.StartAsync(It.IsAny<MetadataRefreshScopeRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    [Trait("Scenario", "AnUnknownAuthorIs404")]
    public async Task StartAsync_Returns404_WhenTheAuthorIdNamesNobody()
    {
        var coordinator = new Mock<IMetadataRefreshCoordinator>();
        coordinator
            .Setup(c => c.StartAsync(It.IsAny<MetadataRefreshScopeRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApplicationNotFoundException(
                "monitored_author_not_found",
                "No monitored author with that id."));

        var result = await new LibraryMetadataRefreshWorkflow(coordinator.Object, EnabledSettings())
            .StartAsync(new MetadataRefreshRequest(AuthorId: 99), httpContext: null, CancellationToken.None);

        // 202 with a total of zero reads as "that author is up to date", which is a different
        // thing from an id that names nobody, and the caller could not tell the two apart.
        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Contains("monitored_author_not_found", notFound.Value!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "AcceptedCarriesTheRunId")]
    public async Task StartAsync_Returns202_WithTheRunIdScopeAndTotal()
    {
        var runId = Guid.NewGuid();
        var coordinator = new Mock<IMetadataRefreshCoordinator>();
        coordinator
            .Setup(c => c.StartAsync(It.IsAny<MetadataRefreshScopeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataRefreshStartResult(true, Snapshot(runId)));

        var result = await new LibraryMetadataRefreshWorkflow(coordinator.Object, EnabledSettings())
            .StartAsync(new MetadataRefreshRequest(AuthorId: 3), httpContext: null, CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        var body = Assert.IsType<MetadataRefreshRunResponse>(accepted.Value);
        Assert.Equal(runId, body.RunId);
        Assert.Equal("Author", body.Scope);
        Assert.Equal(4, body.TotalBooks);
        Assert.Equal("Running", body.Status);
    }

    [Fact]
    [Trait("Scenario", "AuthorIdBecomesAnAuthorScope")]
    public async Task StartAsync_AsksForAnAuthorScope_WhenAnAuthorIdIsGiven()
    {
        var coordinator = new Mock<IMetadataRefreshCoordinator>();
        coordinator
            .Setup(c => c.StartAsync(It.IsAny<MetadataRefreshScopeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataRefreshStartResult(true, Snapshot(Guid.NewGuid())));

        await new LibraryMetadataRefreshWorkflow(coordinator.Object, EnabledSettings())
            .StartAsync(new MetadataRefreshRequest(AuthorId: 7, Force: true), httpContext: null, CancellationToken.None);

        coordinator.Verify(
            c => c.StartAsync(
                It.Is<MetadataRefreshScopeRequest>(request =>
                    request.Scope == MetadataRefreshRunScope.Author
                    && request.MonitoredAuthorId == 7
                    && request.Force),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    [Trait("Scenario", "OverlapIs409WithTheActiveRunId")]
    public async Task StartAsync_Returns409_NamingTheRunThatHoldsTheGate()
    {
        var activeRunId = Guid.NewGuid();
        var coordinator = new Mock<IMetadataRefreshCoordinator>();
        coordinator
            .Setup(c => c.StartAsync(It.IsAny<MetadataRefreshScopeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataRefreshStartResult(false, Snapshot(activeRunId, scope: "Scheduled")));

        var result = await new LibraryMetadataRefreshWorkflow(coordinator.Object, EnabledSettings())
            .StartAsync(new MetadataRefreshRequest(AuthorId: 3), httpContext: null, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var body = Assert.IsType<MetadataRefreshRunResponse>(conflict.Value);
        Assert.Equal(activeRunId, body.RunId);
        Assert.Equal("Scheduled", body.Scope);
    }

    [Fact]
    [Trait("Scenario", "StatusProjection")]
    public void GetStatus_ProjectsEveryCounter()
    {
        var runId = Guid.NewGuid();
        var coordinator = new Mock<IMetadataRefreshCoordinator>();
        coordinator
            .Setup(c => c.Find(runId))
            .Returns(new MetadataRefreshRunSnapshot(
                runId, "Author", "Completed", 10, 10, 6, 2, 1, 1, 23,
                new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 10, 12, 30, 0, DateTimeKind.Utc)));

        var result = new LibraryMetadataRefreshWorkflow(coordinator.Object, EnabledSettings()).GetStatus(runId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<MetadataRefreshRunStatusResponse>(ok.Value);
        Assert.Equal(10, body.TotalBooks);
        Assert.Equal(10, body.Processed);
        Assert.Equal(6, body.Updated);
        Assert.Equal(2, body.Skipped);
        Assert.Equal(1, body.Deferred);
        Assert.Equal(1, body.Failed);
        Assert.Equal(23, body.RequestsSpent);
        Assert.NotNull(body.CompletedAt);
    }

    [Fact]
    [Trait("Scenario", "UnknownRunIs404")]
    public void GetStatus_Returns404_ForARunThisProcessDoesNotHold()
    {
        var coordinator = new Mock<IMetadataRefreshCoordinator>();
        coordinator.Setup(c => c.Find(It.IsAny<Guid>())).Returns((MetadataRefreshRunSnapshot?)null);

        Assert.IsType<NotFoundObjectResult>(
            new LibraryMetadataRefreshWorkflow(coordinator.Object, EnabledSettings()).GetStatus(Guid.NewGuid()));
    }

    [Fact]
    [Trait("Scenario", "NoAuthorIdIsALibraryRun")]
    public async Task StartAsync_AsksForALibraryScope_WhenNoAuthorIdIsGiven()
    {
        var coordinator = new Mock<IMetadataRefreshCoordinator>();
        coordinator
            .Setup(c => c.StartAsync(It.IsAny<MetadataRefreshScopeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataRefreshStartResult(
                true,
                Snapshot(Guid.NewGuid(), scope: "Library", totalBooks: 1200)));

        var result = await new LibraryMetadataRefreshWorkflow(coordinator.Object, EnabledSettings())
            .StartAsync(new MetadataRefreshRequest(), httpContext: null, CancellationToken.None);

        coordinator.Verify(
            c => c.StartAsync(
                It.Is<MetadataRefreshScopeRequest>(request =>
                    request.Scope == MetadataRefreshRunScope.Library
                    && request.MonitoredAuthorId == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.Equal(1200, Assert.IsType<MetadataRefreshRunResponse>(accepted.Value).TotalBooks);
    }

    [Fact]
    [Trait("Scenario", "AnEmptyBodyIsALibraryRun")]
    public async Task StartAsync_TreatsAMissingBody_AsALibraryRun()
    {
        var coordinator = new Mock<IMetadataRefreshCoordinator>();
        coordinator
            .Setup(c => c.StartAsync(It.IsAny<MetadataRefreshScopeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MetadataRefreshStartResult(true, Snapshot(Guid.NewGuid(), scope: "Library")));

        await new LibraryMetadataRefreshWorkflow(coordinator.Object, EnabledSettings())
            .StartAsync(null, httpContext: null, CancellationToken.None);

        coordinator.Verify(
            c => c.StartAsync(
                It.Is<MetadataRefreshScopeRequest>(request =>
                    request.Scope == MetadataRefreshRunScope.Library && !request.Force),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    [Trait("Scenario", "LatestFallsBackToTheMostRecentRun")]
    public void GetLatest_ReturnsTheActiveRun_OrTheMostRecentOne()
    {
        var runId = Guid.NewGuid();
        var coordinator = new Mock<IMetadataRefreshCoordinator>();
        coordinator.Setup(c => c.Current()).Returns(Snapshot(runId, scope: "Library"));

        var ok = Assert.IsType<OkObjectResult>(
            new LibraryMetadataRefreshWorkflow(coordinator.Object, EnabledSettings()).GetLatest());

        Assert.Equal(runId, Assert.IsType<MetadataRefreshRunStatusResponse>(ok.Value).RunId);
    }

    [Fact]
    [Trait("Scenario", "NoRunSinceStartupIs404")]
    public void GetLatest_Returns404_WhenThisProcessHasRunNothing()
    {
        var coordinator = new Mock<IMetadataRefreshCoordinator>();
        coordinator.Setup(c => c.Current()).Returns((MetadataRefreshRunSnapshot?)null);

        Assert.IsType<NotFoundObjectResult>(
            new LibraryMetadataRefreshWorkflow(coordinator.Object, EnabledSettings()).GetLatest());
    }

    [Fact]
    [Trait("Scenario", "CancelAcceptsThenRefuses")]
    public void Cancel_Returns202_WhenTheRunWasActive_And404_WhenItWasNot()
    {
        var runId = Guid.NewGuid();
        var coordinator = new Mock<IMetadataRefreshCoordinator>();
        coordinator.Setup(c => c.Cancel(runId)).Returns(true);
        coordinator.Setup(c => c.Cancel(It.Is<Guid>(id => id != runId))).Returns(false);
        var workflow = new LibraryMetadataRefreshWorkflow(coordinator.Object, EnabledSettings());

        Assert.IsType<AcceptedResult>(workflow.Cancel(runId));
        Assert.IsType<NotFoundObjectResult>(workflow.Cancel(Guid.NewGuid()));
    }

    private static Mock<IMetadataRefreshCoordinator> AcceptingCoordinator()
    {
        var coordinator = new Mock<IMetadataRefreshCoordinator>();
        coordinator
            .Setup(c => c.StartAsync(It.IsAny<MetadataRefreshScopeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MetadataRefreshScopeRequest _, CancellationToken _) =>
                new MetadataRefreshStartResult(true, Snapshot(Guid.NewGuid())));
        return coordinator;
    }

    [Fact]
    [Trait("Scenario", "ATriggerIsThrottledPerActor")]
    public async Task StartAsync_Returns429_WhenTheSameActorTriggersAgainWithinTheCooldown()
    {
        var coordinator = AcceptingCoordinator();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var workflow = new LibraryMetadataRefreshWorkflow(coordinator.Object, EnabledSettings(), cache);
        var httpContext = new DefaultHttpContext();

        var first = await workflow.StartAsync(new MetadataRefreshRequest(), httpContext, CancellationToken.None);
        Assert.IsType<AcceptedResult>(first);

        var second = await workflow.StartAsync(new MetadataRefreshRequest(), httpContext, CancellationToken.None);

        // The per-book rescan beside this one has had a per-actor cooldown all along. Without
        // one here a held button starts run after run: a run that finds nothing due finishes at
        // once, so the coordinator's own 409 never fires and every click costs a due query.
        var throttled = Assert.IsType<ObjectResult>(second);
        Assert.Equal(StatusCodes.Status429TooManyRequests, throttled.StatusCode);

        var retryAfterSeconds = (int)throttled.Value!.GetType().GetProperty("retryAfterSeconds")!.GetValue(throttled.Value)!;
        Assert.InRange(retryAfterSeconds, 1, 15);
        Assert.Equal(retryAfterSeconds.ToString(), httpContext.Response.Headers["Retry-After"]);

        // And the refused trigger costs the coordinator nothing at all.
        coordinator.Verify(
            c => c.StartAsync(It.IsAny<MetadataRefreshScopeRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    [Trait("Scenario", "ATriggerIsThrottledPerActor")]
    public async Task StartAsync_DoesNotThrottleADifferentScope_ForTheSameActor()
    {
        var coordinator = AcceptingCoordinator();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var workflow = new LibraryMetadataRefreshWorkflow(coordinator.Object, EnabledSettings(), cache);
        var httpContext = new DefaultHttpContext();

        Assert.IsType<AcceptedResult>(
            await workflow.StartAsync(new MetadataRefreshRequest(AuthorId: 7), httpContext, CancellationToken.None));

        // The control. Keyed per actor and per scope, like the rescan's per actor and per book,
        // so refreshing one author does not lock the operator out of every other button.
        Assert.IsType<AcceptedResult>(
            await workflow.StartAsync(new MetadataRefreshRequest(AuthorId: 8), httpContext, CancellationToken.None));
        Assert.IsType<AcceptedResult>(
            await workflow.StartAsync(new MetadataRefreshRequest(), httpContext, CancellationToken.None));
    }

    [Fact]
    [Trait("Scenario", "ATriggerIsThrottledPerActor")]
    public async Task StartAsync_DoesNotThrottleADifferentActor()
    {
        var coordinator = AcceptingCoordinator();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var workflow = new LibraryMetadataRefreshWorkflow(coordinator.Object, EnabledSettings(), cache);

        var first = new DefaultHttpContext();
        first.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.10");
        var second = new DefaultHttpContext();
        second.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.0.2.11");

        Assert.IsType<AcceptedResult>(
            await workflow.StartAsync(new MetadataRefreshRequest(), first, CancellationToken.None));

        // One person clicking twice is throttled; two people are not one person.
        Assert.IsType<AcceptedResult>(
            await workflow.StartAsync(new MetadataRefreshRequest(), second, CancellationToken.None));
    }
}
