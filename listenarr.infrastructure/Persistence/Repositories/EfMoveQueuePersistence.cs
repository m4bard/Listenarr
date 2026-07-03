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
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Persistence.Repositories;

public sealed class EfMoveQueuePersistence(IDbContextFactory<ListenArrDbContext> dbFactory)
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
                    && (job.Status == "Queued" || job.Status == "Processing"))
                .OrderBy(job => job.EnqueuedAt)
                .ToListAsync(cancellationToken);
        }
        catch (DbException ex)
        {
            throw new PersistenceException("Failed to query active move job persistence.", ex);
        }
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
        persistedJob.Error = job.Error;
        persistedJob.UpdatedAt = job.UpdatedAt;
        persistedJob.ActiveDeduplicationKey = job.ActiveDeduplicationKey;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateStatusAsync(
        Guid id,
        string status,
        string? error,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var job = await db.MoveJobs.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            if (job == null)
            {
                return;
            }

            job.Status = status;
            job.Error = error;
            job.UpdatedAt = updatedAt.UtcDateTime;
            job.ActiveDeduplicationKey = IsActive(status) ? job.ActiveDeduplicationKey : null;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbException ex)
        {
            throw new PersistenceException("Failed to update move job persistence.", ex);
        }
    }

    private static bool IsActive(string status) =>
        string.Equals(status, "Queued", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "Processing", StringComparison.OrdinalIgnoreCase);
}
