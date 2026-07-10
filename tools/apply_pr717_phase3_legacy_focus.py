from pathlib import Path
import re

root = Path(__file__).resolve().parents[1]

main_path = root / "listenarr.infrastructure/Library/Moving/AudiobookContentMoveService.cs"
main = main_path.read_text(encoding="utf-8")
call_old = """        await ValidatePersistedMoveIdentityAsync(
            request.JobId,
            source,
            target,
            sourceSemantics,
            targetSemantics,
            cancellationToken);
"""
call_new = """        await ValidatePersistedMoveIdentityAsync(
            request.JobId,
            source,
            target,
            sourceSemantics,
            targetSemantics,
            request.LeaseToken,
            cancellationToken);
"""
if main.count(call_old) != 1:
    raise RuntimeError("Persisted identity call anchor mismatch")
main = main.replace(call_old, call_new, 1)

validation_pattern = re.compile(
    r"\n    private static void EnsureTargetCanReceiveContents\(.*?\n    private static void TryDeleteTempDirectory",
    re.DOTALL,
)
main, count = validation_pattern.subn(
    "\n    private static void TryDeleteTempDirectory",
    main,
    count=1,
)
if count != 1:
    raise RuntimeError("Move validation extraction anchor mismatch")
main_path.write_text(main, encoding="utf-8", newline="\n")

validation_path = root / "listenarr.infrastructure/Library/Moving/AudiobookContentMoveService.Validation.cs"
validation_path.write_text(
    '''/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Domain.Common;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private static void EnsureTargetCanReceiveContents(
        string source,
        string target,
        bool sourceInsideTarget,
        bool resumingOwnedDirectCopy,
        FileSystemPathSemantics semantics)
    {
        if (!Directory.Exists(target) || resumingOwnedDirectCopy)
        {
            return;
        }

        // When moving a child folder back into its parent, the target necessarily contains
        // the source subtree. That subtree is not a collision because it is the content being moved.
        var targetHasBlockingContent = Directory
            .EnumerateFileSystemEntries(target)
            .Any(entry => !(sourceInsideTarget && IsTargetEntryAllowedBySourceSubtree(entry, source, semantics)));
        if (targetHasBlockingContent)
        {
            throw new IOException(sourceInsideTarget
                ? "Destination contains unrelated content outside the source subtree"
                : "Target directory already exists and contains files");
        }
    }

    private static bool IsTargetEntryAllowedBySourceSubtree(
        string entry,
        string source,
        FileSystemPathSemantics semantics)
    {
        if (IsSameOrInside(entry, source, semantics))
        {
            return true;
        }

        if (!Directory.Exists(entry) || !IsSameOrInside(source, entry, semantics))
        {
            return false;
        }

        if (!FileSystemSafety.TryEnumerateTreeWithoutLinks(
                entry,
                out var files,
                out var directories,
                out _))
        {
            return false;
        }

        return files
            .Concat(directories)
            .All(child => IsSameOrInside(child, source, semantics) || IsSameOrInside(source, child, semantics));
    }
}
''',
    encoding="utf-8",
    newline="\n",
)

persistence_path = root / "listenarr.infrastructure/Library/Moving/AudiobookContentMoveService.Persistence.cs"
persistence = persistence_path.read_text(encoding="utf-8")
method_old = """    private async Task ValidatePersistedMoveIdentityAsync(
        Guid jobId,
        string source,
        string target,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics,
        CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var identity = await db.MoveJobs
            .AsNoTracking()
            .Where(job => job.Id == jobId)
            .Select(job => new { job.SourcePath, job.RequestedPath })
            .SingleOrDefaultAsync(cancellationToken);
        if (identity == null
            || string.IsNullOrWhiteSpace(identity.SourcePath)
            || string.IsNullOrWhiteSpace(identity.RequestedPath))
        {
            throw new MoveNeedsAttentionException(
                "Persisted move source and target identity are required before filesystem recovery.");
        }

        try
        {
            if (!FileSystemPathIdentity.AreEquivalent(identity.SourcePath, source, sourceSemantics)
                || !FileSystemPathIdentity.AreEquivalent(identity.RequestedPath, target, targetSemantics))
            {
                throw new MoveNeedsAttentionException(
                    "Persisted move source or target identity does not match the requested filesystem operation.");
            }
        }
        catch (ArgumentException)
        {
            throw new MoveNeedsAttentionException(
                "Persisted move source or target identity is invalid.");
        }
    }
"""
method_new = """    private async Task ValidatePersistedMoveIdentityAsync(
        Guid jobId,
        string source,
        string target,
        FileSystemPathSemantics sourceSemantics,
        FileSystemPathSemantics targetSemantics,
        MoveLeaseToken leaseToken,
        CancellationToken cancellationToken)
    {
        EnsureLeaseTokenProvided(jobId, leaseToken);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var identity = await db.MoveJobs
            .AsNoTracking()
            .Where(job => job.Id == jobId)
            .Select(job => new { job.SourcePath, job.RequestedPath })
            .SingleOrDefaultAsync(cancellationToken);
        if (identity == null || string.IsNullOrWhiteSpace(identity.RequestedPath))
        {
            throw new MoveNeedsAttentionException(
                "Persisted move target identity is required before filesystem recovery.");
        }

        try
        {
            if (!FileSystemPathIdentity.AreEquivalent(identity.RequestedPath, target, targetSemantics))
            {
                throw new MoveNeedsAttentionException(
                    "Persisted move target identity does not match the requested filesystem operation.");
            }
        }
        catch (ArgumentException)
        {
            throw new MoveNeedsAttentionException("Persisted move target identity is invalid.");
        }

        var persistedSource = identity.SourcePath;
        if (string.IsNullOrWhiteSpace(persistedSource))
        {
            var hasRecoveryArtifacts = await db.MoveJobEntries.AnyAsync(
                    entry => entry.MoveJobId == jobId,
                    cancellationToken)
                || File.Exists(GetRecoveryMarkerPath(target, jobId))
                || File.Exists(GetRecoveryMarkerPath(source, jobId));
            if (hasRecoveryArtifacts)
            {
                throw new MoveNeedsAttentionException(
                    "A legacy move without a persisted source cannot own existing recovery artifacts.");
            }

            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            if (!db.Database.IsRelational())
            {
                var job = await db.MoveJobs.SingleOrDefaultAsync(
                    candidate => candidate.Id == jobId
                        && candidate.Status == MoveJobStatus.Running
                        && candidate.LeaseOwner == leaseToken.Owner
                        && candidate.LeaseGeneration == leaseToken.Generation
                        && candidate.LeaseExpiresAt != null
                        && candidate.LeaseExpiresAt > nowUtc,
                    cancellationToken);
                if (job == null || !string.IsNullOrWhiteSpace(job.SourcePath))
                {
                    throw new MoveLeaseLostException(jobId, leaseToken.Generation);
                }

                job.SourcePath = source;
                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                var affected = await db.MoveJobs
                    .Where(candidate => candidate.Id == jobId
                        && candidate.SourcePath == identity.SourcePath
                        && candidate.Status == MoveJobStatus.Running
                        && candidate.LeaseOwner == leaseToken.Owner
                        && candidate.LeaseGeneration == leaseToken.Generation
                        && candidate.LeaseExpiresAt != null
                        && candidate.LeaseExpiresAt > nowUtc)
                    .ExecuteUpdateAsync(
                        updates => updates.SetProperty(job => job.SourcePath, source),
                        cancellationToken);
                if (affected != 1)
                {
                    throw new MoveLeaseLostException(jobId, leaseToken.Generation);
                }
            }

            persistedSource = source;
        }

        try
        {
            if (!FileSystemPathIdentity.AreEquivalent(persistedSource, source, sourceSemantics))
            {
                throw new MoveNeedsAttentionException(
                    "Persisted move source identity does not match the requested filesystem operation.");
            }
        }
        catch (ArgumentException)
        {
            throw new MoveNeedsAttentionException("Persisted move source identity is invalid.");
        }
    }
"""
if persistence.count(method_old) != 1:
    raise RuntimeError("Persisted identity method anchor mismatch")
persistence_path.write_text(
    persistence.replace(method_old, method_new, 1),
    encoding="utf-8",
    newline="\n",
)

test_path = root / "tests/Features/Infrastructure/Library/Moving/MoveJobProcessorTests.cs"
tests = test_path.read_text(encoding="utf-8")
test_old = """            Assert.Equal(MoveJobStatus.Completed, updatedJob!.Status);
            Assert.True(File.Exists(Path.Join(target, "book.m4b")));
"""
test_new = """            Assert.Equal(MoveJobStatus.Completed, updatedJob!.Status);
            Assert.Equal(Path.GetFullPath(source), updatedJob.SourcePath);
            Assert.True(File.Exists(Path.Join(target, "book.m4b")));
"""
if tests.count(test_old) != 1:
    raise RuntimeError("Legacy source persistence test anchor mismatch")
test_path.write_text(tests.replace(test_old, test_new, 1), encoding="utf-8", newline="\n")
