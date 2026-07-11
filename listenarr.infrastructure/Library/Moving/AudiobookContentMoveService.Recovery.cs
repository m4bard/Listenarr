/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Text.Json;
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private const int RecoveryMarkerVersion = 1;
    private const string CopyStartedStage = "copy-started";
    private const string CopyCompletedStage = "copy-complete";
    private const string AtomicRenameCompletedStage = "atomic-rename-complete";
    private const string SourceCleanupCompletedStage = "source-cleanup-complete";

    private sealed record MoveRecoveryMarker(
        int Version,
        Guid JobId,
        string Source,
        string Target,
        string Stage,
        bool IsLegacy = false);

    private void WriteRecoveryMarker(
        string markerDirectory,
        Guid jobId,
        string source,
        string target,
        string stage)
    {
        ValidateExistingMoveDirectory(markerDirectory, "recovery-marker directory");
        var markerPath = GetRecoveryMarkerPath(markerDirectory, jobId);
        if (!FileSystemSafety.TryValidateMutationTarget(
                markerPath,
                [markerDirectory],
                out markerPath,
                out var markerReason))
        {
            throw new MoveNeedsAttentionException(markerReason);
        }

        var marker = new MoveRecoveryMarker(
            RecoveryMarkerVersion,
            jobId,
            Path.GetFullPath(source),
            Path.GetFullPath(target),
            stage);
        var payload = JsonSerializer.SerializeToUtf8Bytes(marker);
        var writePath = markerPath + $".writing-{Guid.NewGuid():N}";
        FileAttributes? previousMarkerAttributes = null;

        faultInjector?.OnRecoveryMarkerWrite(
            jobId,
            RecoveryMarkerWriteFaultPoint.BeforeTemporaryFileCreation);

        try
        {
            using (var stream = new FileStream(
                writePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                var split = Math.Max(1, payload.Length / 2);
                stream.Write(payload.AsSpan(0, split));
                faultInjector?.OnRecoveryMarkerWrite(
                    jobId,
                    RecoveryMarkerWriteFaultPoint.DuringJsonWrite);
                stream.Write(payload.AsSpan(split));
                faultInjector?.OnRecoveryMarkerWrite(
                    jobId,
                    RecoveryMarkerWriteFaultPoint.DuringFlush);
                stream.Flush(flushToDisk: true);
            }

            faultInjector?.OnRecoveryMarkerWrite(
                jobId,
                RecoveryMarkerWriteFaultPoint.AfterTemporaryFileWritten);
            faultInjector?.OnRecoveryMarkerWrite(
                jobId,
                RecoveryMarkerWriteFaultPoint.BeforePublication);

            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(
                    writePath,
                    File.GetAttributes(writePath) | FileAttributes.Hidden);
                if (File.Exists(markerPath))
                {
                    previousMarkerAttributes = File.GetAttributes(markerPath);
                    File.SetAttributes(
                        markerPath,
                        previousMarkerAttributes.Value & ~FileAttributes.Hidden);
                }
            }

            File.Move(writePath, markerPath, overwrite: true);
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            Exception? restorationException = null;
            if (OperatingSystem.IsWindows()
                && previousMarkerAttributes.HasValue
                && File.Exists(markerPath))
            {
                try
                {
                    File.SetAttributes(markerPath, previousMarkerAttributes.Value);
                }
                catch (Exception restoreException) when (WorkerExceptionClassifier.IsNonFatal(restoreException))
                {
                    restorationException = restoreException;
                }
            }

            Exception? cleanupException = null;
            try
            {
                faultInjector?.OnRecoveryMarkerWrite(
                    jobId,
                    RecoveryMarkerWriteFaultPoint.BeforeTemporaryFileDeletion);
                if (File.Exists(writePath))
                {
                    File.Delete(writePath);
                }
            }
            catch (Exception temporaryCleanupException) when (WorkerExceptionClassifier.IsNonFatal(temporaryCleanupException))
            {
                cleanupException = temporaryCleanupException;
            }

            if (restorationException != null || cleanupException != null)
            {
                throw new MoveNeedsAttentionException(
                    $"Recovery marker publication failed and recovery state could not be restored cleanly. "
                    + $"Publication error: {exception.Message}. "
                    + $"Attribute restoration error: {restorationException?.Message ?? "none"}. "
                    + $"Temporary cleanup error: {cleanupException?.Message ?? "none"}.");
            }

            throw;
        }
    }

    private void DeleteOwnedRecoveryMarkerWriteFiles(
        string markerDirectory,
        AudiobookContentMoveRequest request,
        string source,
        string target)
    {
        if (!Directory.Exists(markerDirectory))
        {
            return;
        }

        ValidateExistingMoveDirectory(markerDirectory, "recovery-marker cleanup directory");
        var authoritativeMarkerPath = GetRecoveryMarkerPath(markerDirectory, request.JobId);
        var writeFilePrefix = Path.GetFileName(authoritativeMarkerPath) + ".writing-";
        foreach (var writePath in Directory.EnumerateFiles(
            markerDirectory,
            writeFilePrefix + "*",
            SearchOption.TopDirectoryOnly))
        {
            if (!FileSystemSafety.TryValidateMutationTarget(
                    writePath,
                    [markerDirectory],
                    out var safeWritePath,
                    out var writeReason))
            {
                throw new MoveNeedsAttentionException(writeReason);
            }

            if ((File.GetAttributes(safeWritePath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new MoveNeedsAttentionException(
                    "A recovery-marker write-temporary file is a symbolic link or reparse point.");
            }

            MoveRecoveryMarker? marker;
            try
            {
                marker = JsonSerializer.Deserialize<MoveRecoveryMarker>(File.ReadAllText(safeWritePath));
            }
            catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
            {
                throw new MoveNeedsAttentionException(
                    $"A recovery-marker write-temporary file could not be validated safely: {exception.Message}");
            }

            if (marker == null || marker.IsLegacy || !IsKnownRecoveryStage(marker.Stage))
            {
                throw new MoveNeedsAttentionException(
                    "A recovery-marker write-temporary file is invalid and was preserved for operator review.");
            }

            ValidateRecoveryMarker(marker, request, source, target);
            File.Delete(safeWritePath);
            logger.LogInformation(
                "Removed validated orphan recovery-marker write file for move job {JobId}",
                request.JobId);
        }
    }

    private MoveRecoveryMarker? ReadRecoveryMarker(string markerPath)
    {
        if (!File.Exists(markerPath))
        {
            return null;
        }

        try
        {
            var content = File.ReadAllText(markerPath).Trim();
            if (IsKnownRecoveryStage(content))
            {
                return new MoveRecoveryMarker(
                    0,
                    Guid.Empty,
                    string.Empty,
                    string.Empty,
                    content,
                    IsLegacy: true);
            }

            var marker = JsonSerializer.Deserialize<MoveRecoveryMarker>(content);
            if (marker == null || !IsKnownRecoveryStage(marker.Stage))
            {
                throw new MoveNeedsAttentionException("Move recovery marker is invalid.");
            }

            return marker;
        }
        catch (MoveNeedsAttentionException)
        {
            throw;
        }
        catch (Exception exception) when (WorkerExceptionClassifier.IsNonFatal(exception))
        {
            logger.LogWarning(
                exception,
                "Failed to read move recovery marker {Marker}",
                LogRedaction.SanitizeFilePath(markerPath));
            throw new MoveNeedsAttentionException("Move recovery marker could not be read safely.");
        }
    }

    private static void ValidateRecoveryMarker(
        MoveRecoveryMarker? marker,
        AudiobookContentMoveRequest request,
        string source,
        string target)
    {
        if (marker == null || marker.IsLegacy)
        {
            return;
        }

        if (marker.Version != RecoveryMarkerVersion || marker.JobId != request.JobId)
        {
            throw new MoveNeedsAttentionException(
                "Move recovery marker is owned by a different job or unsupported marker version.");
        }

        try
        {
            if (!FileSystemPathIdentity.AreEquivalent(marker.Source, source, request.SourceSemantics)
                || !FileSystemPathIdentity.AreEquivalent(marker.Target, target, request.TargetSemantics))
            {
                throw new MoveNeedsAttentionException(
                    "Move recovery marker source or target identity does not match the persisted job.");
            }
        }
        catch (ArgumentException)
        {
            throw new MoveNeedsAttentionException(
                "Move recovery marker contains an invalid source or target identity.");
        }
    }

    private static void ValidateRecoveryMarkerLocation(
        string markerPath,
        string target,
        FileSystemPathSemantics targetSemantics)
    {
        var markerDirectory = Path.GetDirectoryName(Path.GetFullPath(markerPath));
        var reason = string.Empty;
        if (string.IsNullOrWhiteSpace(markerDirectory)
            || !FileSystemPathIdentity.AreEquivalent(markerDirectory, target, targetSemantics)
            || !FileSystemSafety.TryValidateMutationTarget(
                markerPath,
                [target],
                out _,
                out reason))
        {
            throw new MoveNeedsAttentionException(
                string.IsNullOrWhiteSpace(reason)
                    ? "Move recovery marker is not located inside the persisted target directory."
                    : reason);
        }
    }

    private static bool IsKnownRecoveryStage(string? stage) =>
        stage is CopyStartedStage
            or CopyCompletedStage
            or AtomicRenameCompletedStage
            or SourceCleanupCompletedStage;

    private static string GetRecoveryMarkerPath(string target, Guid jobId) =>
        Path.Join(target, $".listenarr-move-{jobId:N}.pending");
}
