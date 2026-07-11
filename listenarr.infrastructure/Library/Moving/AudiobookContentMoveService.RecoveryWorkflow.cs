using Listenarr.Domain.Common;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    public async Task<AudiobookContentMoveResult?> GetRecoverableMoveAsync(
        AudiobookContentMoveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var source = Path.GetFullPath(request.Source);
        var target = Path.GetFullPath(request.Target);
        var recoveryMarkerPath = GetRecoveryMarkerPath(target, request.JobId);
        var sourceSemantics = request.SourceSemantics;
        var targetSemantics = request.TargetSemantics;

        await EnsureLeaseOwnedAsync(request.JobId, request.LeaseToken, cancellationToken);
        await ValidatePersistedMoveIdentityAsync(
            request.JobId,
            source,
            target,
            sourceSemantics,
            targetSemantics,
            request.LeaseToken,
            cancellationToken);

        var recoveryMarker = ReadRecoveryMarker(recoveryMarkerPath);
        if (recoveryMarker == null)
        {
            return null;
        }

        ValidateRecoveryMarker(recoveryMarker, request, source, target);
        ValidateRecoveryMarkerLocation(recoveryMarkerPath, target, targetSemantics);

        if (IsFilesystemRoot(source, sourceSemantics)
            || IsFilesystemRoot(target, targetSemantics)
            || FileSystemPathIdentity.AreEquivalent(source, target, sourceSemantics))
        {
            throw new MoveNeedsAttentionException(
                "Move recovery artifacts reference a filesystem root or identical source and target.");
        }

        if (!Directory.Exists(target))
        {
            return null;
        }

        if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
        {
            throw new MoveNeedsAttentionException(
                "Move recovery target is a symbolic link or reparse point.");
        }

        var manifest = LoadManifest(request.JobId);
        var recoveryStage = recoveryMarker.Stage;
        if (string.Equals(recoveryStage, AtomicRenameCompletedStage, StringComparison.Ordinal))
        {
            if (manifest.Count > 0)
            {
                throw new MoveNeedsAttentionException(
                    "An atomic rename marker cannot be combined with a persisted copy manifest.");
            }

            if (Directory.Exists(source))
            {
                throw new MoveNeedsAttentionException(
                    "Both source and target exist for an atomic rename marker; completion cannot be proven and no files were changed.");
            }

            return new AudiobookContentMoveResult(
                source,
                target,
                TargetInsideSource: false,
                SourceInsideTarget: false,
                recoveryMarkerPath,
                SourceCleanupCompleted: true);
        }

        if (manifest.Count == 0)
        {
            throw new MoveNeedsAttentionException(
                "A move recovery marker exists without a persisted manifest; destination ownership cannot be proven.");
        }

        if (recoveryStage == CopyStartedStage)
        {
            return null;
        }

        if (recoveryStage is not (CopyCompletedStage or SourceCleanupCompletedStage))
        {
            throw new MoveNeedsAttentionException("The move recovery stage is not recoverable.");
        }

        var tempOwnership = TryValidatePublishedTempOwnership(target, request, source, target);
        var quarantineOwnership = TryValidateExistingQuarantineDirectory(
            source,
            target,
            request.JobId,
            sourceSemantics,
            targetSemantics);
        ValidateExistingDestinationContents(
            source,
            target,
            manifest,
            request.JobId,
            targetSemantics,
            tempOwnership,
            quarantineOwnership,
            allowPartialFiles: false);
        await VerifyPublishedManifestAsync(
            target,
            manifest,
            targetSemantics,
            cancellationToken);

        var targetInsideSource = IsSameOrInside(target, source, sourceSemantics);
        var sourceInsideTarget = IsSameOrInside(source, target, targetSemantics);
        var sourceCleanupCompleted = string.Equals(
            recoveryStage,
            SourceCleanupCompletedStage,
            StringComparison.Ordinal);
        if (sourceCleanupCompleted)
        {
            VerifySourceCleanupState(request, source, target);
        }

        return new AudiobookContentMoveResult(
            source,
            target,
            targetInsideSource,
            sourceInsideTarget,
            recoveryMarkerPath,
            sourceCleanupCompleted);
    }

    public async Task<AudiobookContentMoveResult> ResumeSourceCleanupAsync(
        AudiobookContentMoveRequest request,
        AudiobookContentMoveResult result,
        CancellationToken cancellationToken)
    {
        if (result.SourceCleanupCompleted)
        {
            return result;
        }

        var manifest = LoadManifest(request.JobId);
        if (manifest.Count == 0)
        {
            throw new MoveNeedsAttentionException(
                "Source cleanup is blocked because no persisted move manifest is available.");
        }

        await EnsureLeaseOwnedAsync(request.JobId, request.LeaseToken, cancellationToken);

        await DeleteOriginalSourceAsync(
            result.Source,
            result.Target,
            result.TargetInsideSource,
            request.DeleteEmptySource,
            request.JobId,
            request.LeaseToken,
            manifest,
            request.SourceSemantics,
            request.TargetSemantics,
            request.SourceCleanupBoundary,
            cancellationToken);
        VerifySourceCleanupState(request, result.Source, result.Target);
        WriteRecoveryMarker(
            result.Target,
            request,
            result.Source,
            result.Target,
            SourceCleanupCompletedStage);
        return result with { SourceCleanupCompleted = true };
    }
}
