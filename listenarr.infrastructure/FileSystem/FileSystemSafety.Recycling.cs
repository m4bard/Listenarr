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
/// Outcome of an attempt to move a library file into the recycle bin instead of
/// unlinking it. Every value other than <see cref="Recycled"/> and
/// <see cref="AlreadyGone"/> leaves the source file exactly where it was.
/// </summary>
internal enum RecycleFileOutcome
{
    /// <summary>The file now lives in the bin and the library path is free.</summary>
    Recycled,

    /// <summary>Nothing was there to recycle, proven against a pinned parent.</summary>
    AlreadyGone,

    /// <summary>
    /// The bin is on a different filesystem from the file. A rename cannot cross that
    /// boundary, and this path deliberately does not fall back to a multi-gigabyte copy:
    /// a half-written copy followed by a delete of the original is worse than the
    /// permanent delete the bin exists to prevent.
    /// </summary>
    CrossVolume,

    /// <summary>The move was refused by a safety check; the reason says which.</summary>
    Blocked
}

internal static partial class FileSystemSafety
{
    // Linux rename(2) reports a cross-filesystem rename as EXDEV.
    private const int CrossDeviceLinkErrno = 18;

    // ...and a no-replace rename onto an occupied name as EEXIST.
    private const int FileExistsErrno = 17;

    // Windows reports the same two conditions with its own codes:
    // ERROR_NOT_SAME_DEVICE, and ERROR_ALREADY_EXISTS or ERROR_FILE_EXISTS.
    private const int CrossDeviceLinkWindows = 17;
    private const int FileExistsWindows = 183;
    private const int FileExistsWindowsAlternate = 80;

    private static bool IsCrossDevice(int nativeError) =>
        OperatingSystem.IsWindows()
            ? nativeError == CrossDeviceLinkWindows
            : nativeError == CrossDeviceLinkErrno;

    private static bool IsNameTaken(int nativeError) =>
        OperatingSystem.IsWindows()
            ? nativeError is FileExistsWindows or FileExistsWindowsAlternate
            : nativeError == FileExistsErrno;

    private const int RecycleNameCollisionAttempts = 64;

    /// <summary>
    /// Move <paramref name="filePath"/> into <paramref name="recycleBinDirectory"/> rather
    /// than deleting it, reusing exactly the validation that <see cref="TryDeleteFile(string, IEnumerable{string?}, string?, out string)"/>
    /// performs: the path is canonicalized and proven to sit under an allowed root, the
    /// parent directory is pinned with a no-follow open, and the opened entry's physical
    /// generation is matched against the tracked identity before anything is mutated.
    /// Only the terminal act differs, a pinned no-replace rename in place of an unlink.
    /// </summary>
    /// <param name="relativeSubfolder">
    /// Where inside the bin the file should land, normally the library-relative folder the
    /// file came from. Mirroring the library shape keeps a chapter-per-file audiobook from
    /// colliding with another book's identically named chapter one.
    /// </param>
    public static RecycleFileOutcome TryRecycleFile(
        string filePath,
        IEnumerable<string?> allowedRoots,
        string? expectedPhysicalObjectIdentity,
        string recycleBinDirectory,
        string? relativeSubfolder,
        TimeProvider timeProvider,
        out string recycledPath,
        out string reason)
    {
        recycledPath = string.Empty;
        reason = string.Empty;

        if (string.IsNullOrWhiteSpace(recycleBinDirectory))
        {
            reason = "Recycling was skipped because no recycle bin is configured.";
            return RecycleFileOutcome.Blocked;
        }

        try
        {
            var roots = allowedRoots.ToList();
            if (!TryValidateMutationTarget(
                    filePath,
                    roots,
                    out var normalizedFile,
                    out reason))
            {
                return RecycleFileOutcome.Blocked;
            }

            var parentPath = Path.GetDirectoryName(normalizedFile);
            var fileName = Path.GetFileName(normalizedFile);
            if (string.IsNullOrWhiteSpace(parentPath)
                || string.IsNullOrWhiteSpace(fileName))
            {
                reason = "Recycling was blocked because the file's parent could not be pinned.";
                return RecycleFileOutcome.Blocked;
            }

            if (!TryResolveRecycleBinTarget(
                    normalizedFile,
                    recycleBinDirectory,
                    relativeSubfolder,
                    out var binTargetPath,
                    out reason))
            {
                return RecycleFileOutcome.Blocked;
            }

            PinnedDirectoryCreation.PinnedDirectoryAnchor parent;
            try
            {
                parent = PinnedDirectoryCreation.OpenPinnedHierarchyNoFollow(
                    parentPath,
                    createMissing: false);
            }
            catch (Exception exception) when (IsProvenMissingPathException(exception))
            {
                return RecycleFileOutcome.AlreadyGone;
            }

            using (parent)
            {
                var outcome = parent.TryOpenExistingFileForStableDeleteWithOutcome(
                    fileName,
                    out var openedEntry);
                using var entry = openedEntry;
                if (outcome == PinnedFileOpenOutcome.NotFound)
                {
                    if (!parent.VisiblePathMatches())
                    {
                        reason = "Recycling was blocked because the parent changed while absence was being proved.";
                        return RecycleFileOutcome.Blocked;
                    }

                    return RecycleFileOutcome.AlreadyGone;
                }

                if (outcome != PinnedFileOpenOutcome.Opened || entry == null)
                {
                    reason = "Recycling was blocked because the target could not be inspected safely.";
                    return RecycleFileOutcome.Blocked;
                }

                if (!string.IsNullOrWhiteSpace(expectedPhysicalObjectIdentity)
                    && !entry.MatchesObjectIdentity(expectedPhysicalObjectIdentity))
                {
                    reason =
                        "Recycling was blocked because the target physical generation no longer matches the tracked audiobook file.";
                    return RecycleFileOutcome.Blocked;
                }

                if (!TryValidateMutationTarget(
                        normalizedFile,
                        roots,
                        out var revalidatedFile,
                        out reason)
                    || !StringComparer.Ordinal.Equals(normalizedFile, revalidatedFile)
                    || !parent.VisiblePathMatches()
                    || !entry.VisiblePathMatches())
                {
                    reason = string.IsNullOrWhiteSpace(reason)
                        ? "Recycling was blocked because the validated path changed."
                        : reason;
                    return RecycleFileOutcome.Blocked;
                }

                using var binAnchor = PinnedDirectoryCreation.OpenPinnedHierarchyNoFollow(
                    binTargetPath,
                    createMissing: true);

                // Stamped BEFORE the rename, not after. Retention ages a file from when
                // it was deleted rather than from whenever it was last written, and a
                // rename keeps the original timestamps, so a file last written years ago
                // would be swept on the very next cycle. Readarr stamps for the same
                // reason at RecycleBinProvider.cs:56.
                //
                // Doing it after the rename leaves a window where the file is in the bin
                // carrying its old timestamp, and the daily sweep could see it there and
                // remove it immediately. The cost of stamping first is that a rename which
                // then fails leaves a modified timestamp on a file we were about to delete
                // anyway, which is the cheaper of the two.
                entry.SetLastWriteTimeUtc(timeProvider.GetUtcNow().UtcDateTime);

                var moved = TryPublishIntoRecycleBin(
                    entry,
                    binAnchor,
                    fileName,
                    out var publishedName,
                    out var nativeError,
                    out reason);
                if (!moved)
                {
                    return IsCrossDevice(nativeError)
                        ? RecycleFileOutcome.CrossVolume
                        : RecycleFileOutcome.Blocked;
                }

                recycledPath = Path.Join(binAnchor.FullPath, publishedName);
                return RecycleFileOutcome.Recycled;
            }
        }
        catch (Exception exception) when (exception is not (
            OperationCanceledException or OutOfMemoryException or StackOverflowException))
        {
            reason = $"Recycling failed safely: {exception.GetType().Name}.";
            return RecycleFileOutcome.Blocked;
        }
    }

    /// <summary>
    /// Work out which directory inside the bin this file belongs in, and refuse the whole
    /// operation if the bin turns out to contain the file itself. Recycling a file into a
    /// directory beneath itself would either loop or destroy the thing being preserved.
    /// </summary>
    private static bool TryResolveRecycleBinTarget(
        string normalizedFile,
        string recycleBinDirectory,
        string? relativeSubfolder,
        out string binTargetPath,
        out string reason)
    {
        binTargetPath = string.Empty;
        reason = string.Empty;

        if (!FileSystemPathIdentity.TryCanonicalizeStoredAbsolutePathForHost(
                recycleBinDirectory,
                out var normalizedBin,
                out var binReason))
        {
            reason = $"Recycling was blocked because the recycle bin path is unusable: {binReason}";
            return false;
        }

        if (PathIsWithin(normalizedFile, normalizedBin))
        {
            reason = "Recycling was blocked because the file already sits inside the recycle bin.";
            return false;
        }

        var safeSubfolder = SanitizeRecycleSubfolder(relativeSubfolder);
        binTargetPath = string.IsNullOrEmpty(safeSubfolder)
            ? normalizedBin
            : Path.Join(normalizedBin, safeSubfolder);
        return true;
    }

    /// <summary>
    /// Ordinal containment test over two already-canonicalized absolute paths. Ordinal
    /// rather than case-insensitive on purpose: the rest of this file fails closed on
    /// lexical aliases for the same reason TryValidateMutationTarget does, because two
    /// differently cased spellings are not proof of the same boundary.
    /// </summary>
    private static bool PathIsWithin(string candidate, string basePath)
    {
        if (StringComparer.Ordinal.Equals(candidate, basePath))
        {
            return true;
        }

        var boundary = basePath.EndsWith(Path.DirectorySeparatorChar)
            ? basePath
            : basePath + Path.DirectorySeparatorChar;
        return candidate.StartsWith(boundary, StringComparison.Ordinal);
    }

    /// <summary>
    /// Keep only plain directory names from the caller's suggested subfolder. Anything that
    /// could walk out of the bin, or that names a filesystem root, is dropped rather than
    /// rejected, because a bad subfolder should cost the operator a flatter bin and not a
    /// refused deletion.
    /// </summary>
    private static string SanitizeRecycleSubfolder(string? relativeSubfolder)
    {
        if (string.IsNullOrWhiteSpace(relativeSubfolder))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var segments = relativeSubfolder
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Trim())
            .Where(segment =>
                segment.Length > 0
                && segment != "."
                && segment != ".."
                && !segment.Contains(':', StringComparison.Ordinal)
                && segment.IndexOfAny(invalid) < 0)
            .ToList();

        return segments.Count == 0 ? string.Empty : Path.Join([.. segments]);
    }

    /// <summary>
    /// Rename the pinned entry into the bin without ever replacing something already there.
    /// A name that is taken gets a numbered suffix; the bin is an archive, so silently
    /// overwriting one recycled copy with another would lose the very file the operator
    /// might be coming back for.
    /// </summary>
    private static bool TryPublishIntoRecycleBin(
        PinnedDirectoryCreation.PinnedFileEntry entry,
        PinnedDirectoryCreation.PinnedDirectoryAnchor binAnchor,
        string fileName,
        out string publishedName,
        out int nativeError,
        out string reason)
    {
        publishedName = string.Empty;
        nativeError = 0;
        reason = string.Empty;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var attempt = 0; attempt < RecycleNameCollisionAttempts; attempt++)
        {
            var candidate = attempt == 0
                ? fileName
                : $"{stem}_{attempt}{extension}";

            PinnedDirectoryCreation.PinnedRenameAttempt result;
            try
            {
                result = entry.TryMoveToNoReplace(binAnchor, candidate);
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                // Only the Linux path returns the native error. The macOS and Windows
                // paths throw instead (PinnedDirectoryCreation.FilePublication.cs:76-81),
                // so without this a name already taken in the bin escapes the loop on the
                // first attempt and the operator can never delete a second file of that
                // name, and a bin on another volume reports a generic failure rather than
                // the cross-filesystem message.
                result = new PinnedDirectoryCreation.PinnedRenameAttempt(
                    false,
                    exception.NativeErrorCode);
            }

            if (result.Published)
            {
                publishedName = candidate;
                return true;
            }

            nativeError = result.NativeErrorCode;
            if (IsCrossDevice(nativeError))
            {
                reason =
                    "Recycling was blocked because the recycle bin is on a different filesystem from the library file.";
                return false;
            }

            if (!IsNameTaken(nativeError))
            {
                reason = $"Recycling was blocked because the move failed with error {nativeError}.";
                return false;
            }
        }

        reason =
            $"Recycling was blocked because {RecycleNameCollisionAttempts} names were already taken in the recycle bin.";
        return false;
    }
}
