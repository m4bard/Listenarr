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

using System.Globalization;
using System.IO.Compression;
using System.Text;
using Listenarr.Domain.SystemDiagnostics.Backups;
using Listenarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.SystemDiagnostics.Backups
{
    /// <summary>
    /// Writes and lists zip archives holding the SQLite database and config.json.
    /// </summary>
    public sealed class BackupService : IBackupService
    {
        /// <summary>
        /// Days an automatic backup is kept before the retention sweep removes it.
        /// Matches the *arr family default: Readarr src/NzbDrone.Core/Configuration/ConfigService.cs:398,
        /// Sonarr :422 and Prowlarr :179 all use 28.
        /// </summary>
        public const int DefaultRetentionDays = 28;

        private const string ArchivedDatabaseName = "listenarr.db";
        private const string ArchivedConfigName = "config.json";
        private const string InfoEntryName = "INFO";
        private const string StagingDirectoryName = ".staging";
        private const string BackupsDirectoryName = "backups";

        /// <summary>
        /// How many archives may share one second before naming gives up. Generous: the real number
        /// is one, and more than a handful means something is restarting in a loop.
        /// </summary>
        private const int MaxArchivesPerSecond = 64;

        /// <summary>
        /// The largest retention window the sweep will honour, in days. Anything above this is
        /// indistinguishable from "keep everything" and DateTime.AddDays throws well before int
        /// runs out. The settings screen never sends a value this large, but the settings endpoint
        /// takes the entity straight off the wire, so the bound has to exist here too.
        /// </summary>
        private const int MaxRetentionDays = 3650;

        /// <summary>
        /// How many manual archives are kept. Manual backups never expire by age, so without a
        /// count they grow without limit, and on an install running with authentication off
        /// anyone who can reach the port can ask for one. A full config volume would then fail
        /// the next pre-migration backup, which refuses the start.
        /// </summary>
        private const int MaxManualArchives = 20;

        private readonly ListenArrDbContext _db;
        private readonly IApplicationSettingsRepository _settingsRepository;
        private readonly IApplicationPathService _paths;
        private readonly IApplicationVersionService _versionService;
        private readonly ILogger<BackupService> _logger;

        public BackupService(
            ListenArrDbContext db,
            IApplicationSettingsRepository settingsRepository,
            IApplicationPathService paths,
            IApplicationVersionService versionService,
            ILogger<BackupService> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _versionService = versionService ?? throw new ArgumentNullException(nameof(versionService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public async Task<BackupArchive> CreateAsync(
            BackupTrigger trigger,
            CancellationToken cancellationToken = default)
        {
            // Deliberately reads no settings. The migration backup runs before pending schema
            // changes are applied, so a settings query could fail on a column this build expects
            // and the previous one never created. Retention is a separate call for that reason.
            var timestamp = DateTime.UtcNow;
            var version = ResolveVersion();
            var triggerDirectory = GetTriggerDirectory(trigger);

            CreateProtectedDirectory(triggerDirectory);

            // Staged inside the backups directory rather than the system temp directory so the
            // database copy lands on the same filesystem it will be zipped on. A database-sized
            // file in /tmp is a surprise on hosts where /tmp is a small tmpfs.
            var stagingPath = Path.Combine(GetBackupsRoot(), StagingDirectoryName, Guid.NewGuid().ToString("N"));
            var payloadPath = Path.Combine(stagingPath, "payload");
            CreateProtectedDirectory(payloadPath);

            string fileName;

            try
            {
                var stagedDatabase = Path.Combine(payloadPath, ArchivedDatabaseName);
                SqliteOnlineDatabaseCopier.CopyTo(_db.Database.GetDbConnection(), stagedDatabase);

                cancellationToken.ThrowIfCancellationRequested();

                await WriteInfoFileAsync(payloadPath, trigger, version, timestamp, cancellationToken);
                CopyStartupConfigIfPresent(payloadPath);

                cancellationToken.ThrowIfCancellationRequested();

                // Zipped beside the staged files and moved into place only once it is closed and
                // complete. Writing straight into the trigger directory would leave a half-written
                // file there under a name the listing accepts and the sweep is willing to delete,
                // and a container killed mid-zip would leave it there for good.
                var pendingArchive = Path.Combine(stagingPath, "pending" + BackupArchiveNaming.Extension);
                ZipFile.CreateFromDirectory(stagingPath + Path.DirectorySeparatorChar + "payload", pendingArchive, CompressionLevel.Optimal, false);
                fileName = PublishArchive(pendingArchive, triggerDirectory, version, timestamp);
            }
            finally
            {
                TryDeleteStaging(stagingPath);
            }

            var info = new FileInfo(Path.Combine(triggerDirectory, fileName));
            _logger.LogInformation(
                "Wrote {Trigger} backup {Name} ({SizeBytes} bytes)",
                trigger,
                fileName,
                info.Length);

            return new BackupArchive
            {
                Name = fileName,
                Trigger = trigger,
                SizeBytes = info.Length,
                CreatedAtUtc = timestamp
            };
        }

        /// <inheritdoc/>
        public Task<IReadOnlyList<BackupArchive>> ListAsync(CancellationToken cancellationToken = default)
        {
            var archives = new List<BackupArchive>();

            foreach (var trigger in Enum.GetValues<BackupTrigger>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                archives.AddRange(EnumerateArchives(trigger));
            }

            IReadOnlyList<BackupArchive> ordered = [.. archives.OrderByDescending(archive => archive.CreatedAtUtc)];
            return Task.FromResult(ordered);
        }

        /// <inheritdoc/>
        public async Task<int> ApplyRetentionAsync(CancellationToken cancellationToken = default)
        {
            var settings = await _settingsRepository.GetAsync(cancellationToken);
            var configuredDays = settings?.BackupRetentionDays ?? DefaultRetentionDays;

            // Clamped rather than trusted. The settings endpoint binds the entity straight off the
            // wire, and DateTime.AddDays throws on a value a long way below int.MaxValue, which
            // would turn a sweep into an exception on a backup that had already been written.
            var retentionDays = Math.Min(configuredDays, MaxRetentionDays);

            var removed = TrimManualArchives(cancellationToken);

            if (retentionDays <= 0)
            {
                _logger.LogDebug("Backup retention by age is off (retention days is {RetentionDays})", configuredDays);
                return removed;
            }

            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

            foreach (var trigger in Enum.GetValues<BackupTrigger>())
            {
                // Manual backups never expire by age, matching Readarr, which only sweeps when the
                // backup type is not Manual (src/NzbDrone.Core/Backup/BackupService.cs:85-88). An
                // operator who asked for a backup did not ask for it to expire. They are bounded by
                // count instead, which Readarr does not need because its UI requires a login.
                if (trigger == BackupTrigger.Manual)
                {
                    continue;
                }

                // Materialised before deleting: this enumerates the same directory it mutates.
                foreach (var archive in EnumerateArchives(trigger).ToList())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (archive.CreatedAtUtc >= cutoff)
                    {
                        continue;
                    }

                    Delete(trigger, archive, "expired");
                    removed++;
                }
            }

            if (removed > 0)
            {
                _logger.LogInformation("Retention sweep removed {Count} backup(s)", removed);
            }

            return removed;
        }

        /// <summary>
        /// Keeps the newest <see cref="MaxManualArchives"/> manual archives and removes the rest.
        /// </summary>
        private int TrimManualArchives(CancellationToken cancellationToken)
        {
            var manual = EnumerateArchives(BackupTrigger.Manual)
                .OrderByDescending(archive => archive.CreatedAtUtc)
                .Skip(MaxManualArchives)
                .ToList();

            foreach (var archive in manual)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Delete(BackupTrigger.Manual, archive, "beyond the manual backup limit");
            }

            return manual.Count;
        }

        private void Delete(BackupTrigger trigger, BackupArchive archive, string reason)
        {
            File.Delete(Path.Combine(GetTriggerDirectory(trigger), archive.Name));
            _logger.LogDebug("Deleted backup {Name} ({Reason})", archive.Name, reason);
        }

        /// <summary>
        /// Creates a directory that only the owner can enter, where the platform supports it.
        /// </summary>
        /// <remarks>
        /// Archives concentrate the API key, the SSL certificate password, indexer keys, download
        /// client credentials and the admin password hash into one portable file, so the default
        /// group and other bits are worth dropping even though the live database does not drop
        /// them. Same approach as FileSystem/CompatibilitySourceCleanupCoordinator.Quarantine.cs.
        /// </remarks>
        private static void CreateProtectedDirectory(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(path);
                return;
            }

            Directory.CreateDirectory(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        /// <summary>
        /// Moves a finished archive out of staging and into the trigger directory, choosing the
        /// first name in the second that is free.
        /// </summary>
        private static string PublishArchive(
            string pendingArchive,
            string triggerDirectory,
            string version,
            DateTime timestampUtc)
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(pendingArchive, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            for (var ordinal = 1; ordinal <= MaxArchivesPerSecond; ordinal++)
            {
                var candidate = BackupArchiveNaming.BuildFileName(version, timestampUtc, ordinal);
                var target = Path.Combine(triggerDirectory, candidate);

                // File.Move refuses to overwrite by default, so losing a race to the same name
                // costs one more attempt rather than clobbering somebody else's archive.
                try
                {
                    File.Move(pendingArchive, target);
                    return candidate;
                }
                catch (IOException) when (ordinal < MaxArchivesPerSecond)
                {
                    // Taken by another backup in this same second. Try the next ordinal.
                }
            }

            throw new IOException(
                $"Could not find a free backup file name after {MaxArchivesPerSecond} attempts.");
        }

        private IEnumerable<BackupArchive> EnumerateArchives(BackupTrigger trigger)
        {
            var directory = GetTriggerDirectory(trigger);
            if (!Directory.Exists(directory))
            {
                yield break;
            }

            foreach (var path in Directory.EnumerateFiles(directory, "*" + BackupArchiveNaming.Extension))
            {
                var name = Path.GetFileName(path);

                // Only files this code is known to have written are listed, and therefore only
                // those are ever eligible for deletion by the retention sweep.
                if (!BackupArchiveNaming.IsBackupArchive(name))
                {
                    continue;
                }

                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    continue;
                }

                yield return new BackupArchive
                {
                    Name = name,
                    Trigger = trigger,
                    SizeBytes = info.Length,
                    CreatedAtUtc = info.LastWriteTimeUtc
                };
            }
        }

        private string GetBackupsRoot() => _paths.ResolveFromConfig(BackupsDirectoryName);

        private string GetTriggerDirectory(BackupTrigger trigger)
            => _paths.ResolveFromConfig(
                BackupsDirectoryName,
                trigger.ToString().ToLowerInvariant());

        private string ResolveVersion()
        {
            var version = _versionService.Resolve();
            return string.Equals(version, "unknown", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : version;
        }

        private async Task WriteInfoFileAsync(
            string stagingPath,
            BackupTrigger trigger,
            string version,
            DateTime timestampUtc,
            CancellationToken cancellationToken)
        {
            // Readarr writes the same two facts at src/NzbDrone.Core/Backup/BackupService.cs:213-222.
            // They are what tells a restorer whether the archive predates a schema change.
            var builder = new StringBuilder();
            builder.AppendLine(string.IsNullOrEmpty(version) ? "unknown" : $"v{version}");
            builder.AppendLine(timestampUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC");
            builder.AppendLine(trigger.ToString());

            await File.WriteAllTextAsync(
                Path.Combine(stagingPath, InfoEntryName),
                builder.ToString(),
                cancellationToken);
        }

        private void CopyStartupConfigIfPresent(string stagingPath)
        {
            // config.json carries the API key and the SSL certificate password, and the database
            // carries indexer keys and download client credentials. Both are in the archive because
            // a backup that cannot restore a working instance is not a backup; Readarr archives its
            // equivalent Config.xml for the same reason (BackupService.cs:203-211). Nothing serves
            // these bytes over HTTP: see the note on BackupArchive and the controller.
            var configPath = _paths.ResolveFromConfig(ArchivedConfigName);
            if (!File.Exists(configPath))
            {
                _logger.LogDebug("No config.json found alongside the database; archiving the database only");
                return;
            }

            File.Copy(configPath, Path.Combine(stagingPath, ArchivedConfigName), true);
        }

        private void TryDeleteStaging(string stagingPath)
        {
            try
            {
                if (Directory.Exists(stagingPath))
                {
                    Directory.Delete(stagingPath, true);
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not remove backup staging directory");
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Could not remove backup staging directory");
            }
        }
    }
}
