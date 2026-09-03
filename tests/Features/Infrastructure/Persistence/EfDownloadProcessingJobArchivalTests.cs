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
/// A processing job holds the per-attempt ProcessingLog explaining why an import failed, and the
/// seven day retention sweep deletes it. Nothing sweeps a failed or blocked download, so the row
/// stays behind with its cause gone. Retention now copies the job's explanation onto the download
/// before the job is removed. These run against real SQLite rather than the in-memory provider
/// because the archived column is a primitive collection stored as JSON, and detecting an in-place
/// mutation of that list is exactly the part a provider substitution would hide.
/// </summary>
[Trait("Name", "EfDownloadProcessingJobArchivalTests")]
[Trait("Category", "Infrastructure")]
public sealed class EfDownloadProcessingJobArchivalTests : BaseTests
{
    private const int MaxArchivedFailureEntries = 20;

    private static readonly ProcessingJobStatus[] TerminalStatuses =
        [ProcessingJobStatus.Completed, ProcessingJobStatus.Failed];

    private readonly string _databasePath =
        Path.Join(Path.GetTempPath(), "listenarr-tests", $"job-archival-{Guid.NewGuid():N}.db");
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
    [Trait("Scenario", "SweptJobLeavesItsReasonOnTheDownload")]
    public async Task ArchiveAndDeleteCompletedBeforeAsync_FailedJob_PreservesTheReasonOnTheDownload()
    {
        var repository = new EfDownloadProcessingJobRepository(_factory);
        await AddDownloadAsync("blocked-download", DownloadStatus.ImportBlocked);

        var job = CreateJob("blocked-download");
        job.AddLogEntry("Attempt 1: destination unavailable");
        job.MarkAsFailed("Import validation failed");
        job.CompletedAt = DateTime.UtcNow.AddDays(-30);
        await repository.AddAsync(job);

        var removed = await repository.ArchiveAndDeleteCompletedBeforeAsync(
            TerminalStatuses,
            DateTime.UtcNow.AddDays(-7));

        Assert.Equal(1, removed);
        Assert.Null(await repository.GetByIdAsync(job.Id));

        var download = await GetDownloadAsync("blocked-download");
        Assert.NotNull(download.ArchivedFailureDetail);
        var entry = Assert.Single(download.ArchivedFailureDetail!);
        Assert.Contains("Import validation failed", entry, StringComparison.Ordinal);
        Assert.Contains(job.Id, entry, StringComparison.Ordinal);

        // The entry says how much detail went with the job, so nobody reads it as the full account.
        Assert.Contains("processing log entries were discarded", entry, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Scenario", "SucceededJobsLeaveNothingBehind")]
    public async Task ArchiveAndDeleteCompletedBeforeAsync_CompletedJob_ArchivesNothing()
    {
        var repository = new EfDownloadProcessingJobRepository(_factory);
        await AddDownloadAsync("imported-download", DownloadStatus.Moved);

        var job = CreateJob("imported-download").MarkAsCompleted();
        job.CompletedAt = DateTime.UtcNow.AddDays(-30);
        await repository.AddAsync(job);

        var removed = await repository.ArchiveAndDeleteCompletedBeforeAsync(
            TerminalStatuses,
            DateTime.UtcNow.AddDays(-7));

        Assert.Equal(1, removed);

        var download = await GetDownloadAsync("imported-download");
        Assert.True(download.ArchivedFailureDetail is null or { Count: 0 });
    }

    [Fact]
    [Trait("Scenario", "ClearedDownloadHasNothingLeftToExplain")]
    public async Task ArchiveAndDeleteCompletedBeforeAsync_DownloadAlreadyGone_StillSweepsTheJob()
    {
        var repository = new EfDownloadProcessingJobRepository(_factory);

        var job = CreateJob("cleared-download").MarkAsFailed("Import validation failed");
        job.CompletedAt = DateTime.UtcNow.AddDays(-30);
        await repository.AddAsync(job);

        var removed = await repository.ArchiveAndDeleteCompletedBeforeAsync(
            TerminalStatuses,
            DateTime.UtcNow.AddDays(-7));

        Assert.Equal(1, removed);
        Assert.Null(await repository.GetByIdAsync(job.Id));
    }

    [Fact]
    [Trait("Scenario", "JobInsideTheWindowKeepsItsOwnDetail")]
    public async Task ArchiveAndDeleteCompletedBeforeAsync_JobInsideWindow_ArchivesNothing()
    {
        var repository = new EfDownloadProcessingJobRepository(_factory);
        await AddDownloadAsync("recent-download", DownloadStatus.ImportBlocked);

        var job = CreateJob("recent-download").MarkAsFailed("Import validation failed");
        job.CompletedAt = DateTime.UtcNow.AddDays(-1);
        await repository.AddAsync(job);

        var removed = await repository.ArchiveAndDeleteCompletedBeforeAsync(
            TerminalStatuses,
            DateTime.UtcNow.AddDays(-7));

        Assert.Equal(0, removed);

        var download = await GetDownloadAsync("recent-download");
        Assert.True(download.ArchivedFailureDetail is null or { Count: 0 });
    }

    [Fact]
    [Trait("Scenario", "ChronicFailureCannotGrowTheDownloadRowWithoutBound")]
    public async Task ArchiveAndDeleteCompletedBeforeAsync_ManyFailures_KeepsOnlyTheMostRecentEntries()
    {
        var repository = new EfDownloadProcessingJobRepository(_factory);
        await AddDownloadAsync("chronic-download", DownloadStatus.ImportBlocked);

        const int failureCount = MaxArchivedFailureEntries + 5;
        for (var i = 0; i < failureCount; i++)
        {
            var job = CreateJob("chronic-download").MarkAsFailed($"Import validation failed #{i}");
            job.CompletedAt = DateTime.UtcNow.AddDays(-30).AddMinutes(i);
            await repository.AddAsync(job);
        }

        var removed = await repository.ArchiveAndDeleteCompletedBeforeAsync(
            TerminalStatuses,
            DateTime.UtcNow.AddDays(-7));

        Assert.Equal(failureCount, removed);

        var download = await GetDownloadAsync("chronic-download");
        Assert.NotNull(download.ArchivedFailureDetail);
        Assert.Equal(MaxArchivedFailureEntries, download.ArchivedFailureDetail!.Count);

        // Oldest first is what gets dropped, so the surviving window is the most recent failures.
        Assert.DoesNotContain(
            download.ArchivedFailureDetail,
            candidate => candidate.Contains("failed #0.", StringComparison.Ordinal));
        Assert.Contains(
            download.ArchivedFailureDetail,
            candidate => candidate.Contains($"failed #{failureCount - 1}.", StringComparison.Ordinal));
    }

    private async Task AddDownloadAsync(string downloadId, DownloadStatus status)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.Downloads.Add(new Download
        {
            Id = downloadId,
            Title = "Archival Fixture",
            Status = status,
            StartedAt = DateTime.UtcNow.AddDays(-31)
        });
        await db.SaveChangesAsync();
    }

    private async Task<Download> GetDownloadAsync(string downloadId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Downloads.SingleAsync(download => download.Id == downloadId);
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
