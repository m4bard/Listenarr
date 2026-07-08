/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

public sealed class EfMoveQueuePersistenceTests : IAsyncLifetime
{
    private readonly string _databasePath =
        Path.Join(Path.GetTempPath(), "listenarr-tests", $"move-dedupe-{Guid.NewGuid():N}.db");
    private IDbContextFactory<ListenArrDbContext> _factory = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        var options = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite($"Data Source={_databasePath};Pooling=False")
            .Options;
        _factory = new TestDbContextFactory(options);
        await using var db = await _factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ActiveDeduplicationKey_IsUniqueUntilTerminalStatus()
    {
        var persistence = CreatePersistence();
        var first = CreateJob("42:/LIBRARY/BOOK");
        var duplicate = CreateJob("42:/LIBRARY/BOOK");

        await persistence.AddAsync(first);
        await Assert.ThrowsAsync<UniqueConstraintViolationException>(
            () => persistence.AddAsync(duplicate));

        var claimedGeneration = await persistence.TryClaimAsync(
            first.Id,
            "worker-a",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(2));
        await persistence.UpdateStatusAsync(
            first.Id,
            "worker-a",
            claimedGeneration.GetValueOrDefault(),
            MoveJobStatus.Completed,
            MoveJobPhase.Finalizing,
            null,
            MoveFailureKind.None,
            DateTimeOffset.UtcNow);
        await persistence.AddAsync(duplicate);

        Assert.Equal(duplicate.Id, (await persistence.GetActiveByKeyAsync("42:/LIBRARY/BOOK"))?.Id);
    }

    [Fact]
    public async Task ReconcileIdentityKeys_SelectsMostAdvancedLegacyDuplicate()
    {
        await using (var db = await _factory.CreateDbContextAsync())
        {
            db.MoveJobs.AddRange(
                new MoveJob
                {
                    AudiobookId = 42,
                    RequestedPath = "/library/book",
                    Status = MoveJobStatus.Queued,
                    Phase = MoveJobPhase.Planned,
                    IdentityKeyVersion = 1,
                    ActiveDeduplicationKey = "legacy:first"
                },
                new MoveJob
                {
                    AudiobookId = 42,
                    RequestedPath = "/library/book",
                    Status = MoveJobStatus.Running,
                    Phase = MoveJobPhase.Published,
                    IdentityKeyVersion = 1,
                    ActiveDeduplicationKey = "legacy:second"
                });
            await db.SaveChangesAsync();
        }

        var persistence = CreatePersistence();
        await persistence.ReconcileIdentityKeysAsync();

        await using var verification = await _factory.CreateDbContextAsync();
        var jobs = await verification.MoveJobs.OrderBy(job => job.Phase).ToListAsync();
        Assert.Equal(MoveJobStatus.Superseded, jobs[0].Status);
        Assert.Null(jobs[0].ActiveDeduplicationKey);
        Assert.Equal(MoveJobStatus.Running, jobs[1].Status);
        Assert.StartsWith("v2:move:42:", jobs[1].ActiveDeduplicationKey);
    }

    [Fact]
    public async Task TryClaimAsync_ConcurrentWorkers_OnlyOneAcquiresLease()
    {
        var persistence = CreatePersistence();
        var job = CreateJob("v2:move:42:s:claim");
        await persistence.AddAsync(job);
        var now = DateTimeOffset.UtcNow;

        var claims = await Task.WhenAll(
            persistence.TryClaimAsync(job.Id, "worker-a", now, now.AddMinutes(2)),
            persistence.TryClaimAsync(job.Id, "worker-b", now, now.AddMinutes(2)));

        Assert.Single(claims, generation => generation.HasValue);
        var claimedJob = await persistence.GetByIdAsync(job.Id);
        Assert.Equal(MoveJobStatus.Running, claimedJob!.Status);
        Assert.Contains(claimedJob.LeaseOwner, new[] { "worker-a", "worker-b" });
        Assert.Equal(1, claimedJob.LeaseGeneration);
    }

    [Fact]
    public async Task TryClaimAsync_ExpiredLease_IncrementsLeaseGeneration()
    {
        var persistence = CreatePersistence();
        var job = CreateJob("v2:move:42:s:reclaim");
        await persistence.AddAsync(job);
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(1, await persistence.TryClaimAsync(
            job.Id,
            "worker-a",
            now,
            now.AddMinutes(2)));

        await using (var db = await _factory.CreateDbContextAsync())
        {
            var claimedJob = await db.MoveJobs.SingleAsync(candidate => candidate.Id == job.Id);
            claimedJob.LeaseExpiresAt = now.AddSeconds(-1).UtcDateTime;
            await db.SaveChangesAsync();
        }

        Assert.Equal(2, await persistence.TryClaimAsync(
            job.Id,
            "worker-b",
            now,
            now.AddMinutes(2)));

        var reclaimedJob = await persistence.GetByIdAsync(job.Id);
        Assert.Equal(2, reclaimedJob!.LeaseGeneration);
        Assert.Equal("worker-b", reclaimedJob.LeaseOwner);
    }

    [Fact]
    public async Task MatchingUnexpiredOwnership_CanHeartbeatAndUpdateStatus()
    {
        var persistence = CreatePersistence();
        var job = CreateJob("v2:move:42:s:valid");
        await persistence.AddAsync(job);
        var now = DateTimeOffset.UtcNow;
        var generation = await persistence.TryClaimAsync(
            job.Id,
            "worker-a",
            now,
            now.AddMinutes(2));
        Assert.Equal(1, generation);

        Assert.True(await persistence.HeartbeatAsync(
            job.Id,
            "worker-a",
            generation.GetValueOrDefault(),
            now.AddSeconds(1),
            now.AddMinutes(3)));
        var beforeIncrement = await persistence.GetByIdAsync(job.Id);
        Assert.True(await persistence.TryIncrementAttemptAsync(
            job.Id,
            "worker-a",
            generation.GetValueOrDefault(),
            now.AddSeconds(1)));
        var afterIncrement = await persistence.GetByIdAsync(job.Id);
        Assert.Equal(1, afterIncrement!.AttemptCount);
        Assert.Equal(beforeIncrement!.Status, afterIncrement.Status);
        Assert.Equal(beforeIncrement.Phase, afterIncrement.Phase);
        Assert.Equal(beforeIncrement.ActiveDeduplicationKey, afterIncrement.ActiveDeduplicationKey);
        Assert.Equal(beforeIncrement.LeaseOwner, afterIncrement.LeaseOwner);
        Assert.Equal(beforeIncrement.LeaseGeneration, afterIncrement.LeaseGeneration);
        Assert.Equal(beforeIncrement.LeaseExpiresAt, afterIncrement.LeaseExpiresAt);
        Assert.True(await persistence.UpdateStatusAsync(
            job.Id,
            "worker-a",
            generation.GetValueOrDefault(),
            MoveJobStatus.Completed,
            MoveJobPhase.Finalizing,
            null,
            MoveFailureKind.None,
            now.AddSeconds(2)));

        var completed = await persistence.GetByIdAsync(job.Id);
        Assert.Equal(MoveJobStatus.Completed, completed!.Status);
        Assert.Null(completed.ActiveDeduplicationKey);
        Assert.Null(completed.LeaseOwner);
        Assert.Null(completed.LeaseExpiresAt);
    }

    [Fact]
    public async Task ExpiredOwnership_CannotHeartbeatOrUpdateStatus()
    {
        var persistence = CreatePersistence();
        var job = CreateJob("v2:move:42:s:expired");
        await persistence.AddAsync(job);
        var now = DateTimeOffset.UtcNow;
        var generation = await persistence.TryClaimAsync(
            job.Id,
            "worker-a",
            now,
            now.AddSeconds(1));
        Assert.Equal(1, generation);

        Assert.False(await persistence.HeartbeatAsync(
            job.Id,
            "worker-a",
            generation.GetValueOrDefault(),
            now.AddSeconds(2),
            now.AddMinutes(3)));
        Assert.False(await persistence.TryIncrementAttemptAsync(
            job.Id,
            "worker-a",
            generation.GetValueOrDefault(),
            now.AddSeconds(2)));
        Assert.False(await persistence.UpdateStatusAsync(
            job.Id,
            "worker-a",
            generation.GetValueOrDefault(),
            MoveJobStatus.Completed,
            MoveJobPhase.Finalizing,
            null,
            MoveFailureKind.None,
            now.AddSeconds(2)));
        Assert.Equal(0, (await persistence.GetByIdAsync(job.Id))!.AttemptCount);
        Assert.Equal(2, await persistence.TryClaimAsync(
            job.Id,
            "worker-b",
            now.AddSeconds(2),
            now.AddMinutes(4)));
    }

    [Fact]
    public async Task WrongOwner_CannotHeartbeatOrUpdateStatus()
    {
        var persistence = CreatePersistence();
        var job = CreateJob("v2:move:42:s:owner");
        await persistence.AddAsync(job);
        var now = DateTimeOffset.UtcNow;
        var generation = await persistence.TryClaimAsync(
            job.Id,
            "worker-a",
            now,
            now.AddMinutes(2));

        Assert.False(await persistence.HeartbeatAsync(
            job.Id,
            "worker-b",
            generation.GetValueOrDefault(),
            now,
            now.AddMinutes(3)));
        Assert.False(await persistence.TryIncrementAttemptAsync(
            job.Id,
            "worker-b",
            generation.GetValueOrDefault(),
            now));
        Assert.False(await persistence.UpdateStatusAsync(
            job.Id,
            "worker-b",
            generation.GetValueOrDefault(),
            MoveJobStatus.Completed,
            MoveJobPhase.Finalizing,
            null,
            MoveFailureKind.None,
            now));
        Assert.Equal(0, (await persistence.GetByIdAsync(job.Id))!.AttemptCount);
    }

    [Fact]
    public async Task NonRunningJob_CannotHeartbeatOrUpdateStatus()
    {
        var persistence = CreatePersistence();
        var job = CreateJob("v2:move:42:s:queued");
        await persistence.AddAsync(job);
        var now = DateTimeOffset.UtcNow;

        Assert.False(await persistence.HeartbeatAsync(
            job.Id,
            "worker-a",
            1,
            now,
            now.AddMinutes(3)));
        Assert.False(await persistence.TryIncrementAttemptAsync(
            job.Id,
            "worker-a",
            1,
            now));
        Assert.False(await persistence.UpdateStatusAsync(
            job.Id,
            "worker-a",
            1,
            MoveJobStatus.Completed,
            MoveJobPhase.Finalizing,
            null,
            MoveFailureKind.None,
            now));
        Assert.Equal(0, (await persistence.GetByIdAsync(job.Id))!.AttemptCount);
    }

    [Fact]
    public async Task StaleLeaseGeneration_CannotHeartbeatOrUpdateStatus()
    {
        var persistence = CreatePersistence();
        var job = CreateJob("v2:move:42:s:fenced");
        await persistence.AddAsync(job);
        var now = DateTimeOffset.UtcNow;
        var staleGeneration = await persistence.TryClaimAsync(
            job.Id,
            "worker-a",
            now,
            now.AddMinutes(2));
        Assert.Equal(1, staleGeneration);

        await using (var db = await _factory.CreateDbContextAsync())
        {
            var claimedJob = await db.MoveJobs.SingleAsync(candidate => candidate.Id == job.Id);
            claimedJob.LeaseExpiresAt = now.AddSeconds(-1).UtcDateTime;
            await db.SaveChangesAsync();
        }

        var currentGeneration = await persistence.TryClaimAsync(
            job.Id,
            "worker-b",
            now,
            now.AddMinutes(2));
        Assert.Equal(2, currentGeneration);

        Assert.False(await persistence.HeartbeatAsync(
            job.Id,
            "worker-a",
            staleGeneration.GetValueOrDefault(),
            now,
            now.AddMinutes(3)));
        Assert.False(await persistence.TryIncrementAttemptAsync(
            job.Id,
            "worker-a",
            staleGeneration.GetValueOrDefault(),
            now));
        Assert.False(await persistence.UpdateStatusAsync(
            job.Id,
            "worker-a",
            staleGeneration.GetValueOrDefault(),
            MoveJobStatus.Completed,
            MoveJobPhase.Finalizing,
            null,
            MoveFailureKind.None,
            now));

        var currentJob = await persistence.GetByIdAsync(job.Id);
        Assert.Equal(MoveJobStatus.Running, currentJob!.Status);
        Assert.Equal("worker-b", currentJob.LeaseOwner);
        Assert.Equal(2, currentJob.LeaseGeneration);
        Assert.Equal(0, currentJob.AttemptCount);
    }

    [Fact]
    public async Task TerminalReconciliationState_WithSameGeneration_CannotBeOverwrittenByStaleWorker()
    {
        var persistence = CreatePersistence();
        var job = CreateJob("v2:move:42:s:superseded");
        await persistence.AddAsync(job);
        var now = DateTimeOffset.UtcNow;
        var generation = await persistence.TryClaimAsync(
            job.Id,
            "worker-a",
            now,
            now.AddMinutes(2));
        Assert.Equal(1, generation);

        await using (var db = await _factory.CreateDbContextAsync())
        {
            var claimedJob = await db.MoveJobs.SingleAsync(candidate => candidate.Id == job.Id);
            claimedJob.Status = MoveJobStatus.Superseded;
            claimedJob.Error = "Superseded by reconciliation.";
            claimedJob.LeaseOwner = null;
            claimedJob.LeaseExpiresAt = null;
            await db.SaveChangesAsync();
        }

        Assert.False(await persistence.UpdateStatusAsync(
            job.Id,
            "worker-a",
            generation.GetValueOrDefault(),
            MoveJobStatus.Failed,
            MoveJobPhase.Finalizing,
            "stale failure",
            MoveFailureKind.Unknown,
            now));

        var currentJob = await persistence.GetByIdAsync(job.Id);
        Assert.Equal(MoveJobStatus.Superseded, currentJob!.Status);
        Assert.Equal("Superseded by reconciliation.", currentJob.Error);
    }

    [Fact]
    public async Task RequeueAsync_PersistsResetRetryAndLeaseState()
    {
        var persistence = CreatePersistence();
        var future = DateTimeOffset.UtcNow.AddHours(1);
        var job = CreateJob("v2:move:42:s:requeue-reset");
        job.Status = MoveJobStatus.Failed;
        job.Phase = MoveJobPhase.CleaningSource;
        job.Error = "verification failed";
        job.FailureKind = MoveFailureKind.Verification;
        job.NextAttemptAt = future.UtcDateTime;
        job.LeaseOwner = "worker-a";
        job.LeaseExpiresAt = future.UtcDateTime;
        job.LeaseGeneration = 3;
        job.AttemptCount = 2;
        await persistence.AddAsync(job);

        job.Status = MoveJobStatus.Queued;
        job.Phase = MoveJobPhase.None;
        job.Error = null;
        job.FailureKind = MoveFailureKind.None;
        job.NextAttemptAt = null;
        job.LeaseOwner = null;
        job.LeaseExpiresAt = null;
        job.ActiveDeduplicationKey = "v2:move:42:s:requeue-reset-new";
        await persistence.RequeueAsync(job);

        var persisted = await persistence.GetByIdAsync(job.Id);
        Assert.NotNull(persisted);
        Assert.Equal(MoveJobStatus.Queued, persisted.Status);
        Assert.Equal(MoveJobPhase.None, persisted.Phase);
        Assert.Null(persisted.Error);
        Assert.Equal(MoveFailureKind.None, persisted.FailureKind);
        Assert.Null(persisted.NextAttemptAt);
        Assert.Null(persisted.LeaseOwner);
        Assert.Null(persisted.LeaseExpiresAt);
        Assert.Equal("v2:move:42:s:requeue-reset-new", persisted.ActiveDeduplicationKey);
        Assert.Equal(3, persisted.LeaseGeneration);
        Assert.Equal(2, persisted.AttemptCount);
    }

    private EfMoveQueuePersistence CreatePersistence() =>
        new(_factory, BuildSemanticsResolver());

    private static IFileSystemSemanticsResolver BuildSemanticsResolver()
    {
        var resolver = new Mock<IFileSystemSemanticsResolver>();
        resolver.Setup(service => service.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<FileSystemCaseSensitivityMode>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, FileSystemCaseSensitivityMode, CancellationToken>((path, _, _) =>
                ValueTask.FromResult(new FileSystemSemanticsResolution(
                    new FileSystemPathSemantics(FileSystemPathSemantics.CurrentHostDefault.Syntax, FileSystemCaseSensitivity.Sensitive),
                    PathIdentityState.Valid,
                    path)));
        return resolver.Object;
    }

    private static MoveJob CreateJob(string key) => new()
    {
        AudiobookId = 42,
        RequestedPath = "/library/book",
        Status = MoveJobStatus.Queued,
        ActiveDeduplicationKey = key
    };

    private sealed class TestDbContextFactory(DbContextOptions<ListenArrDbContext> options)
        : IDbContextFactory<ListenArrDbContext>
    {
        public ListenArrDbContext CreateDbContext() => new(options);

        public Task<ListenArrDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
