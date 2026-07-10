from pathlib import Path

root = Path(__file__).resolve().parents[1]
path = root / "listenarr.infrastructure/Library/Moving/AudiobookContentMoveService.cs"
content = path.read_text(encoding="utf-8")

manifest_old = """        var persistedManifest = LoadManifest(request.JobId);
"""
manifest_new = """        var persistedManifest = await LoadManifestAsync(
            request.JobId,
            cancellationToken);
"""
if content.count(manifest_old) != 1:
    raise RuntimeError("Persisted manifest loader anchor mismatch")
content = content.replace(manifest_old, manifest_new, 1)

guard_old = """        if ((recoveryStage is CopyStartedStage or CopyCompletedStage or SourceCleanupCompletedStage)
            && persistedManifest.Count == 0)
        {
            throw new MoveNeedsAttentionException(
                \"A move recovery marker exists without a persisted manifest; destination ownership cannot be proven.\");
        }
"""
guard_new = """        if (recoveryMarker != null
            && persistedManifest.Count == 0
            && !string.Equals(recoveryStage, AtomicRenameCompletedStage, StringComparison.Ordinal))
        {
            throw new MoveNeedsAttentionException(
                \"A move recovery marker exists without a persisted manifest; destination ownership cannot be proven.\");
        }
"""
if content.count(guard_old) != 1:
    raise RuntimeError("Marker manifest guard anchor mismatch")
content = content.replace(guard_old, guard_new, 1)

path.write_text(content, encoding="utf-8", newline="\n")
