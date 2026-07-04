using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private async Task EnsureLeaseOwnedAsync(
        Guid jobId,
        int leaseGeneration,
        CancellationToken cancellationToken)
    {
        if (leaseGeneration == 0)
        {
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.MoveJobs.AnyAsync(
            job => job.Id == jobId && job.LeaseGeneration == leaseGeneration,
            cancellationToken))
        {
            throw new MoveLeaseLostException(jobId, leaseGeneration);
        }
    }

    private async Task PersistManifestAsync(
        Guid jobId,
        int leaseGeneration,
        IReadOnlyCollection<MoveJobEntry> manifest,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (leaseGeneration == 0)
        {
            if (!await db.MoveJobs.AnyAsync(job => job.Id == jobId, cancellationToken))
            {
                return;
            }

            db.MoveJobEntries.AddRange(manifest);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (!db.Database.IsRelational())
        {
            if (!await db.MoveJobs.AnyAsync(
                job => job.Id == jobId && job.LeaseGeneration == leaseGeneration,
                cancellationToken))
            {
                throw new MoveLeaseLostException(jobId, leaseGeneration);
            }

            db.MoveJobEntries.AddRange(manifest);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await db.MoveJobs.AnyAsync(
            job => job.Id == jobId && job.LeaseGeneration == leaseGeneration,
            cancellationToken))
        {
            throw new MoveLeaseLostException(jobId, leaseGeneration);
        }

        db.MoveJobEntries.AddRange(manifest);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private List<MoveJobEntry> LoadManifest(Guid jobId)
    {
        using var db = dbContextFactory.CreateDbContext();
        return db.MoveJobEntries
            .AsNoTracking()
            .Where(entry => entry.MoveJobId == jobId)
            .OrderBy(entry => entry.Id)
            .ToList();
    }

    private async Task<List<MoveJobEntry>> LoadManifestAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.MoveJobEntries
            .AsNoTracking()
            .Where(entry => entry.MoveJobId == jobId)
            .OrderBy(entry => entry.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task UpdateCleanupStateAsync(
        Guid jobId,
        int leaseGeneration,
        string relativePath,
        MoveJobEntryCleanupState cleanupState,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (leaseGeneration == 0)
        {
            var entry = await db.MoveJobEntries.SingleOrDefaultAsync(
                candidate => candidate.MoveJobId == jobId
                    && candidate.RelativePath == relativePath,
                cancellationToken);
            if (entry == null) return;
            entry.CleanupState = cleanupState;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (!db.Database.IsRelational())
        {
            var entry = await db.MoveJobEntries.SingleOrDefaultAsync(
                candidate => candidate.MoveJobId == jobId
                    && candidate.RelativePath == relativePath
                    && candidate.MoveJob.LeaseGeneration == leaseGeneration,
                cancellationToken);
            if (entry == null) throw new MoveLeaseLostException(jobId, leaseGeneration);
            entry.CleanupState = cleanupState;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var entries = db.MoveJobEntries
            .Where(entry => entry.MoveJobId == jobId
                && entry.RelativePath == relativePath);
        if (leaseGeneration != 0)
        {
            entries = entries.Where(entry => entry.MoveJob.LeaseGeneration == leaseGeneration);
        }

        var affected = await entries
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(entry => entry.CleanupState, cleanupState),
                cancellationToken);
        if (affected != 1 && leaseGeneration != 0)
        {
            throw new MoveLeaseLostException(jobId, leaseGeneration);
        }
    }

    private async Task UpdateCopyStateAsync(
        Guid jobId,
        int leaseGeneration,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (leaseGeneration == 0)
        {
            var persistedEntries = await db.MoveJobEntries
                .Where(entry => entry.MoveJobId == jobId)
                .ToListAsync(cancellationToken);
            foreach (var entry in persistedEntries)
            {
                entry.CopyState = MoveJobEntryCopyState.Verified;
            }

            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (!db.Database.IsRelational())
        {
            if (!await db.MoveJobs.AnyAsync(
                job => job.Id == jobId && job.LeaseGeneration == leaseGeneration,
                cancellationToken))
            {
                throw new MoveLeaseLostException(jobId, leaseGeneration);
            }

            var persistedEntries = await db.MoveJobEntries
                .Where(entry => entry.MoveJobId == jobId
                    && entry.MoveJob.LeaseGeneration == leaseGeneration)
                .ToListAsync(cancellationToken);
            var expectedCount = await db.MoveJobEntries.CountAsync(
                entry => entry.MoveJobId == jobId,
                cancellationToken);
            if (persistedEntries.Count != expectedCount)
            {
                throw new MoveLeaseLostException(jobId, leaseGeneration);
            }
            foreach (var entry in persistedEntries)
            {
                entry.CopyState = MoveJobEntryCopyState.Verified;
            }
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var entries = db.MoveJobEntries.Where(entry => entry.MoveJobId == jobId);
        if (leaseGeneration != 0)
        {
            entries = entries.Where(entry => entry.MoveJob.LeaseGeneration == leaseGeneration);
        }

        var affected = await entries
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(
                    entry => entry.CopyState,
                    MoveJobEntryCopyState.Verified),
                cancellationToken);
        var expected = await db.MoveJobEntries.CountAsync(
            entry => entry.MoveJobId == jobId,
            cancellationToken);
        if (affected != expected && leaseGeneration != 0)
        {
            throw new MoveLeaseLostException(jobId, leaseGeneration);
        }
    }

    private async Task UpdateJobPhaseAsync(
        Guid jobId,
        int leaseGeneration,
        MoveJobPhase phase,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (leaseGeneration == 0)
        {
            var job = await db.MoveJobs.SingleOrDefaultAsync(
                candidate => candidate.Id == jobId,
                cancellationToken);
            if (job == null) return;
            job.Phase = phase;
            job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (!db.Database.IsRelational())
        {
            var job = await db.MoveJobs.SingleOrDefaultAsync(
                candidate => candidate.Id == jobId
                    && candidate.LeaseGeneration == leaseGeneration,
                cancellationToken);
            if (job == null) throw new MoveLeaseLostException(jobId, leaseGeneration);
            job.Phase = phase;
            job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var jobs = db.MoveJobs.Where(candidate => candidate.Id == jobId);
        if (leaseGeneration != 0)
        {
            jobs = jobs.Where(candidate => candidate.LeaseGeneration == leaseGeneration);
        }

        var affected = await jobs
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(job => job.Phase, phase)
                    .SetProperty(job => job.UpdatedAt, DateTime.UtcNow),
                cancellationToken);
        if (affected != 1 && leaseGeneration != 0)
        {
            throw new MoveLeaseLostException(jobId, leaseGeneration);
        }
    }
}
