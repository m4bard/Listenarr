using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private async Task PersistManifestAsync(
        Guid jobId,
        IReadOnlyCollection<MoveJobEntry> manifest,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.MoveJobs.AnyAsync(job => job.Id == jobId, cancellationToken))
        {
            return;
        }

        db.MoveJobEntries.AddRange(manifest);
        await db.SaveChangesAsync(cancellationToken);
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
        string relativePath,
        MoveJobEntryCleanupState cleanupState,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var persistedEntry = await db.MoveJobEntries.SingleOrDefaultAsync(
            entry => entry.MoveJobId == jobId && entry.RelativePath == relativePath,
            cancellationToken);
        if (persistedEntry == null)
        {
            return;
        }

        persistedEntry.CleanupState = cleanupState;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task UpdateCopyStateAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entries = await db.MoveJobEntries
            .Where(entry => entry.MoveJobId == jobId)
            .ToListAsync(cancellationToken);
        foreach (var entry in entries)
        {
            entry.CopyState = MoveJobEntryCopyState.Verified;
        }

        if (entries.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task UpdateJobPhaseAsync(
        Guid jobId,
        MoveJobPhase phase,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var job = await db.MoveJobs.SingleOrDefaultAsync(
            candidate => candidate.Id == jobId,
            cancellationToken);
        if (job == null)
        {
            return;
        }

        job.Phase = phase;
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
