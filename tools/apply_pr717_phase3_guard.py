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

test_path = root / "tests/Features/Infrastructure/Library/Moving/AudiobookContentMoveServiceTests.cs"
tests = test_path.read_text(encoding="utf-8")
test_old = """            var service = _provider.GetRequiredService<AudiobookContentMoveService>();
            var exception = await Assert.ThrowsAsync<MoveNeedsAttentionException>(async () =>
                service.MoveContentsAsync(
                    await CreateLeasedMoveRequestAsync(source, target, jobId),
                    CancellationToken.None));
"""
test_new = """            var service = _provider.GetRequiredService<AudiobookContentMoveService>();
            var request = await CreateLeasedMoveRequestAsync(source, target, jobId);
            var exception = await Assert.ThrowsAsync<MoveNeedsAttentionException>(() =>
                service.MoveContentsAsync(request, CancellationToken.None));
"""
if tests.count(test_old) != 1:
    raise RuntimeError("Direct-copy marker test await anchor mismatch")
test_path.write_text(tests.replace(test_old, test_new, 1), encoding="utf-8", newline="\n")
