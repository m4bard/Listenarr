using Listenarr.Domain.Common;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private async Task<AudiobookContentMoveResult?> TryMoveByAtomicRenameAsync(
        AudiobookContentMoveRequest request,
        string source,
        string target,
        string tempName,
        bool targetInsideSource,
        bool sourceInsideTarget,
        string? recoveryStage,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics,
        CancellationToken cancellationToken)
    {
        if (targetInsideSource
            || sourceInsideTarget
            || faultInjector != null
            || !request.DeleteEmptySource
            || IsSourceCleanupBoundary(source, request.SourceCleanupBoundary, sourceSemantics)
            || Directory.Exists(target)
            || Directory.Exists(tempName)
            || recoveryStage != null)
        {
            return null;
        }

        await EnsureLeaseOwnedAsync(request.JobId, request.LeaseToken, cancellationToken);
        await ValidatePersistedMoveIdentityAsync(
            request.JobId,
            source,
            target,
            sourceSemantics,
            targetSemantics,
            request.LeaseToken,
            cancellationToken);
        ValidateMoveSourceRoot(source);
        ValidateMoveTargetRoot(target);
        if (Directory.Exists(target))
        {
            throw new MoveNeedsAttentionException(
                "Atomic rename target appeared after validation; no filesystem mutation was performed.");
        }

        var atomicMarkerPath = GetRecoveryMarkerPath(source, request.JobId);
        WriteRecoveryMarker(
            source,
            request.JobId,
            source,
            target,
            AtomicRenameCompletedStage);
        try
        {
            // Recheck both roots after publishing the durable marker and immediately
            // before the rename so a linked or newly occupied target is never followed.
            ValidateMoveSourceRoot(source);
            ValidateMoveTargetRoot(target);
            if (Directory.Exists(target))
            {
                throw new MoveNeedsAttentionException(
                    "Atomic rename target appeared before publication; no directory was moved.");
            }

            Directory.Move(source, target);
        }
        catch (MoveNeedsAttentionException)
        {
            DeleteFailedAtomicMarker(atomicMarkerPath, null);
            throw;
        }
        catch (IOException exception)
        {
            DeleteFailedAtomicMarker(atomicMarkerPath, exception);
            ValidateMoveTargetRoot(target);
            if (!Directory.Exists(source) || Directory.Exists(target))
            {
                throw new MoveNeedsAttentionException(
                    "Atomic rename failed with an ambiguous source or target state; copy fallback was blocked.");
            }

            return null;
        }

        await UpdateJobPhaseAsync(
            request.JobId,
            request.LeaseToken,
            MoveJobPhase.Finalizing,
            cancellationToken);
        return new AudiobookContentMoveResult(
            source,
            target,
            false,
            false,
            GetRecoveryMarkerPath(target, request.JobId),
            SourceCleanupCompleted: true);
    }

    private static void DeleteFailedAtomicMarker(
        string atomicMarkerPath,
        Exception? renameException)
    {
        try
        {
            if (File.Exists(atomicMarkerPath))
            {
                File.Delete(atomicMarkerPath);
            }
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            throw new MoveNeedsAttentionException(
                $"Atomic rename failed and its recovery marker could not be removed. "
                + $"Rename error: {renameException?.Message ?? "precondition changed"}. "
                + $"Marker cleanup error: {exception.Message}");
        }
    }
}
