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

        private Task UpdateJobStatusAsync(
            MoveJob job,
            MoveJobStatus status,
            string? error = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(job.LeaseOwner))
            {
                throw new MoveLeaseLostException(job.Id, job.LeaseGeneration);
            }

            return moveQueueService.UpdateJobStatusAsync(
                job.Id,
                job.LeaseOwner,
                job.LeaseGeneration,
                status,
                error,
                cancellationToken);
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
