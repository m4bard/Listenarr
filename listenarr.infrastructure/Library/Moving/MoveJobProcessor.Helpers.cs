/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Domain.Common;

namespace Listenarr.Infrastructure.Library.Moving
{
    internal partial class MoveJobProcessor
    {
        private static MoveLeaseToken CreateLeaseToken(MoveJob job)
        {
            if (string.IsNullOrWhiteSpace(job.LeaseOwner) || job.LeaseGeneration <= 0)
            {
                throw new MoveLeaseLostException(job.Id, job.LeaseGeneration);
            }

            return new MoveLeaseToken(job.LeaseOwner, job.LeaseGeneration);
        }

        private static string? ResolveSourceCleanupBoundary(
            string source,
            IEnumerable<RootFolder> rootFolders,
            FileSystemPathSemantics semantics)
        {
            var containingRoots = new List<(string Path, int CanonicalLength)>();
            foreach (var root in rootFolders)
            {
                try
                {
                    if (!FileSystemPathIdentity.IsSameOrInside(source, root.Path, semantics))
                    {
                        continue;
                    }

                    containingRoots.Add((
                        root.Path,
                        FileSystemPathIdentity.Canonicalize(root.Path, semantics.Syntax).Length));
                }
                catch (Exception exception) when (exception is
                    ArgumentException or InvalidOperationException or NotSupportedException or PathTooLongException)
                {
                    // A legacy root from another host must not block an otherwise valid
                    // move or broaden cleanup beyond a root whose identity is known.
                }
            }

            return containingRoots
                .OrderByDescending(root => root.CanonicalLength)
                .Select(root => root.Path)
                .FirstOrDefault();
        }

        private static bool IsFilesystemRoot(string path, FileSystemPathSemantics semantics)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            return !string.IsNullOrWhiteSpace(root)
                && FileSystemPathIdentity.AreEquivalent(fullPath, root, semantics);
        }
    }
}
