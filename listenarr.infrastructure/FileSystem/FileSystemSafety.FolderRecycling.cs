/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using Listenarr.Domain.Common;

namespace Listenarr.Infrastructure.FileSystem;

/// <summary>
/// Outcome of preparing the recycle bin to receive a whole audiobook folder. Every value
/// other than <see cref="Ready"/> means nothing in the library was touched.
/// </summary>
internal enum RecycleFolderPreparationOutcome
{
    Ready,

    /// <summary>The bin is inside the folder being deleted, or the folder is inside the bin.</summary>
    Overlaps,

    /// <summary>
    /// Some part of the folder is on a different filesystem from the bin. Found before
    /// anything is moved, so the book is never left split between the two.
    /// </summary>
    CrossVolume,

    Blocked
}

/// <summary>
/// A freshly created directory in the bin that receives one audiobook folder's contents.
/// Created exclusively, so nothing else can already be in it and no recycled file inside
/// it can collide with an earlier deletion.
/// </summary>
internal sealed class RecycleFolderContainer : IDisposable
{
    private readonly PinnedDirectoryCreation _creation;
    private readonly string _name;

    internal RecycleFolderContainer(
        PinnedDirectoryCreation creation,
        string name,
        DateTime recycleTimeUtc)
    {
        _creation = creation;
        _name = name;
        RecycleTimeUtc = recycleTimeUtc;
        Directory = creation.OpenCreatedDirectoryAnchor();
    }

    internal PinnedDirectoryCreation.PinnedDirectoryAnchor Directory { get; }

    internal string FullPath => Directory.FullPath;

    /// <summary>One timestamp for the whole folder, so its files age out together.</summary>
    internal DateTime RecycleTimeUtc { get; }

    internal int RecycledFiles { get; set; }

    internal int UnstampedFiles { get; set; }

    /// <summary>
    /// Remove the container if nothing was moved into it, so a refused or empty delete
    /// does not leave a stray folder in the bin. Best effort: an empty directory left
    /// behind is harmless and the retention sweep prunes it.
    /// </summary>
    internal void RemoveIfEmpty()
    {
        try
        {
            if (!System.IO.Directory.EnumerateFileSystemEntries(Directory.FullPath).Any()
                && _creation.VisiblePathMatches())
            {
                _creation.DeletePinnedEmptyDirectoryImmediately(_name);
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
        }
    }

    public void Dispose()
    {
        Directory.Dispose();
        _creation.Dispose();
    }
}

internal static partial class FileSystemSafety
{
    /// <summary>
    /// Get the bin ready to take the contents of <paramref name="sourceFolder"/>, and prove
    /// before anything moves that every piece of the move can be a rename.
    ///
    /// The folder lands at bin/<paramref name="relativeParent"/>/folder-name, mirroring the
    /// library layout the same way <see cref="TryRecycleFile"/> does for a single file. If
    /// that name is taken by an earlier deletion, the folder gets a numbered suffix rather
    /// than being merged into it. Readarr merges and overwrites same-named files there
    /// (DiskTransferService.TransferFolder calling TransferFile with overwrite), which
    /// destroys the earlier deletion.
    /// </summary>
    internal static RecycleFolderPreparationOutcome TryPrepareFolderRecycle(
        PinnedDirectoryCreation.PinnedDirectoryAnchor sourceFolder,
        string normalizedFolderPath,
        string recycleBinDirectory,
        string? relativeParent,
        TimeProvider timeProvider,
        out RecycleFolderContainer? container,
        out string reason)
    {
        container = null;
        reason = string.Empty;

        if (!FileSystemPathIdentity.TryCanonicalizeStoredAbsolutePathForHost(
                recycleBinDirectory,
                out var normalizedBin,
                out var binReason))
        {
            reason = $"The recycle bin path is unusable: {binReason}";
            return RecycleFolderPreparationOutcome.Blocked;
        }

        var normalizedFolder = Path.TrimEndingDirectorySeparator(normalizedFolderPath);
        if (PathIsWithin(normalizedBin, normalizedFolder)
            || PathIsWithin(normalizedFolder, normalizedBin))
        {
            reason = "The recycle bin and the audiobook folder overlap.";
            return RecycleFolderPreparationOutcome.Overlaps;
        }

        var folderName = Path.GetFileName(normalizedFolder);
        if (string.IsNullOrWhiteSpace(folderName))
        {
            reason = "The audiobook folder has no name to recycle it under.";
            return RecycleFolderPreparationOutcome.Blocked;
        }

        using var binRoot = PinnedDirectoryCreation.OpenPinnedHierarchyNoFollow(
            normalizedBin,
            createMissing: true);

        // Checked against the bin root before creating anything inside it, and for every
        // directory in the folder, not just its top. A disc subfolder that is a mount
        // point would otherwise be found only when its first file failed to rename, after
        // the rest of the book had already gone.
        if (!IsTreeOnVolume(sourceFolder, binRoot))
        {
            reason = "Part of the audiobook folder is on a different filesystem from the recycle bin.";
            return RecycleFolderPreparationOutcome.CrossVolume;
        }

        var safeParent = SanitizeRecycleSubfolder(relativeParent);
        using var parent = string.IsNullOrEmpty(safeParent)
            ? binRoot.Duplicate()
            : PinnedDirectoryCreation.OpenPinnedHierarchyNoFollow(
                Path.Join(normalizedBin, safeParent),
                createMissing: true);

        for (var attempt = 0; attempt < RecycleNameCollisionAttempts; attempt++)
        {
            var candidate = attempt == 0 ? folderName : $"{folderName}_{attempt}";
            var creation = parent.TryCreateChildForPublication(candidate);
            if (!creation.Created)
            {
                creation.Dispose();
                continue;
            }

            var created = new RecycleFolderContainer(
                creation,
                candidate,
                timeProvider.GetUtcNow().UtcDateTime);

            // A filesystem mounted somewhere below the bin root.
            if (!sourceFolder.IsOnSameVolume(created.Directory))
            {
                created.RemoveIfEmpty();
                created.Dispose();
                reason = "The recycle bin folder for this audiobook is on a different filesystem from the library.";
                return RecycleFolderPreparationOutcome.CrossVolume;
            }

            container = created;
            return RecycleFolderPreparationOutcome.Ready;
        }

        reason = $"{RecycleNameCollisionAttempts} folder names were already taken in the recycle bin.";
        return RecycleFolderPreparationOutcome.Blocked;
    }

    /// <summary>
    /// Move one already-opened, already-verified file from the recursive delete into the
    /// bin. The caller has done every check the unlink would have had; only the terminal
    /// act changes, a pinned no-replace rename in place of <c>entry.Delete()</c>.
    /// </summary>
    internal static bool TryMoveIntoRecycleFolder(
        PinnedDirectoryCreation.PinnedFileEntry entry,
        PinnedDirectoryCreation.PinnedDirectoryAnchor destination,
        string fileName,
        RecycleFolderContainer container,
        out string reason)
    {
        // Stamped first for the reason given in TryRecycleFile: a rename keeps the old
        // timestamp, and a sweep that runs between the rename and a later stamp would
        // remove the file at once.
        var stamped = TryStampRecycleTime(entry, container.RecycleTimeUtc);
        if (!TryPublishIntoRecycleBin(
                entry,
                destination,
                fileName,
                out var publishedName,
                out _,
                out reason))
        {
            return false;
        }

        if (!stamped)
        {
            stamped = TryStampRecycleTimeByPath(
                Path.Join(destination.FullPath, publishedName),
                container.RecycleTimeUtc);
        }

        container.RecycledFiles++;
        if (!stamped)
        {
            container.UnstampedFiles++;
        }

        return true;
    }

    /// <summary>
    /// Create the bin-side twin of a subdirectory of the folder being recycled. The
    /// container is new, so the name being taken already means something else is writing
    /// into it, and the move stops rather than mixing two deletions together.
    /// </summary>
    internal static PinnedDirectoryCreation.PinnedDirectoryAnchor CreateRecycleSubfolder(
        PinnedDirectoryCreation.PinnedDirectoryAnchor parent,
        string name)
    {
        using var creation = parent.TryCreateChild(name);
        if (!creation.Created)
        {
            throw new IOException(
                "A folder of the same name appeared in the recycle bin while the audiobook was being moved into it.");
        }

        return creation.OpenCreatedDirectoryAnchor();
    }

    private static bool IsTreeOnVolume(
        PinnedDirectoryCreation.PinnedDirectoryAnchor directory,
        PinnedDirectoryCreation.PinnedDirectoryAnchor volume)
    {
        if (!directory.IsOnSameVolume(volume))
        {
            return false;
        }

        foreach (var entryPath in System.IO.Directory.EnumerateDirectories(directory.FullPath))
        {
            // Links were refused by the caller's preflight; skipping them here keeps this
            // walk from following one if it appeared since.
            if ((File.GetAttributes(entryPath) & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            var name = Path.GetFileName(entryPath);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            using var child = directory.OpenExistingChild(name);
            if (!IsTreeOnVolume(child, volume))
            {
                return false;
            }
        }

        return true;
    }
}
