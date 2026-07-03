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
using System.Text.RegularExpressions;

namespace Listenarr.Domain.Common
{
    public static partial class FileUtils
    {
        private static readonly Regex WindowsDriveRootPattern = new("^[A-Za-z]:[\\\\/]", RegexOptions.Compiled);
        private static readonly Regex WindowsReservedDeviceNamePattern = new(
            "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Validates and normalizes a user-provided directory path that Listenarr will store or create.
        /// This must not be used for externally reported download-client source paths, where whitespace
        /// and other path identity details must be preserved exactly as reported by the client.
        /// </summary>
        public static bool TryNormalizeUserProvidedDirectoryPathForCurrentOs(
            string? path,
            out string normalizedPath,
            out string reason,
            bool allowFileSystemRoot = false,
            bool rejectParentTraversal = false) =>
            TryNormalizeUserProvidedDirectoryPathForOs(
                path,
                OperatingSystem.IsWindows(),
                out normalizedPath,
                out reason,
                allowFileSystemRoot,
                rejectParentTraversal);

        // The explicit OS parameter lets tests verify Windows and Unix validation rules
        // from any host. Production callers should use TryNormalizeUserProvidedDirectoryPathForCurrentOs.
        public static bool TryNormalizeUserProvidedDirectoryPathForOs(
            string? path,
            bool isWindows,
            out string normalizedPath,
            out string reason,
            bool allowFileSystemRoot = false,
            bool rejectParentTraversal = false)
        {
            normalizedPath = string.Empty;
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(path))
            {
                reason = "Path is required.";
                return false;
            }

            var candidate = path;
            if (candidate.IndexOf('\0') >= 0)
            {
                reason = "Path contains invalid characters.";
                return false;
            }

            if (isWindows)
            {
                return TryNormalizeWindowsUserProvidedDirectoryPath(
                    candidate,
                    out normalizedPath,
                    out reason,
                    allowFileSystemRoot,
                    rejectParentTraversal);
            }

            return TryNormalizeUnixUserProvidedDirectoryPath(
                candidate,
                out normalizedPath,
                out reason,
                allowFileSystemRoot,
                rejectParentTraversal);
        }

        private static bool TryNormalizeWindowsUserProvidedDirectoryPath(
            string path,
            out string normalizedPath,
            out string reason,
            bool allowFileSystemRoot,
            bool rejectParentTraversal)
        {
            normalizedPath = string.Empty;
            reason = string.Empty;

            // Windows accepts \ or / as the current drive root. Root-folder configuration may
            // intentionally use that boundary, but concrete destinations must still reject it.
            if (IsWindowsCurrentDriveRoot(path))
            {
                if (!allowFileSystemRoot)
                {
                    reason = "Path cannot be the filesystem root.";
                    return false;
                }

                try
                {
                    normalizedPath = OperatingSystem.IsWindows()
                        ? Path.GetFullPath(path)
                        : path.Replace('/', '\\');
                    return true;
                }
                catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
                {
                    normalizedPath = string.Empty;
                    reason = "Path is not valid for this operating system.";
                    return false;
                }
            }

            var rootLength = GetWindowsRootLength(path);
            if (rootLength <= 0)
            {
                reason = "Path must be an absolute directory path.";
                return false;
            }

            var pathWithoutRoot = path[rootLength..];
            if (string.IsNullOrWhiteSpace(pathWithoutRoot.Trim('/', '\\')) && !allowFileSystemRoot)
            {
                reason = "Path cannot be the filesystem root.";
                return false;
            }

            if (!ValidateWindowsDirectorySegments(pathWithoutRoot, rejectParentTraversal, out reason))
            {
                return false;
            }

            try
            {
                normalizedPath = OperatingSystem.IsWindows()
                    ? Path.GetFullPath(path)
                    : NormalizeWindowsDirectoryPathSyntax(path);

                if (IsWindowsRootOnly(normalizedPath) && !allowFileSystemRoot)
                {
                    normalizedPath = string.Empty;
                    reason = "Path cannot be the filesystem root.";
                    return false;
                }

                return true;
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
            {
                normalizedPath = string.Empty;
                reason = "Path is not valid for this operating system.";
                return false;
            }
        }

        private static bool TryNormalizeUnixUserProvidedDirectoryPath(
            string path,
            out string normalizedPath,
            out string reason,
            bool allowFileSystemRoot,
            bool rejectParentTraversal)
        {
            normalizedPath = string.Empty;
            reason = string.Empty;

            if (!path.StartsWith("/", StringComparison.Ordinal))
            {
                reason = "Path must be an absolute directory path.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(path.Trim('/')) && !allowFileSystemRoot)
            {
                reason = "Path cannot be the filesystem root.";
                return false;
            }

            if (ContainsCurrentDirectorySegment(path, '/'))
            {
                reason = "Path cannot contain current directory segments.";
                return false;
            }

            if (rejectParentTraversal && ContainsParentDirectorySegment(path, '/'))
            {
                reason = "Path cannot traverse to a parent directory.";
                return false;
            }

            try
            {
                normalizedPath = OperatingSystem.IsWindows()
                    ? NormalizeUnixDirectoryPathSyntax(path)
                    : Path.GetFullPath(path);

                if (IsUnixRootOnly(normalizedPath) && !allowFileSystemRoot)
                {
                    normalizedPath = string.Empty;
                    reason = "Path cannot be the filesystem root.";
                    return false;
                }

                return true;
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
            {
                normalizedPath = string.Empty;
                reason = "Path is not valid for this operating system.";
                return false;
            }
        }

        /// <summary>
        /// Detects values that visually look like absolute paths after accidental leading whitespace.
        /// Do not trim user-provided paths before validation because Unix path-segment whitespace is valid.
        /// </summary>
        public static bool HasLeadingWhitespaceBeforeRootedPath(string? path)
        {
            if (string.IsNullOrEmpty(path) || !char.IsWhiteSpace(path[0]))
            {
                return false;
            }

            var trimmedStart = path.TrimStart();
            return Path.IsPathRooted(trimmedStart)
                || IsWindowsCurrentDriveRoot(trimmedStart)
                || GetWindowsRootLength(trimmedStart) > 0;
        }

        private static bool IsWindowsCurrentDriveRoot(string path)
        {
            return path.Length == 1 && (path[0] is '\\' or '/');
        }

        private static int GetWindowsRootLength(string path)
        {
            if (WindowsDriveRootPattern.IsMatch(path))
            {
                return 3;
            }

            if (!path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith("//", StringComparison.Ordinal))
            {
                return 0;
            }

            var parts = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return 0;
            }

            var index = 2;
            var separatorsSeen = 0;
            while (index < path.Length && separatorsSeen < 2)
            {
                if (path[index] is '\\' or '/')
                {
                    separatorsSeen++;
                }

                index++;
            }

            return index;
        }

        private static bool ValidateWindowsDirectorySegments(string pathWithoutRoot, bool rejectParentTraversal, out string reason)
        {
            reason = string.Empty;
            var segments = pathWithoutRoot.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                if (segment == ".")
                {
                    reason = "Path cannot contain current directory segments.";
                    return false;
                }

                if (segment == ".." && rejectParentTraversal)
                {
                    reason = "Path cannot traverse to a parent directory.";
                    return false;
                }

                if (segment == "..")
                {
                    continue;
                }

                if (segment.Any(IsInvalidWindowsDirectorySegmentCharacter))
                {
                    reason = "Path contains invalid characters.";
                    return false;
                }

                if (segment.EndsWith(' ') || segment.EndsWith('.'))
                {
                    reason = "Path segments cannot end with a space or period on Windows.";
                    return false;
                }

                var stem = segment.Split('.', 2)[0];
                if (WindowsReservedDeviceNamePattern.IsMatch(stem))
                {
                    reason = "Path contains a reserved Windows device name.";
                    return false;
                }
            }

            return true;
        }

        private static bool IsInvalidWindowsDirectorySegmentCharacter(char character)
        {
            return character < 32 || character is '<' or '>' or ':' or '"' or '|' or '?' or '*';
        }

        public static bool ContainsParentDirectorySegment(string path, params char[] separators)
        {
            if (string.IsNullOrEmpty(path) || separators.Length == 0)
            {
                return false;
            }

            return path.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment == "..");
        }

        private static bool ContainsCurrentDirectorySegment(string path, params char[] separators)
        {
            if (string.IsNullOrEmpty(path) || separators.Length == 0)
            {
                return false;
            }

            return path.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment == ".");
        }

        private static string NormalizeWindowsDirectoryPathSyntax(string path)
        {
            var normalizedPath = path.Replace('/', '\\');
            var rootLength = GetWindowsRootLength(normalizedPath);
            var root = normalizedPath[..rootLength];
            var pathWithoutRoot = normalizedPath[rootLength..];
            var segments = new List<string>();

            foreach (var segment in pathWithoutRoot.Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == ".")
                {
                    continue;
                }

                if (segment == "..")
                {
                    if (segments.Count > 0)
                    {
                        segments.RemoveAt(segments.Count - 1);
                    }

                    continue;
                }

                segments.Add(segment);
            }

            return segments.Count == 0
                ? root.TrimEnd('\\')
                : root.TrimEnd('\\') + "\\" + string.Join("\\", segments);
        }

        private static bool IsWindowsRootOnly(string path)
        {
            var pathWithWindowsSeparators = path.Replace('/', '\\');
            var normalizedPath = pathWithWindowsSeparators.TrimEnd('\\');
            if (Regex.IsMatch(normalizedPath, "^[A-Za-z]:$"))
            {
                return true;
            }

            var rootLength = GetWindowsRootLength(pathWithWindowsSeparators);
            if (rootLength <= 0)
            {
                return false;
            }

            var root = pathWithWindowsSeparators[..rootLength].TrimEnd('\\');
            return string.Equals(normalizedPath, root, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeUnixDirectoryPathSyntax(string path)
        {
            var segments = new List<string>();
            foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == ".")
                {
                    continue;
                }

                if (segment == "..")
                {
                    if (segments.Count > 0)
                    {
                        segments.RemoveAt(segments.Count - 1);
                    }

                    continue;
                }

                segments.Add(segment);
            }

            return segments.Count == 0 ? "/" : "/" + string.Join("/", segments);
        }

        private static bool IsUnixRootOnly(string path)
        {
            return string.Equals(path.TrimEnd('/'), string.Empty, StringComparison.Ordinal)
                || string.Equals(path.TrimEnd('/'), "/", StringComparison.Ordinal);
        }
    }
}
