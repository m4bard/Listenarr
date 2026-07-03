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
        var persistence = new EfMoveQueuePersistence(_factory);
        var first = CreateJob("42:/LIBRARY/BOOK");
        var duplicate = CreateJob("42:/LIBRARY/BOOK");

        await persistence.AddAsync(first);
        await Assert.ThrowsAsync<UniqueConstraintViolationException>(
            () => persistence.AddAsync(duplicate));

        await persistence.UpdateStatusAsync(
            first.Id,
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

        var persistence = new EfMoveQueuePersistence(_factory);
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
        var persistence = new EfMoveQueuePersistence(_factory);
        var job = CreateJob("v2:move:42:s:claim");
        await persistence.AddAsync(job);
        var now = DateTimeOffset.UtcNow;

        var claims = await Task.WhenAll(
            persistence.TryClaimAsync(job.Id, "worker-a", now, now.AddMinutes(2)),
            persistence.TryClaimAsync(job.Id, "worker-b", now, now.AddMinutes(2)));

        Assert.Single(claims, claimed => claimed);
        var claimedJob = await persistence.GetByIdAsync(job.Id);
        Assert.Equal(MoveJobStatus.Running, claimedJob!.Status);
        Assert.Contains(claimedJob.LeaseOwner, new[] { "worker-a", "worker-b" });
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

        public Task<ListenArrDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
