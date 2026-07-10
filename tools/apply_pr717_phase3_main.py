from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
PATH = ROOT / "listenarr.infrastructure/Library/Moving/AudiobookContentMoveService.cs"


def read() -> str:
    return PATH.read_text(encoding="utf-8")


def write(content: str) -> None:
    PATH.write_text(content, encoding="utf-8", newline="\n")


def replace_once(old: str, new: str) -> None:
    content = read()
    count = content.count(old)
    if count != 1:
        raise RuntimeError(f"AudiobookContentMoveService.cs: expected one match, found {count}: {old.splitlines()[0]}")
    write(content.replace(old, new, 1))


replace_once(
    """        var sourceSemantics = request.SourceSemantics;
        var targetSemantics = request.TargetSemantics;
""",
    """        var sourceSemantics = request.SourceSemantics;
        var targetSemantics = request.TargetSemantics;
        await ValidatePersistedMoveIdentityAsync(
            request.JobId,
            source,
            target,
            sourceSemantics,
            targetSemantics,
            cancellationToken);
""",
)
replace_once(
    """        var recoveryMarkerPath = GetRecoveryMarkerPath(target, request.JobId);
        var recoveryStage = ReadRecoveryStage(recoveryMarkerPath);
        if (string.Equals(recoveryStage, CopyCompletedStage, StringComparison.Ordinal)
            && LoadManifest(request.JobId).Count == 0)
        {
            throw new MoveNeedsAttentionException(
                "A legacy copy-complete marker has no byte-verified manifest; source cleanup is blocked.");
        }

        var resumingDirectCopy = string.Equals(recoveryStage, CopyStartedStage, StringComparison.Ordinal);
        EnsureTargetCanReceiveContents(source, target, sourceInsideTarget, resumingDirectCopy, targetSemantics);
""",
    """        var recoveryMarkerPath = GetRecoveryMarkerPath(target, request.JobId);
        var recoveryMarker = ReadRecoveryMarker(recoveryMarkerPath);
        ValidateRecoveryMarker(recoveryMarker, request, source, target);
        var recoveryStage = recoveryMarker?.Stage;
        var persistedManifest = LoadManifest(request.JobId);
        if (recoveryStage is CopyStartedStage or CopyCompletedStage or SourceCleanupCompletedStage
            && persistedManifest.Count == 0)
        {
            throw new MoveNeedsAttentionException(
                "A move recovery marker exists without a persisted manifest; destination ownership cannot be proven.");
        }

        var resumingDirectCopy = recoveryStage == CopyStartedStage && persistedManifest.Count > 0;
        EnsureTargetCanReceiveContents(source, target, sourceInsideTarget, resumingDirectCopy, targetSemantics);
""",
)
replace_once(
    """                WriteRecoveryMarker(source, request.JobId, AtomicRenameCompletedStage);
""",
    """                WriteRecoveryMarker(
                    source,
                    request.JobId,
                    source,
                    target,
                    AtomicRenameCompletedStage);
""",
)
replace_once(
    """            var manifest = await LoadOrCreateManifestAsync(
                request.JobId,
                request.LeaseToken,
                source,
                target,
                targetInsideSource,
                sourceSemantics,
                cancellationToken);
""",
    """            var manifest = persistedManifest.Count > 0
                ? persistedManifest
                : await LoadOrCreateManifestAsync(
                    request.JobId,
                    request.LeaseToken,
                    source,
                    target,
                    targetInsideSource,
                    sourceSemantics,
                    cancellationToken);
""",
)
replace_once(
    """                WriteRecoveryMarker(copyDestination, request.JobId, CopyStartedStage);
""",
    """                WriteRecoveryMarker(
                    copyDestination,
                    request.JobId,
                    source,
                    target,
                    CopyStartedStage);
""",
)
replace_once(
    """            WriteRecoveryMarker(copyDestination, request.JobId, CopyCompletedStage);
""",
    """            WriteRecoveryMarker(
                copyDestination,
                request.JobId,
                source,
                target,
                CopyCompletedStage);
""",
)
replace_once(
    """            WriteRecoveryMarker(target, request.JobId, SourceCleanupCompletedStage);
""",
    """            WriteRecoveryMarker(
                target,
                request.JobId,
                source,
                target,
                SourceCleanupCompletedStage);
""",
)
replace_once(
    """        var recoveryMarkerPath = GetRecoveryMarkerPath(target, request.JobId);
        var recoveryStage = ReadRecoveryStage(recoveryMarkerPath);
        var sourceSemantics = request.SourceSemantics;
        var targetSemantics = request.TargetSemantics;
        var manifest = LoadManifest(request.JobId);
""",
    """        var recoveryMarkerPath = GetRecoveryMarkerPath(target, request.JobId);
        var sourceSemantics = request.SourceSemantics;
        var targetSemantics = request.TargetSemantics;
        MoveRecoveryMarker? recoveryMarker;
        try
        {
            recoveryMarker = ReadRecoveryMarker(recoveryMarkerPath);
            ValidateRecoveryMarker(recoveryMarker, request, source, target);
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            logger.LogWarning(
                exception,
                "Rejected invalid recovery marker for move job {JobId}",
                request.JobId);
            return null;
        }

        var recoveryStage = recoveryMarker?.Stage;
        var manifest = LoadManifest(request.JobId);
""",
)
replace_once(
    """            if (!atomicRenameCompleted)
            {
                await VerifyPublishedManifestAsync(
                    target,
                    manifest,
                    targetSemantics,
                    cancellationToken);
            }
""",
    """            if (!atomicRenameCompleted)
            {
                ValidateExistingDestinationContents(
                    source,
                    target,
                    manifest,
                    request.JobId,
                    targetSemantics);
                await VerifyPublishedManifestAsync(
                    target,
                    manifest,
                    targetSemantics,
                    cancellationToken);
            }
""",
)
replace_once(
    """        WriteRecoveryMarker(result.Target, request.JobId, SourceCleanupCompletedStage);
""",
    """        WriteRecoveryMarker(
            result.Target,
            request.JobId,
            result.Source,
            result.Target,
            SourceCleanupCompletedStage);
""",
)
replace_once(
    """        return Directory
            .EnumerateFileSystemEntries(source, "*", SearchOption.AllDirectories)
            .All(entry => IsSameOrInside(entry, target, semantics) || IsSameOrInside(target, entry, semantics));
""",
    """        if (!FileSystemSafety.TryEnumerateTreeWithoutLinks(
                source,
                out var files,
                out var directories,
                out _))
        {
            return false;
        }

        return files
            .Concat(directories)
            .All(entry => IsSameOrInside(entry, target, semantics) || IsSameOrInside(target, entry, semantics));
""",
)
content = read()
pattern = re.compile(
    r"\n    private string\? ReadRecoveryStage\(string markerPath\)\n    \{\n.*?\n    \}\n\n    private static bool IsTargetEntryAllowedBySourceSubtree",
    re.DOTALL,
)
content, count = pattern.subn(
    "\n    private static bool IsTargetEntryAllowedBySourceSubtree",
    content,
    count=1,
)
if count != 1:
    raise RuntimeError("AudiobookContentMoveService.cs: failed to remove ReadRecoveryStage")
write(content)
