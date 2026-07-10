from pathlib import Path

root = Path(__file__).resolve().parents[1]
path = root / "listenarr.infrastructure/Library/Moving/AudiobookContentMoveService.cs"
content = path.read_text(encoding="utf-8")
old = """        if ((recoveryStage is CopyStartedStage or CopyCompletedStage or SourceCleanupCompletedStage)
            && persistedManifest.Count == 0)
        {
            throw new MoveNeedsAttentionException(
                \"A move recovery marker exists without a persisted manifest; destination ownership cannot be proven.\");
        }
"""
new = """        if (recoveryMarker != null
            && persistedManifest.Count == 0
            && !string.Equals(recoveryStage, AtomicRenameCompletedStage, StringComparison.Ordinal))
        {
            throw new MoveNeedsAttentionException(
                \"A move recovery marker exists without a persisted manifest; destination ownership cannot be proven.\");
        }
"""
if content.count(old) != 1:
    raise RuntimeError("Marker manifest guard anchor mismatch")
path.write_text(content.replace(old, new, 1), encoding="utf-8", newline="\n")
