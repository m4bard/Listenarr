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
        string Stage);

    private sealed record ParsedRecoveryMarker(
        MoveRecoveryMarker? StructuredMarker,
        string? ObsoleteStage)
    {
        public string Stage => StructuredMarker?.Stage
            ?? ObsoleteStage
            ?? throw new InvalidOperationException("The recovery marker has no stage.");

        public bool IsObsolete => ObsoleteStage != null;
    }

    private void WriteRecoveryMarker(
        string markerDirectory,
        AudiobookContentMoveRequest request,
        string source,
        string target,
        string stage)
    {
        ValidateExistingMoveDirectory(markerDirectory, "recovery-marker directory");
        var markerPath = GetRecoveryMarkerPath(markerDirectory, request.JobId);
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
            request.JobId,
            Path.GetFullPath(source),
            Path.GetFullPath(target),
            stage);
        var payload = JsonSerializer.SerializeToUtf8Bytes(marker);
        var writePath = markerPath + $".writing-{Guid.NewGuid():N}";
        FileAttributes? previousMarkerAttributes = null;

        faultInjector?.OnRecoveryMarkerWrite(
            request.JobId,
            RecoveryMarkerWriteFaultPoint.BeforeTemporaryFileCreation);

        try
        {
            ValidateNewRecoveryMarkerWritePath(writePath, markerDirectory);
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
                    request.JobId,
                    RecoveryMarkerWriteFaultPoint.DuringJsonWrite);
                stream.Write(payload.AsSpan(split));
                faultInjector?.OnRecoveryMarkerWrite(
                    request.JobId,
                    RecoveryMarkerWriteFaultPoint.DuringFlush);
                stream.Flush(flushToDisk: true);
            }

            faultInjector?.OnRecoveryMarkerWrite(
                request.JobId,
                RecoveryMarkerWriteFaultPoint.AfterTemporaryFileWritten);
            faultInjector?.OnRecoveryMarkerWrite(
                request.JobId,
                RecoveryMarkerWriteFaultPoint.BeforePublication);

            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(
                    writePath,
                    File.GetAttributes(writePath) | FileAttributes.Hidden);
                if (File.Exists(markerPath))
                {
                    ValidateExistingRecoveryMarker(
                        markerDirectory,
                        markerPath,
                        request,
                        source,
                        target);
                    previousMarkerAttributes = File.GetAttributes(markerPath);
                    File.SetAttributes(
                        markerPath,
                        previousMarkerAttributes.Value & ~FileAttributes.Hidden);
                }
            }

            ValidateRecoveryMarkerPublicationPaths(
                markerDirectory,
                writePath,
                markerPath);
            if (File.Exists(markerPath))
            {
                ValidateExistingRecoveryMarker(
                    markerDirectory,
                    markerPath,
                    request,
                    source,
                    target);
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
                    ValidateExistingRecoveryMarker(
                        markerDirectory,
                        markerPath,
                        request,
                        source,
                        target);
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
                    request.JobId,
                    RecoveryMarkerWriteFaultPoint.BeforeTemporaryFileDeletion);
                if (File.Exists(writePath))
                {
                    ValidateRecoveryMarkerWritePath(writePath, markerDirectory);
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

            if (marker == null || !IsKnownRecoveryStage(marker.Stage))
            {
                throw new MoveNeedsAttentionException(
                    "A recovery-marker write-temporary file is invalid and was preserved for operator review.");
            }

            ValidateRecoveryMarker(
                new ParsedRecoveryMarker(marker, ObsoleteStage: null),
                request,
                source,
                target);
            ValidateExistingMoveDirectory(
                markerDirectory,
                "recovery-marker cleanup directory");
            ValidateRecoveryMarkerWritePath(safeWritePath, markerDirectory);
            var currentMarker = JsonSerializer.Deserialize<MoveRecoveryMarker>(
                File.ReadAllText(safeWritePath));
            if (currentMarker == null || !IsKnownRecoveryStage(currentMarker.Stage))
            {
                throw new MoveNeedsAttentionException(
                    "A recovery-marker write-temporary file changed before deletion.");
            }

            ValidateRecoveryMarker(
                new ParsedRecoveryMarker(currentMarker, ObsoleteStage: null),
                request,
                source,
                target);
            File.Delete(safeWritePath);
            logger.LogInformation(
                "Removed validated orphan recovery-marker write file for move job {JobId}",
                request.JobId);
        }
    }

    private void ValidateExistingRecoveryMarker(
        string markerDirectory,
        string markerPath,
        AudiobookContentMoveRequest request,
        string source,
        string target)
    {
        ValidateExistingMoveDirectory(markerDirectory, "recovery-marker directory");
        if (!FileSystemSafety.TryValidateMutationTarget(
                markerPath,
                [markerDirectory],
                out markerPath,
                out var markerReason))
        {
            throw new MoveNeedsAttentionException(markerReason);
        }

        if (!File.Exists(markerPath)
            || (File.GetAttributes(markerPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new MoveNeedsAttentionException(
                "The existing recovery marker is missing or linked.");
        }

        ValidateRecoveryMarker(
            ReadRecoveryMarker(markerPath),
            request,
            source,
            target);
    }

    private static void ValidateNewRecoveryMarkerWritePath(
        string writePath,
        string markerDirectory)
    {
        ValidateExistingMoveDirectory(markerDirectory, "recovery-marker directory");
        if (!FileSystemSafety.TryValidateMutationTarget(
                writePath,
                [markerDirectory],
                out writePath,
                out var writeReason))
        {
            throw new MoveNeedsAttentionException(writeReason);
        }

        if (File.Exists(writePath) || Directory.Exists(writePath))
        {
            throw new MoveNeedsAttentionException(
                "The recovery-marker temporary path appeared before creation.");
        }
    }

    private static void ValidateRecoveryMarkerPublicationPaths(
        string markerDirectory,
        string writePath,
        string markerPath)
    {
        ValidateExistingMoveDirectory(markerDirectory, "recovery-marker directory");
        ValidateRecoveryMarkerWritePath(writePath, markerDirectory);
        if (!FileSystemSafety.TryValidateMutationTarget(
                markerPath,
                [markerDirectory],
                out markerPath,
                out var markerReason))
        {
            throw new MoveNeedsAttentionException(markerReason);
        }

        if (File.Exists(markerPath)
            && (File.GetAttributes(markerPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new MoveNeedsAttentionException(
                "The authoritative recovery marker became a symbolic link or reparse point.");
        }
    }

    private static void ValidateRecoveryMarkerWritePath(
        string writePath,
        string markerDirectory)
    {
        ValidateExistingMoveDirectory(markerDirectory, "recovery-marker directory");
        if (!FileSystemSafety.TryValidateMutationTarget(
                writePath,
                [markerDirectory],
                out writePath,
                out var writeReason))
        {
            throw new MoveNeedsAttentionException(writeReason);
        }

        if (!File.Exists(writePath)
            || (File.GetAttributes(writePath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new MoveNeedsAttentionException(
                "The recovery-marker write-temporary file is missing or linked.");
        }
    }

    private ParsedRecoveryMarker? ReadRecoveryMarker(string markerPath)
    {
        if (!File.Exists(markerPath))
        {
            return null;
        }

        if ((File.GetAttributes(markerPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new MoveNeedsAttentionException(
                "The move recovery marker is a symbolic link or reparse point.");
        }

        try
        {
            var content = File.ReadAllText(markerPath).Trim();
            if (IsKnownRecoveryStage(content))
            {
                return new ParsedRecoveryMarker(
                    StructuredMarker: null,
                    ObsoleteStage: content);
            }

            var marker = JsonSerializer.Deserialize<MoveRecoveryMarker>(content);
            if (marker == null || !IsKnownRecoveryStage(marker.Stage))
            {
                throw new MoveNeedsAttentionException("Move recovery marker is invalid.");
            }

            return new ParsedRecoveryMarker(
                StructuredMarker: marker,
                ObsoleteStage: null);
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
        ParsedRecoveryMarker? parsedMarker,
        AudiobookContentMoveRequest request,
        string source,
        string target)
    {
        if (parsedMarker == null)
        {
            return;
        }

        if (parsedMarker.IsObsolete)
        {
            throw new MoveNeedsAttentionException(
                "This move contains an obsolete pre-release recovery marker and cannot be resumed safely.");
        }

        var marker = parsedMarker.StructuredMarker
            ?? throw new MoveNeedsAttentionException("The move recovery marker is invalid.");
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
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException or NotSupportedException or PathTooLongException)
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
