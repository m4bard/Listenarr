/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Data.Common;
using Listenarr.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence.Repositories;

public sealed class EfMoveQueuePersistence(
    IDbContextFactory<ListenArrDbContext> dbFactory,
    IFileSystemSemanticsResolver semanticsResolver)
    : IMoveQueuePersistence
{
    public async Task<MoveJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            return await db.MoveJobs.AsNoTracking().SingleOrDefaultAsync(job => job.Id == id, cancellationToken);
        }
        catch (DbException ex)
        {
            throw new PersistenceException("Failed to query move job persistence.", ex);
        }
    }

    public async Task<MoveJob?> GetActiveByKeyAsync(
        string deduplicationKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            return await db.MoveJobs
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    job => job.ActiveDeduplicationKey == deduplicationKey,
                    cancellationToken);
        }
        catch (DbException ex)
        {
            throw new PersistenceException("Failed to query move job persistence.", ex);
        }
    }

    public async Task<IReadOnlyList<MoveJob>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            return await db.MoveJobs
                .AsNoTracking()
                .Where(job => job.ActiveDeduplicationKey != null
                    && (job.Status == MoveJobStatus.Queued
                        || job.Status == MoveJobStatus.Running
                        || job.Status == MoveJobStatus.RetryScheduled))
                .OrderBy(job => job.EnqueuedAt)
                .ToListAsync(cancellationToken);
        }
        catch (DbException ex)
        {
            throw new PersistenceException("Failed to query active move job persistence.", ex);
        }
    }

    public async Task ReconcileIdentityKeysAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var activeJobs = await db.MoveJobs
            .Where(job => job.Status == MoveJobStatus.Queued
                || job.Status == MoveJobStatus.Running
                || job.Status == MoveJobStatus.RetryScheduled)
            .ToListAsync(cancellationToken);

        foreach (var job in activeJobs)
        {
            job.ActiveDeduplicationKey = null;
        }

        await db.SaveChangesAsync(cancellationToken);

        var resolvedJobs = new List<(MoveJob Job, string Key)>();
        foreach (var job in activeJobs.Where(job => !string.IsNullOrWhiteSpace(job.RequestedPath)))
        {
            string absolutePath;
            FileSystemSemanticsResolution resolution;
            try
            {
                absolutePath = FileSystemPathIdentity.ResolveNativeAbsolutePath(job.RequestedPath!);
                resolution = await semanticsResolver.ResolveAsync(
                    absolutePath,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or InvalidOperationException)
            {
                job.Status = MoveJobStatus.NeedsAttention;
                job.FailureKind = MoveFailureKind.Verification;
                job.Error = $"Target path could not be reconciled: {ex.Message}";
                continue;
            }

            if (resolution.State != PathIdentityState.Valid)
            {
                job.Status = MoveJobStatus.NeedsAttention;
                job.FailureKind = MoveFailureKind.Verification;
                job.Error = resolution.Reason ?? "Target filesystem identity is unavailable.";
                continue;
            }

            resolvedJobs.Add((
                job,
                FileSystemPathIdentity.CreateKey($"move:{job.AudiobookId}", absolutePath, resolution.Semantics)));
        }

        var keyedJobs = resolvedJobs.GroupBy(item => item.Key, StringComparer.Ordinal);

        foreach (var group in keyedJobs)
        {
            var canonical = group
                .OrderByDescending(item => item.Job.Phase)
                .ThenByDescending(item => item.Job.Status == MoveJobStatus.Running)
                .ThenByDescending(item => item.Job.UpdatedAt ?? item.Job.EnqueuedAt)
                .First();
            canonical.Job.ActiveDeduplicationKey = group.Key;
            canonical.Job.IdentityKeyVersion = 2;

            foreach (var duplicate in group.Where(item => item.Job.Id != canonical.Job.Id))
            {
                duplicate.Job.Status = MoveJobStatus.Superseded;
                duplicate.Job.IdentityKeyVersion = 2;
                duplicate.Job.Error = $"Superseded by move job {canonical.Job.Id} during identity-key reconciliation.";
                duplicate.Job.LeaseOwner = null;
                duplicate.Job.LeaseExpiresAt = null;
            }
        }

        foreach (var unavailable in activeJobs.Where(job => string.IsNullOrWhiteSpace(job.RequestedPath)))
        {
            unavailable.Status = MoveJobStatus.NeedsAttention;
            unavailable.FailureKind = MoveFailureKind.Verification;
            unavailable.Error = "The legacy move job has no target path and cannot be reconciled safely.";
        }

        await db.SaveChangesAsync(cancellationToken);
        if (transaction != null) await transaction.CommitAsync(cancellationToken);
    }

    public async Task<MoveQueueHealthSnapshot> GetHealthAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var jobs = await db.MoveJobs
            .AsNoTracking()
            .Where(job => job.Status == MoveJobStatus.Queued
                || job.Status == MoveJobStatus.Running
                || job.Status == MoveJobStatus.RetryScheduled
                || job.Status == MoveJobStatus.NeedsAttention)
            .Select(job => new
            {
                job.Status,
                job.EnqueuedAt,
                job.AttemptCount,
                job.LeaseExpiresAt
            })
            .ToListAsync(cancellationToken);
        var queued = jobs.Where(job => job.Status is MoveJobStatus.Queued or MoveJobStatus.RetryScheduled).ToList();
        var oldestAge = queued.Count == 0
            ? 0
            : Math.Max(0, (now.UtcDateTime - queued.Min(job => job.EnqueuedAt)).TotalSeconds);
        return new MoveQueueHealthSnapshot(
            queued.Count,
            oldestAge,
            jobs.Sum(job => job.AttemptCount),
            jobs.Count(job => job.Status == MoveJobStatus.Running
                && job.LeaseExpiresAt != null
                && job.LeaseExpiresAt <= now.UtcDateTime),
            jobs.Count(job => job.Status == MoveJobStatus.NeedsAttention));
    }

    public async Task AddAsync(MoveJob job, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.MoveJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RequeueAsync(MoveJob job, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var persistedJob = await db.MoveJobs.SingleOrDefaultAsync(
            candidate => candidate.Id == job.Id,
            cancellationToken);
        if (persistedJob == null)
        {
            throw new PersistenceException(
                $"Move job {job.Id} no longer exists.",
                new InvalidOperationException("Move job not found."));
        }

        persistedJob.Status = job.Status;
        persistedJob.Phase = job.Phase;
        persistedJob.Error = job.Error;
        persistedJob.FailureKind = job.FailureKind;
        persistedJob.NextAttemptAt = job.NextAttemptAt;
        persistedJob.LeaseOwner = null;
        persistedJob.LeaseExpiresAt = null;
        persistedJob.UpdatedAt = job.UpdatedAt;
        persistedJob.ActiveDeduplicationKey = job.ActiveDeduplicationKey;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateStatusAsync(
        Guid id,
        string leaseOwner,
        int leaseGeneration,
        MoveJobStatus status,
        MoveJobPhase phase,
        string? error,
        MoveFailureKind failureKind,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (!db.Database.IsRelational())
            {
                var trackedJob = await db.MoveJobs.SingleOrDefaultAsync(
                    job => job.Id == id
                        && job.Status == MoveJobStatus.Running
                        && job.LeaseOwner == leaseOwner
                        && job.LeaseGeneration == leaseGeneration
                        && job.LeaseExpiresAt != null
                        && job.LeaseExpiresAt > updatedAt.UtcDateTime,
                    cancellationToken);
                if (trackedJob == null) return false;
                trackedJob.Status = status;
                trackedJob.Phase = phase;
                trackedJob.Error = error;
                trackedJob.FailureKind = failureKind;
                trackedJob.UpdatedAt = updatedAt.UtcDateTime;
                if (!status.IsActive())
                {
                    trackedJob.ActiveDeduplicationKey = null;
                    trackedJob.LeaseOwner = null;
                    trackedJob.LeaseExpiresAt = null;
                }
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }

            var active = status.IsActive();
            var affected = await db.MoveJobs
                .Where(job => job.Id == id
                    && job.Status == MoveJobStatus.Running
                    && job.LeaseOwner == leaseOwner
                    && job.LeaseGeneration == leaseGeneration
                    && job.LeaseExpiresAt != null
                    && job.LeaseExpiresAt > updatedAt.UtcDateTime)
                .ExecuteUpdateAsync(
                    updates => updates
                        .SetProperty(job => job.Status, status)
                        .SetProperty(job => job.Phase, phase)
                        .SetProperty(job => job.Error, error)
                        .SetProperty(job => job.FailureKind, failureKind)
                        .SetProperty(job => job.UpdatedAt, updatedAt.UtcDateTime)
                        .SetProperty(job => job.ActiveDeduplicationKey, job => active ? job.ActiveDeduplicationKey : null)
                        .SetProperty(job => job.LeaseOwner, job => active ? job.LeaseOwner : null)
                        .SetProperty(job => job.LeaseExpiresAt, job => active ? job.LeaseExpiresAt : null),
                    cancellationToken);
            return affected == 1;
        }
        catch (DbException ex)
        {
            throw new PersistenceException("Failed to update move job persistence.", ex);
        }
    }

    public async Task<int?> TryClaimAsync(
        Guid id,
        string leaseOwner,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var nowUtc = now.UtcDateTime;
        if (db.Database.IsRelational())
        {
            var affected = await db.MoveJobs
                .Where(job => job.Id == id
                    && job.ActiveDeduplicationKey != null
                    && (job.Status == MoveJobStatus.Queued
                        || job.Status == MoveJobStatus.RetryScheduled
                        || (job.Status == MoveJobStatus.Running
                            && (job.LeaseExpiresAt == null || job.LeaseExpiresAt <= nowUtc)))
                    && (job.NextAttemptAt == null || job.NextAttemptAt <= nowUtc))
                .ExecuteUpdateAsync(
                    updates => updates
                        .SetProperty(job => job.Status, MoveJobStatus.Running)
                        .SetProperty(job => job.LeaseOwner, leaseOwner)
                        .SetProperty(job => job.LeaseGeneration, job => job.LeaseGeneration + 1)
                        .SetProperty(job => job.LeaseExpiresAt, leaseExpiresAt.UtcDateTime)
                        .SetProperty(job => job.UpdatedAt, nowUtc),
                    cancellationToken);
            if (affected != 1) return null;
            return await db.MoveJobs
                .Where(job => job.Id == id && job.LeaseOwner == leaseOwner)
                .Select(job => (int?)job.LeaseGeneration)
                .SingleAsync(cancellationToken);
        }

        var job = await db.MoveJobs.SingleOrDefaultAsync(
            candidate => candidate.Id == id,
            cancellationToken);
        if (job == null
            || job.ActiveDeduplicationKey == null
            || (job.NextAttemptAt != null && job.NextAttemptAt > nowUtc)
            || (job.Status == MoveJobStatus.Running
                && job.LeaseExpiresAt != null
                && job.LeaseExpiresAt > nowUtc)
            || job.Status is not (MoveJobStatus.Queued or MoveJobStatus.RetryScheduled or MoveJobStatus.Running))
        {
            return null;
        }

        job.Status = MoveJobStatus.Running;
        job.LeaseOwner = leaseOwner;
        job.LeaseGeneration++;
        job.LeaseExpiresAt = leaseExpiresAt.UtcDateTime;
        job.UpdatedAt = nowUtc;
        await db.SaveChangesAsync(cancellationToken);
        return job.LeaseGeneration;
    }

    public async Task<bool> TryIncrementAttemptAsync(
        Guid id,
        string leaseOwner,
        int leaseGeneration,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var nowUtc = now.UtcDateTime;
            if (!db.Database.IsRelational())
            {
                var trackedJob = await db.MoveJobs.SingleOrDefaultAsync(
                    job => job.Id == id
                        && job.Status == MoveJobStatus.Running
                        && job.LeaseOwner == leaseOwner
                        && job.LeaseGeneration == leaseGeneration
                        && job.LeaseExpiresAt != null
                        && job.LeaseExpiresAt > nowUtc,
                    cancellationToken);
                if (trackedJob == null) return false;
                trackedJob.AttemptCount++;
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }

            var affected = await db.MoveJobs
                .Where(job => job.Id == id
                    && job.Status == MoveJobStatus.Running
                    && job.LeaseOwner == leaseOwner
                    && job.LeaseGeneration == leaseGeneration
                    && job.LeaseExpiresAt != null
                    && job.LeaseExpiresAt > nowUtc)
                .ExecuteUpdateAsync(
                    updates => updates.SetProperty(
                        job => job.AttemptCount,
                        job => job.AttemptCount + 1),
                    cancellationToken);
            return affected == 1;
        }
        catch (DbException ex)
        {
            throw new PersistenceException("Failed to increment move job attempt count.", ex);
        }
    }

    public async Task<bool> HeartbeatAsync(
        Guid id,
        string leaseOwner,
        int leaseGeneration,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var nowUtc = now.UtcDateTime;
        if (!db.Database.IsRelational())
        {
            var trackedJob = await db.MoveJobs.SingleOrDefaultAsync(
                candidate => candidate.Id == id
                    && candidate.Status == MoveJobStatus.Running
                    && candidate.LeaseOwner == leaseOwner
                    && candidate.LeaseGeneration == leaseGeneration
                    && candidate.LeaseExpiresAt != null
                    && candidate.LeaseExpiresAt > nowUtc,
                cancellationToken);
            if (trackedJob == null) return false;
            trackedJob.LeaseExpiresAt = leaseExpiresAt.UtcDateTime;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        var affected = await db.MoveJobs
            .Where(candidate => candidate.Id == id
                && candidate.Status == MoveJobStatus.Running
                && candidate.LeaseOwner == leaseOwner
                && candidate.LeaseGeneration == leaseGeneration
                && candidate.LeaseExpiresAt != null
                && candidate.LeaseExpiresAt > nowUtc)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(
                    job => job.LeaseExpiresAt,
                    leaseExpiresAt.UtcDateTime),
                cancellationToken);
        return affected == 1;
    }
}
