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

    private static void WriteRecoveryMarker(
        string markerDirectory,
        Guid jobId,
        string source,
        string target,
        string stage)
    {
        var markerPath = GetRecoveryMarkerPath(markerDirectory, jobId);
        if (OperatingSystem.IsWindows() && File.Exists(markerPath))
        {
            File.SetAttributes(markerPath, FileAttributes.Normal);
        }

        var marker = new MoveRecoveryMarker(
            RecoveryMarkerVersion,
            jobId,
            Path.GetFullPath(source),
            Path.GetFullPath(target),
            stage);
        File.WriteAllText(markerPath, JsonSerializer.Serialize(marker));
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(markerPath, FileAttributes.Hidden);
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
                "Move recovery marker is owned by a different job or unsupported manifest version.");
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

    private static bool IsKnownRecoveryStage(string? stage) =>
        stage is CopyStartedStage
            or CopyCompletedStage
            or AtomicRenameCompletedStage
            or SourceCleanupCompletedStage;

    private static string GetRecoveryMarkerPath(string target, Guid jobId) =>
        Path.Join(target, $".listenarr-move-{jobId:N}.pending");
}
