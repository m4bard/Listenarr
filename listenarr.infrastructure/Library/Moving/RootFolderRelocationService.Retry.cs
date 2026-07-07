using Listenarr.Application.Common.Exceptions;
using Listenarr.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Library.Moving;

public sealed partial class RootFolderRelocationService
{
    public async Task<RootFolderPathChangeResult> RetryAsync(
        Guid relocationId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var relocation = await db.RootFolderRelocations
            .Include(candidate => candidate.MoveJobs)
            .Include(candidate => candidate.SkippedItems)
            .SingleOrDefaultAsync(candidate => candidate.Id == relocationId, cancellationToken)
            ?? throw new KeyNotFoundException("Root folder relocation not found");
        if (relocation.Status != RootFolderRelocationStatus.NeedsAttention)
        {
            throw new InvalidOperationException("Only relocations needing attention can be retried.");
        }

        var targetResolution = await semanticsResolver.ResolveAsync(
            relocation.TargetPath,
            relocation.TargetCaseSensitivityMode,
            cancellationToken);
        if (targetResolution.State != PathIdentityState.Valid)
        {
            throw new InvalidOperationException(
                targetResolution.Reason ?? "Target filesystem identity is unavailable.");
        }

        var skippedSupersededJobs = 0;
        foreach (var job in relocation.MoveJobs.Where(job => job.Status is
            MoveJobStatus.NeedsAttention or MoveJobStatus.Failed or MoveJobStatus.Superseded))
        {
            var deduplicationKey = FileSystemPathIdentity.CreateKey(
                $"move:{job.AudiobookId}",
                job.RequestedPath!,
                targetResolution.Semantics);
            var conflictingJob = await db.MoveJobs.AsNoTracking().FirstOrDefaultAsync(
                candidate => candidate.Id != job.Id
                    && candidate.ActiveDeduplicationKey == deduplicationKey,
                cancellationToken);
            if (conflictingJob != null)
            {
                if (job.Status == MoveJobStatus.Superseded)
                {
                    skippedSupersededJobs++;
                    continue;
                }

                throw new ApplicationConflictException(
                    "move_job_retry_conflict",
                    "A newer move for this audiobook is already active.");
            }

            job.Status = MoveJobStatus.Queued;
            job.Error = null;
            job.FailureKind = MoveFailureKind.None;
            job.NextAttemptAt = null;
            job.ActiveDeduplicationKey = deduplicationKey;
        }

        if (relocation.SkippedItems.Count > 0)
        {
            await RetrySkippedMetadataReferencesAsync(
                db,
                relocation,
                targetResolution.Semantics,
                cancellationToken);
        }

        var remainingSkippedItems = relocation.SkippedItems.Count;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (remainingSkippedItems > 0 || skippedSupersededJobs > 0)
        {
            relocation.Status = RootFolderRelocationStatus.NeedsAttention;
            relocation.Error = BuildRetryAttentionError(remainingSkippedItems, skippedSupersededJobs);
        }
        else if (relocation.MoveJobs.Count == 0
            || relocation.MoveJobs.All(job => job.Status == MoveJobStatus.Completed))
        {
            relocation.Status = RootFolderRelocationStatus.Completed;
            relocation.ActiveRootFolderId = null;
            relocation.CompletedJobs = relocation.TotalJobs;
            relocation.CompletedAt = now;
            relocation.Error = null;
        }
        else
        {
            relocation.Status = RootFolderRelocationStatus.Running;
            relocation.Error = null;
        }

        relocation.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var rootPath = await db.RootFolders
            .Where(root => root.Id == relocation.RootFolderId)
            .Select(root => root.Path)
            .SingleAsync(cancellationToken);
        var result = Map(relocation, rootPath);
        await BroadcastAsync(result, cancellationToken);
        return result;
    }
}
