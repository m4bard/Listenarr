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
            request,
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
            DeleteFailedAtomicMarker(atomicMarkerPath, source, null);
            throw;
        }
        catch (IOException exception)
        {
            DeleteFailedAtomicMarker(atomicMarkerPath, source, exception);
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
        string source,
        Exception? renameException)
    {
        try
        {
            if (File.Exists(atomicMarkerPath))
            {
                ValidateMoveSourceRoot(source);
                if (!FileSystemSafety.TryValidateMutationTarget(
                        atomicMarkerPath,
                        [source],
                        out atomicMarkerPath,
                        out var markerReason))
                {
                    throw new MoveNeedsAttentionException(markerReason);
                }

                if ((File.GetAttributes(atomicMarkerPath) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new MoveNeedsAttentionException(
                        "The failed atomic recovery marker became a symbolic link or reparse point.");
                }

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
