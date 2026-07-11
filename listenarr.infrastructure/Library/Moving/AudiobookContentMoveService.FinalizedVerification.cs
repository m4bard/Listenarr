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
            throw new MoveNeedsAttentionException(
                "A markerless atomic move cannot be verified from persisted filesystem evidence.");
        }

        ValidateTargetManifest(target, manifest, request.TargetSemantics);
        var tempOwnership = TryValidatePublishedTempOwnership(
            target,
            request,
            source,
            target);
        ValidateExistingDestinationContents(
            source,
            target,
            manifest,
            request.JobId,
            request.TargetSemantics,
            tempOwnership,
            allowPartialFiles: false);
        await VerifyPublishedManifestAsync(
            target,
            manifest,
            request.TargetSemantics,
            cancellationToken);

        if (!IsSourceCleanupComplete(source, target, request.TargetSemantics))
        {
            throw new MoveNeedsAttentionException(
                "The finalized move source cleanup is incomplete or cannot be verified safely.");
        }
    }
}
