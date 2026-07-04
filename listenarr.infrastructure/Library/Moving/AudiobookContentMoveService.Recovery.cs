/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Listenarr.Infrastructure.Library.Moving;

internal sealed partial class AudiobookContentMoveService
{
    private const string CopyStartedStage = "copy-started";
    private const string CopyCompletedStage = "copy-complete";
    private const string AtomicRenameCompletedStage = "atomic-rename-complete";
    private const string SourceCleanupCompletedStage = "source-cleanup-complete";

    private static void WriteRecoveryMarker(string target, Guid jobId, string stage)
    {
        var markerPath = GetRecoveryMarkerPath(target, jobId);
        if (OperatingSystem.IsWindows() && File.Exists(markerPath))
        {
            File.SetAttributes(markerPath, FileAttributes.Normal);
        }

        File.WriteAllText(markerPath, stage);
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(markerPath, FileAttributes.Hidden);
        }
    }

    private static string GetRecoveryMarkerPath(string target, Guid jobId) =>
        Path.Join(target, $".listenarr-move-{jobId:N}.pending");
}
