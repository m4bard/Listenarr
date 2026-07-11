namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    public async Task VerifyFinalizedMoveAsync(
        AudiobookContentMoveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var source = Path.GetFullPath(request.Source);
        var target = Path.GetFullPath(request.Target);
        await EnsureLeaseOwnedAsync(request.JobId, request.LeaseToken, cancellationToken);
        await ValidatePersistedMoveIdentityAsync(
            request.JobId,
            source,
            target,
            request.SourceSemantics,
            request.TargetSemantics,
            request.LeaseToken,
            cancellationToken);

        ValidateMoveRootPath(source, mustExist: false, "source recovery");
        ValidateMoveTargetRoot(target);
        if (!Directory.Exists(target))
        {
            throw new MoveNeedsAttentionException(
                "The finalized move target no longer exists.");
        }

        var manifest = await LoadManifestAsync(request.JobId, cancellationToken);
        if (manifest.Count == 0)
        {
            var phase = await LoadJobPhaseAsync(request.JobId, cancellationToken);
            if (phase < MoveJobPhase.CleaningArtifacts)
            {
                throw new MoveNeedsAttentionException(
                    "A markerless atomic move cannot be verified before durable artifact cleanup begins.");
            }

            if (!FileSystemSafety.TryEnumerateTreeWithoutLinks(
                    target,
                    out _,
                    out _,
                    out var reason))
            {
                throw new MoveNeedsAttentionException(
                    $"The finalized atomic target could not be verified safely: {reason}");
            }

            VerifySourceCleanupState(request, source, target);
            return;
        }

        ValidateTargetManifest(target, manifest, request.TargetSemantics);
        var tempOwnership = await TryValidatePublishedTempOwnershipAsync(
            target,
            request,
            source,
            target,
            cancellationToken);
        var quarantineOwnership = await TryValidateExistingQuarantineDirectoryAsync(
            source,
            target,
            request.JobId,
            request.SourceSemantics,
            request.TargetSemantics,
            request.LeaseToken,
            cancellationToken);
        ValidateExistingDestinationContents(
            source,
            target,
            manifest,
            request.JobId,
            request.TargetSemantics,
            tempOwnership,
            quarantineOwnership,
            allowPartialFiles: false);
        await VerifyPublishedManifestAsync(
            target,
            manifest,
            request.TargetSemantics,
            cancellationToken);

        VerifySourceCleanupState(request, source, target);
    }
}
