using Listenarr.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private Task EnsureMutationAuthorizedAsync(
        AudiobookContentMoveRequest request,
        string source,
        string target,
        CancellationToken cancellationToken) =>
        EnsureMutationAuthorizedAsync(
            request.JobId,
            request.LeaseToken,
            source,
            target,
            request.SourceSemantics,
            request.TargetSemantics,
            cancellationToken);

    private async Task EnsureMutationAuthorizedAsync(
        Guid jobId,
        MoveLeaseToken leaseToken,
        string source,
        string target,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics,
        CancellationToken cancellationToken)
    {
        EnsureLeaseTokenProvided(jobId, leaseToken);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var state = await db.MoveJobs
            .AsNoTracking()
            .Where(job => job.Id == jobId
                && job.Status == MoveJobStatus.Running
                && job.LeaseOwner == leaseToken.Owner
                && job.LeaseGeneration == leaseToken.Generation
                && job.LeaseExpiresAt != null
                && job.LeaseExpiresAt > nowUtc)
            .Select(job => new { job.SourcePath, job.RequestedPath })
            .SingleOrDefaultAsync(cancellationToken);
        if (state == null)
        {
            throw new MoveLeaseLostException(jobId, leaseToken.Generation);
        }

        if (string.IsNullOrWhiteSpace(state.SourcePath)
            || string.IsNullOrWhiteSpace(state.RequestedPath))
        {
            throw new MoveNeedsAttentionException(
                "Persisted source and target identities are required before a filesystem mutation.");
        }

        try
        {
            if (!FileSystemPathIdentity.AreEquivalent(state.SourcePath, source, sourceSemantics)
                || !FileSystemPathIdentity.AreEquivalent(state.RequestedPath, target, targetSemantics))
            {
                throw new MoveNeedsAttentionException(
                    "Persisted move identity changed before a filesystem mutation.");
            }
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException or NotSupportedException or PathTooLongException)
        {
            throw new MoveNeedsAttentionException(
                "Persisted move identity became invalid before a filesystem mutation.");
        }
    }
}
