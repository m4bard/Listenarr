/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Listenarr.Infrastructure.DownloadClients.Nzbget;

internal static class NzbgetFailureMessageMapper
{
    public static string Map(NzbgetHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var status = entry.RawStatus.Trim();

        if (status.StartsWith("FAILURE/MOVE", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("FAILURE/POSTPROCESS", StringComparison.OrdinalIgnoreCase))
        {
            return "NZBGet failed during post-processing or final move. Check the NZBGet completed folder for existing files, permissions, or path conflicts.";
        }

        if (status.StartsWith("FAILURE/UNPACK", StringComparison.OrdinalIgnoreCase))
        {
            return "NZBGet failed while unpacking the download. The archive may be damaged, incomplete, password-protected, or missing parts.";
        }

        if (status.StartsWith("FAILURE/PAR", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("FAILURE/REPAIR", StringComparison.OrdinalIgnoreCase))
        {
            return "NZBGet failed while verifying or repairing the download. The release may be incomplete or damaged.";
        }

        if (status.StartsWith("FAILURE/HEALTH", StringComparison.OrdinalIgnoreCase))
        {
            return "NZBGet marked the download failed because its health dropped below the required threshold.";
        }

        return string.IsNullOrWhiteSpace(status)
            ? "NZBGet reported a failed download."
            : $"NZBGet reported a failed download: {status}";
    }

    public static string MapDuplicate(NzbgetHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var status = entry.RawStatus.Trim();
        var detail = string.IsNullOrWhiteSpace(status) ? string.Empty : $" ({status})";
        // COPY does not mean the earlier entry ever downloaded anything: NZBGet also answers COPY
        // for an identical entry that is still queued, or that was deleted from the queue.
        return string.Equals(entry.DeleteStatus, "COPY", StringComparison.OrdinalIgnoreCase)
            ? $"NZBGet skipped this grab as a duplicate: its queue or history already holds an entry with exactly the same content{detail}, so nothing was downloaded. That entry may still be downloading, or may have finished, failed or been deleted. NZBGet keeps answering this way while that entry exists, so delete it from NZBGet's queue or history first, then remove this download to allow the same release again."
            : $"NZBGet skipped this grab as a duplicate of an earlier download with the same name or duplicate key that already succeeded{detail}, so nothing was downloaded. Check the library first. NZBGet keeps answering this way while that successful entry is in its history, so delete it there before removing this download if the same release should be grabbed again.";
    }

    public static bool IsMoveOrPostProcessingFailure(string? rawStatus)
    {
        var status = (rawStatus ?? string.Empty).Trim();
        return status.StartsWith("FAILURE/MOVE", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("FAILURE/POSTPROCESS", StringComparison.OrdinalIgnoreCase);
    }
}
