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
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Library
{
    public partial class RootFoldersController
    {
        /// <summary>
        /// Enqueues a background scan of a root folder to find audio files not in the library.
        /// Returns a jobId; subscribe to the realtime "UnmatchedScanComplete" event for completion notification.
        /// </summary>
        [HttpPost("{id}/scan-unmatched")]
        public async Task<IActionResult> ScanUnmatched(int id)
        {
            _filesystemMutationGate.EnsureReady();

            var folder = await _service.GetByIdAsync(id);
            if (folder == null) return NotFound(new { message = "Root folder not found" });

            var storage = await _storageHealthResolver.ResolveAsync(folder);
            if (!storage.CanScanFilesystem)
            {
                return Conflict(new
                {
                    message = storage.Message
                        ?? "The root folder cannot be scanned in its current storage state.",
                    code = "root_folder_scan_unavailable"
                });
            }

            var jobId = await _unmatchedQueue.EnqueueAsync(folder.Path);
            return Ok(new { jobId = jobId.ToString() });
        }

        /// <summary>
        /// Returns the status and results of a previously enqueued unmatched scan job.
        /// </summary>
        [HttpGet("unmatched-results/{jobId}")]
        public IActionResult GetUnmatchedResults(Guid jobId)
        {
            if (!_unmatchedQueue.TryGetJob(jobId, out var job) || job == null)
                return NotFound(new { message = "Scan job not found" });

            return Ok(new
            {
                jobId = job.Id.ToString(),
                status = job.Status,
                error = UnmatchedScanPublicError.FromInternal(job.Error),
                warnings = job.Warnings,
                items = job.Results ?? new List<UnmatchedFileResult>()
            });
        }

        /// <summary>
        /// Returns the cached results from the last completed unmatched scan for a root folder.
        /// Returns an empty list if no scan has been run yet this session.
        /// </summary>
        [HttpGet("{id}/unmatched")]
        public async Task<IActionResult> GetSavedUnmatched(int id)
        {
            var folder = await _service.GetByIdAsync(id);
            if (folder == null) return NotFound(new { message = "Root folder not found" });

            if (_unmatchedQueue.TryGetLastJobForPath(folder.Path, out var job) && job != null)
            {
                // Filter out items already added to the library since the scan ran
                var trackedPathSemantics = await ResolveFolderSemanticsAsync(folder);
                var trackedFromFiles = await _fileRepository.GetAllFilePathsAsync(
                    trackedPathSemantics);
                var trackedFromAudiobooks = (await _audiobookRepository.GetAllAsync())
                    .Where(a => a.FilePath != null)
                    .Select(a => a.FilePath!)
                    .ToList();
                var tracked = trackedFromFiles
                    .Concat(trackedFromAudiobooks)
                    .Select(path => TryCanonicalizePathForComparison(path, trackedPathSemantics))
                    .Where(path => path != null)
                    .Select(path => path!)
                    .ToHashSet(trackedPathSemantics.Comparer);

                var filtered = (job.Results ?? new List<UnmatchedFileResult>())
                    .Where(result =>
                    {
                        var canonicalPath = TryCanonicalizePathForComparison(
                            result.FullPath,
                            trackedPathSemantics);
                        return canonicalPath != null
                            && !tracked.Contains(canonicalPath)
                            && _fileSystem.FileExists(result.FullPath);
                    })
                    .ToList();

                return Ok(new
                {
                    lastScannedAt = job.CompletedAt,
                    warnings = job.Warnings,
                    items = filtered
                });
            }

            return Ok(new
            {
                lastScannedAt = (DateTime?)null,
                warnings = new List<string>(),
                items = new List<UnmatchedFileResult>()
            });
        }
    }
}
