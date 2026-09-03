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
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// Retention for a processing job is tied to the download it explains. The job holds the
/// per-attempt ProcessingLog, and a failed or blocked download is never swept by anything, so
/// sweeping the job on its own age leaves a permanent queue entry whose cause has been deleted.
/// These run against real SQLite rather than the in-memory provider because the surviving-download
/// check is a correlated subquery, and the in-memory provider would evaluate it client-side and
/// pass even if it did not translate.
/// </summary>
[Trait("Name", "EfDownloadProcessingJobRetentionTests")]
[Trait("Category", "Infrastructure")]
public sealed class EfDownloadProcessingJobRetentionTests : BaseTests
{
    private static readonly ProcessingJobStatus[] TerminalStatuses =
        [ProcessingJobStatus.Completed, ProcessingJobStatus.Failed];

    private readonly string _databasePath =
        Path.Join(Path.GetTempPath(), "listenarr-tests", $"job-retention-{Guid.NewGuid():N}.db");
    private IDbContextFactory<ListenArrDbContext> _factory = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        var options = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite($"Data Source={_databasePath};Pooling=False")
            .Options;
        _factory = new TestDbContextFactory(options);
        await using var db = await _factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    [Trait("Scenario", "BlockedDownloadKeepsItsFailureDetailPastTheRetentionWindow")]
    public async Task DeleteOrphanedCompletedBeforeAsync_DownloadStillPresent_RetainsTheJob()
    {
        var repository = new EfDownloadProcessingJobRepository(_factory);
        await AddDownloadAsync("blocked-download", DownloadStatus.ImportBlocked);

        var job = CreateJob("blocked-download").MarkAsFailed("Import validation failed");
        job.CompletedAt = DateTime.UtcNow.AddDays(-30);
        await repository.AddAsync(job);

        var removed = await repository.DeleteOrphanedCompletedBeforeAsync(
            TerminalStatuses,
            DateTime.UtcNow.AddDays(-7));

        Assert.Equal(0, removed);
        var surviving = await repository.GetByIdAsync(job.Id);
        Assert.NotNull(surviving);
        Assert.Contains(surviving!.ProcessingLog, entry => entry.Contains("Import validation failed"));
    }

    [Fact]
    [Trait("Scenario", "JobDrainsOnceTheDownloadItExplainsIsGone")]
    public async Task DeleteOrphanedCompletedBeforeAsync_DownloadRemoved_DeletesTheJob()
    {
        var repository = new EfDownloadProcessingJobRepository(_factory);

        var job = CreateJob("cleared-download").MarkAsFailed("Import validation failed");
        job.CompletedAt = DateTime.UtcNow.AddDays(-30);
        await repository.AddAsync(job);

        var removed = await repository.DeleteOrphanedCompletedBeforeAsync(
            TerminalStatuses,
            DateTime.UtcNow.AddDays(-7));

        Assert.Equal(1, removed);
        Assert.Null(await repository.GetByIdAsync(job.Id));
    }

    [Fact]
    [Trait("Scenario", "RetentionCouplingDoesNotResurrectJobsInsideTheWindow")]
    public async Task DeleteOrphanedCompletedBeforeAsync_OrphanInsideWindow_RetainsTheJob()
    {
        var repository = new EfDownloadProcessingJobRepository(_factory);

        var job = CreateJob("recent-download").MarkAsCompleted();
        job.CompletedAt = DateTime.UtcNow.AddDays(-1);
        await repository.AddAsync(job);

        var removed = await repository.DeleteOrphanedCompletedBeforeAsync(
            TerminalStatuses,
            DateTime.UtcNow.AddDays(-7));

        Assert.Equal(0, removed);
        Assert.NotNull(await repository.GetByIdAsync(job.Id));
    }

    private async Task AddDownloadAsync(string downloadId, DownloadStatus status)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.Downloads.Add(new Download
        {
            Id = downloadId,
            Title = "Retention Fixture",
            Status = status,
            StartedAt = DateTime.UtcNow.AddDays(-31)
        });
        await db.SaveChangesAsync();
    }

    private static DownloadProcessingJob CreateJob(string downloadId) => new()
    {
        DownloadId = downloadId,
        Status = ProcessingJobStatus.Pending,
        JobType = ProcessingJobType.MoveOrCopyFile
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
