using Listenarr.Domain.Common;
using Listenarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private async Task EnsureLeaseOwnedAsync(
        Guid jobId,
        MoveLeaseToken leaseToken,
        CancellationToken cancellationToken)
    {
        EnsureLeaseTokenProvided(jobId, leaseToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.MoveJobs.AnyAsync(
            job => job.Id == jobId
                && job.Status == MoveJobStatus.Running
                && job.LeaseOwner == leaseToken.Owner
                && job.LeaseGeneration == leaseToken.Generation
                && job.LeaseExpiresAt != null
                && job.LeaseExpiresAt > nowUtc,
            cancellationToken))
        {
            throw new MoveLeaseLostException(jobId, leaseToken.Generation);
        }
    }

    private async Task ValidatePersistedMoveIdentityAsync(
        Guid jobId,
        string source,
        string target,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics,
        MoveLeaseToken leaseToken,
        CancellationToken cancellationToken)
    {
        EnsureLeaseTokenProvided(jobId, leaseToken);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var identity = await db.MoveJobs
            .AsNoTracking()
            .Where(job => job.Id == jobId)
            .Select(job => new { job.SourcePath, job.RequestedPath })
            .SingleOrDefaultAsync(cancellationToken);
        if (identity == null || string.IsNullOrWhiteSpace(identity.RequestedPath))
        {
            throw new MoveNeedsAttentionException(
                "Persisted move target identity is required before filesystem recovery.");
        }

        try
        {
            if (!FileSystemPathIdentity.AreEquivalent(identity.RequestedPath, target, targetSemantics))
            {
                throw new MoveNeedsAttentionException(
                    "Persisted move target identity does not match the requested filesystem operation.");
            }
        }
        catch (ArgumentException)
        {
            throw new MoveNeedsAttentionException("Persisted move target identity is invalid.");
        }

        var persistedSource = identity.SourcePath;
        if (string.IsNullOrWhiteSpace(persistedSource))
        {
            var hasRecoveryArtifacts = await db.MoveJobEntries.AnyAsync(
                    entry => entry.MoveJobId == jobId,
                    cancellationToken)
                || File.Exists(GetRecoveryMarkerPath(target, jobId))
                || File.Exists(GetRecoveryMarkerPath(source, jobId));
            if (hasRecoveryArtifacts)
            {
                throw new MoveNeedsAttentionException(
                    "A legacy move without a persisted source cannot own existing recovery artifacts.");
            }

            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            if (!db.Database.IsRelational())
            {
                var job = await db.MoveJobs.SingleOrDefaultAsync(
                    candidate => candidate.Id == jobId
                        && candidate.Status == MoveJobStatus.Running
                        && candidate.LeaseOwner == leaseToken.Owner
                        && candidate.LeaseGeneration == leaseToken.Generation
                        && candidate.LeaseExpiresAt != null
                        && candidate.LeaseExpiresAt > nowUtc,
                    cancellationToken);
                if (job == null || !string.IsNullOrWhiteSpace(job.SourcePath))
                {
                    throw new MoveLeaseLostException(jobId, leaseToken.Generation);
                }

                job.SourcePath = source;
                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                var affected = await db.MoveJobs
                    .Where(candidate => candidate.Id == jobId
                        && candidate.SourcePath == identity.SourcePath
                        && candidate.Status == MoveJobStatus.Running
                        && candidate.LeaseOwner == leaseToken.Owner
                        && candidate.LeaseGeneration == leaseToken.Generation
                        && candidate.LeaseExpiresAt != null
                        && candidate.LeaseExpiresAt > nowUtc)
                    .ExecuteUpdateAsync(
                        updates => updates.SetProperty(job => job.SourcePath, source),
                        cancellationToken);
                if (affected != 1)
                {
                    throw new MoveLeaseLostException(jobId, leaseToken.Generation);
                }
            }

            persistedSource = source;
        }

        try
        {
            if (!FileSystemPathIdentity.AreEquivalent(persistedSource, source, sourceSemantics))
            {
                throw new MoveNeedsAttentionException(
                    "Persisted move source identity does not match the requested filesystem operation.");
            }
        }
        catch (ArgumentException)
        {
            throw new MoveNeedsAttentionException("Persisted move source identity is invalid.");
        }
    }

    private async Task PersistManifestAsync(
        Guid jobId,
        MoveLeaseToken leaseToken,
        IReadOnlyCollection<MoveJobEntry> manifest,
        CancellationToken cancellationToken)
    {
        EnsureLeaseTokenProvided(jobId, leaseToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!db.Database.IsRelational())
        {
            if (!await IsLeaseActiveAsync(db, jobId, leaseToken, nowUtc, cancellationToken))
            {
                throw new MoveLeaseLostException(jobId, leaseToken.Generation);
            }

            db.MoveJobEntries.AddRange(manifest);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await IsLeaseActiveAsync(db, jobId, leaseToken, nowUtc, cancellationToken))
        {
            throw new MoveLeaseLostException(jobId, leaseToken.Generation);
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
        var entries = await db.MoveJobEntries
            .AsNoTracking()
            .Where(entry => entry.MoveJobId == jobId)
            .OrderBy(entry => entry.Id)
            .ToListAsync(cancellationToken);
        if (entries.Count > 0)
        {
            return entries;
        }

        var requestedPath = await db.MoveJobs
            .AsNoTracking()
            .Where(job => job.Id == jobId)
            .Select(job => job.RequestedPath)
            .SingleOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            var target = Path.GetFullPath(requestedPath);
            var markerPath = GetRecoveryMarkerPath(target, jobId);
            if (File.Exists(markerPath))
            {
                throw new MoveNeedsAttentionException(
                    "A move recovery marker exists without a persisted manifest; destination ownership cannot be proven.");
            }
        }

        return entries;
    }

    private async Task UpdateCleanupStateAsync(
        Guid jobId,
        MoveLeaseToken leaseToken,
        string relativePath,
        MoveJobEntryCleanupState cleanupState,
        CancellationToken cancellationToken)
    {
        EnsureLeaseTokenProvided(jobId, leaseToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!db.Database.IsRelational())
        {
            var entry = await db.MoveJobEntries.SingleOrDefaultAsync(
                candidate => candidate.MoveJobId == jobId
                    && candidate.RelativePath == relativePath
                    && candidate.MoveJob.Status == MoveJobStatus.Running
                    && candidate.MoveJob.LeaseOwner == leaseToken.Owner
                    && candidate.MoveJob.LeaseGeneration == leaseToken.Generation
                    && candidate.MoveJob.LeaseExpiresAt != null
                    && candidate.MoveJob.LeaseExpiresAt > nowUtc,
                cancellationToken);
            if (entry == null) throw new MoveLeaseLostException(jobId, leaseToken.Generation);
            entry.CleanupState = cleanupState;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var affected = await db.MoveJobEntries
            .Where(entry => entry.MoveJobId == jobId
                && entry.RelativePath == relativePath
                && entry.MoveJob.Status == MoveJobStatus.Running
                && entry.MoveJob.LeaseOwner == leaseToken.Owner
                && entry.MoveJob.LeaseGeneration == leaseToken.Generation
                && entry.MoveJob.LeaseExpiresAt != null
                && entry.MoveJob.LeaseExpiresAt > nowUtc)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(entry => entry.CleanupState, cleanupState),
                cancellationToken);
        if (affected != 1)
        {
            throw new MoveLeaseLostException(jobId, leaseToken.Generation);
        }
    }

    private async Task UpdateCopyStateAsync(
        Guid jobId,
        MoveLeaseToken leaseToken,
        CancellationToken cancellationToken)
    {
        EnsureLeaseTokenProvided(jobId, leaseToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!db.Database.IsRelational())
        {
            if (!await IsLeaseActiveAsync(db, jobId, leaseToken, nowUtc, cancellationToken))
            {
                throw new MoveLeaseLostException(jobId, leaseToken.Generation);
            }

            var persistedEntries = await db.MoveJobEntries
                .Where(entry => entry.MoveJobId == jobId
                    && entry.MoveJob.Status == MoveJobStatus.Running
                    && entry.MoveJob.LeaseOwner == leaseToken.Owner
                    && entry.MoveJob.LeaseGeneration == leaseToken.Generation
                    && entry.MoveJob.LeaseExpiresAt != null
                    && entry.MoveJob.LeaseExpiresAt > nowUtc)
                .ToListAsync(cancellationToken);
            var expectedCount = await db.MoveJobEntries.CountAsync(
                entry => entry.MoveJobId == jobId,
                cancellationToken);
            if (persistedEntries.Count != expectedCount)
            {
                throw new MoveLeaseLostException(jobId, leaseToken.Generation);
            }

            foreach (var entry in persistedEntries)
            {
                entry.CopyState = MoveJobEntryCopyState.Verified;
            }

            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (!await IsLeaseActiveAsync(db, jobId, leaseToken, nowUtc, cancellationToken))
        {
            throw new MoveLeaseLostException(jobId, leaseToken.Generation);
        }

        var affected = await db.MoveJobEntries
            .Where(entry => entry.MoveJobId == jobId
                && entry.MoveJob.Status == MoveJobStatus.Running
                && entry.MoveJob.LeaseOwner == leaseToken.Owner
                && entry.MoveJob.LeaseGeneration == leaseToken.Generation
                && entry.MoveJob.LeaseExpiresAt != null
                && entry.MoveJob.LeaseExpiresAt > nowUtc)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(
                    entry => entry.CopyState,
                    MoveJobEntryCopyState.Verified),
                cancellationToken);
        var expected = await db.MoveJobEntries.CountAsync(
            entry => entry.MoveJobId == jobId,
            cancellationToken);
        if (affected != expected)
        {
            throw new MoveLeaseLostException(jobId, leaseToken.Generation);
        }
    }

    private async Task UpdateJobPhaseAsync(
        Guid jobId,
        MoveLeaseToken leaseToken,
        MoveJobPhase phase,
        CancellationToken cancellationToken)
    {
        EnsureLeaseTokenProvided(jobId, leaseToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!db.Database.IsRelational())
        {
            var job = await db.MoveJobs.SingleOrDefaultAsync(
                candidate => candidate.Id == jobId
                    && candidate.Status == MoveJobStatus.Running
                    && candidate.LeaseOwner == leaseToken.Owner
                    && candidate.LeaseGeneration == leaseToken.Generation
                    && candidate.LeaseExpiresAt != null
                    && candidate.LeaseExpiresAt > nowUtc,
                cancellationToken);
            if (job == null) throw new MoveLeaseLostException(jobId, leaseToken.Generation);
            job.Phase = phase;
            job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var affected = await db.MoveJobs
            .Where(candidate => candidate.Id == jobId
                && candidate.Status == MoveJobStatus.Running
                && candidate.LeaseOwner == leaseToken.Owner
                && candidate.LeaseGeneration == leaseToken.Generation
                && candidate.LeaseExpiresAt != null
                && candidate.LeaseExpiresAt > nowUtc)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(job => job.Phase, phase)
                    .SetProperty(job => job.UpdatedAt, DateTime.UtcNow),
                cancellationToken);
        if (affected != 1)
        {
            throw new MoveLeaseLostException(jobId, leaseToken.Generation);
        }
    }

    private static async Task<bool> IsLeaseActiveAsync(
        ListenArrDbContext db,
        Guid jobId,
        MoveLeaseToken leaseToken,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        return await db.MoveJobs.AnyAsync(
            job => job.Id == jobId
                && job.Status == MoveJobStatus.Running
                && job.LeaseOwner == leaseToken.Owner
                && job.LeaseGeneration == leaseToken.Generation
                && job.LeaseExpiresAt != null
                && job.LeaseExpiresAt > nowUtc,
            cancellationToken);
    }

    private static void EnsureLeaseTokenProvided(Guid jobId, MoveLeaseToken leaseToken)
    {
        if (string.IsNullOrWhiteSpace(leaseToken.Owner) || leaseToken.Generation <= 0)
        {
            // Filesystem mutations and their manifest/phase writes must always be tied
            // to a claimed worker lease; missing owner/generation values are unclaimed DTO defaults.
            throw new MoveLeaseLostException(jobId, leaseToken.Generation);
        }
    }
}
