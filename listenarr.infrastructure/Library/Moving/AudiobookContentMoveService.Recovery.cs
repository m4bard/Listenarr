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

    private async Task RecoverRecoveryMarkerWriteFilesAsync(
        string markerDirectory,
        AudiobookContentMoveRequest request,
        string source,
        string target,
        CancellationToken cancellationToken)
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
                SearchOption.TopDirectoryOnly)
            .ToList())
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

            if (!TryParseMarkerWriteIdentity(
                    safeWritePath,
                    authoritativeMarkerPath,
                    out var writeIdentity)
                || writeIdentity.JobId != request.JobId)
            {
                throw new MoveNeedsAttentionException(
                    "A recovery-marker write-temporary filename does not match the active move job.");
            }

            var marker = TryReadRecoveryMarkerWriteFile(safeWritePath);
            if (marker == null)
            {
                if (writeIdentity.LeaseGeneration >= request.LeaseGeneration)
                {
                    throw new MoveNeedsAttentionException(
                        "A current or future-generation recovery-marker write file is truncated and was preserved.");
                }

                await EnsureMutationAuthorizedAsync(request, source, target, cancellationToken);
                ValidateRecoveryMarkerWritePath(safeWritePath, markerDirectory);
                if (!TryParseMarkerWriteIdentity(
                        safeWritePath,
                        authoritativeMarkerPath,
                        out var currentIdentity)
                    || currentIdentity != writeIdentity)
                {
                    throw new MoveNeedsAttentionException(
                        "A truncated recovery-marker write file changed before cleanup.");
                }

                File.Delete(safeWritePath);
                logger.LogInformation(
                    "Removed truncated predecessor recovery-marker write file for move job {JobId}",
                    request.JobId);
                continue;
            }

            if (writeIdentity.LeaseGeneration > request.LeaseGeneration)
            {
                throw new MoveNeedsAttentionException(
                    "A future-generation recovery-marker write file was preserved.");
            }

            ValidateRecoveryMarker(
                new ParsedRecoveryMarker(marker, ObsoleteStage: null),
                request,
                source,
                target);
            var authoritativeMarker = ReadRecoveryMarker(authoritativeMarkerPath);
            ValidateRecoveryMarker(authoritativeMarker, request, source, target);
            if (authoritativeMarker == null)
            {
                await WriteRecoveryMarkerAsync(
                    markerDirectory,
                    request,
                    source,
                    target,
                    marker.Stage,
                    cancellationToken);
            }
            else if (!string.Equals(
                authoritativeMarker.Stage,
                marker.Stage,
                StringComparison.Ordinal))
            {
                if (CanAdvanceRecoveryStage(authoritativeMarker.Stage, marker.Stage))
                {
                    await WriteRecoveryMarkerAsync(
                        markerDirectory,
                        request,
                        source,
                        target,
                        marker.Stage,
                        cancellationToken);
                }
                else if (!CanAdvanceRecoveryStage(marker.Stage, authoritativeMarker.Stage))
                {
                    throw new MoveNeedsAttentionException(
                        "A recovery-marker write file belongs to an incompatible recovery workflow.");
                }
                // The authoritative marker is already at a later compatible stage. The
                // predecessor write file is redundant and can be removed below.
            }

            await EnsureMutationAuthorizedAsync(request, source, target, cancellationToken);
            ValidateRecoveryMarkerWritePath(safeWritePath, markerDirectory);
            var currentMarker = TryReadRecoveryMarkerWriteFile(safeWritePath)
                ?? throw new MoveNeedsAttentionException(
                    "A recovery-marker write-temporary file changed before deletion.");
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

    private static MoveRecoveryMarker? TryReadRecoveryMarkerWriteFile(string writePath)
    {
        try
        {
            var marker = JsonSerializer.Deserialize<MoveRecoveryMarker>(
                File.ReadAllText(writePath));
            return marker != null && IsKnownRecoveryStage(marker.Stage)
                ? marker
                : null;
        }
        catch (Exception exception) when (exception is
            JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void ValidateExistingRecoveryMarker(
        string markerDirectory,
        string markerPath,
        AudiobookContentMoveRequest request,
        string source,
        string target)
    {
        _ = ReadValidatedExistingRecoveryMarker(
            markerDirectory,
            markerPath,
            request,
            source,
            target);
    }

    private void ValidateExistingRecoveryMarkerForStage(
        string markerDirectory,
        string markerPath,
        AudiobookContentMoveRequest request,
        string source,
        string target,
        string candidateStage)
    {
        var existing = ReadValidatedExistingRecoveryMarker(
            markerDirectory,
            markerPath,
            request,
            source,
            target);
        if (!CanAdvanceRecoveryStage(existing.Stage, candidateStage))
        {
            throw new MoveNeedsAttentionException(
                "The existing recovery marker is already at a later or incompatible stage.");
        }
    }

    private ParsedRecoveryMarker ReadValidatedExistingRecoveryMarker(
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

        var parsed = ReadRecoveryMarker(markerPath)
            ?? throw new MoveNeedsAttentionException("The existing recovery marker disappeared.");
        ValidateRecoveryMarker(parsed, request, source, target);
        return parsed;
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

    private static bool CanAdvanceRecoveryStage(string currentStage, string candidateStage)
    {
        if (string.Equals(currentStage, candidateStage, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(currentStage, AtomicRenameCompletedStage, StringComparison.Ordinal)
            || string.Equals(candidateStage, AtomicRenameCompletedStage, StringComparison.Ordinal))
        {
            return false;
        }

        static int GetOrder(string stage) => stage switch
        {
            CopyStartedStage => 0,
            CopyCompletedStage => 1,
            SourceCleanupCompletedStage => 2,
            _ => -1
        };

        var currentOrder = GetOrder(currentStage);
        var candidateOrder = GetOrder(candidateStage);
        return currentOrder >= 0 && candidateOrder > currentOrder;
    }

    private static bool IsKnownRecoveryStage(string? stage) =>
        stage is CopyStartedStage
            or CopyCompletedStage
            or AtomicRenameCompletedStage
            or SourceCleanupCompletedStage;

    private static string GetRecoveryMarkerPath(string target, Guid jobId) =>
        Path.Join(target, $".listenarr-move-{jobId:N}.pending");
}
