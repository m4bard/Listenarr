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

using Listenarr.Api.Attributes;
using Listenarr.Domain.SystemDiagnostics.Backups;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.SystemDiagnostics
{
    /// <summary>
    /// Lists backup archives and takes new ones on request.
    /// </summary>
    /// <remarks>
    /// There is deliberately no endpoint that serves an archive's bytes and none that reveals where
    /// one is stored. An archive contains the database and config.json, so it holds indexer keys,
    /// download client credentials, the admin password hash and the API key. Readarr can afford a
    /// download route because its equivalent sits behind [Authorize(Policy="UI")]
    /// (src/Readarr.Http/Frontend/StaticResourceController.cs:12, reached through BackupFileMapper),
    /// and Listenarr ships with authentication off by default
    /// (Startup/ListenarrStartupTasks.cs warns about exactly this). Archives land in the config
    /// directory, which is a mounted volume on a container install, so an operator already has the
    /// file without Listenarr handing it out over HTTP.
    /// </remarks>
    [ApiController]
    [Route("api/v{version:apiVersion}/system/backup")]
    [Tags("System")]
    [RequireAdminOrApiKey]
    public class BackupController : ControllerBase
    {
        private readonly IBackupService _backupService;
        private readonly ILogger<BackupController> _logger;

        public BackupController(IBackupService backupService, ILogger<BackupController> logger)
        {
            _backupService = backupService;
            _logger = logger;
        }

        /// <summary>
        /// Lists every backup archive on disk, newest first.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<BackupArchive>), StatusCodes.Status200OK)]
        public async Task<ActionResult<IReadOnlyList<BackupArchive>>> GetBackups(
            CancellationToken cancellationToken)
        {
            return Ok(await _backupService.ListAsync(cancellationToken));
        }

        /// <summary>
        /// Takes a backup now and applies the retention sweep.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(BackupArchive), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<BackupArchive>> CreateBackup(CancellationToken cancellationToken)
        {
            try
            {
                var archive = await _backupService.CreateAsync(BackupTrigger.Manual, cancellationToken);
                await _backupService.ApplyRetentionAsync(cancellationToken);

                // No Location header: there is no endpoint that serves the archive, by design.
                return StatusCode(StatusCodes.Status201Created, archive);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Backup failed while writing to the config directory");
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { error = "Backup failed. Check that the config directory is writable and has free space." });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Backup failed because the config directory is not writable");
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { error = "Backup failed. The config directory is not writable." });
            }
        }
    }
}
