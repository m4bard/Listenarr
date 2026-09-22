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

using Listenarr.Domain.SystemDiagnostics.Backups;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Listenarr.Infrastructure.SystemDiagnostics.Backups
{
    /// <summary>
    /// Decides whether to take a backup before EF Core applies pending migrations, and takes it.
    /// </summary>
    /// <remarks>
    /// Listenarr applies migrations unattended on every start
    /// (DependencyInjection/InfrastructureStartupCompositionExtensions.cs, ctx.Database.Migrate()),
    /// and at least one migration cannot be reversed: the Down method of
    /// Persistence/Migrations/20251208001000_ConvertLegacyDelimitedToJsonArraysMigrationColumns.cs
    /// tells the reader to restore from a database backup instead. Pulling a new image is a
    /// one-command action, so the copy has to be taken by the application itself.
    ///
    /// The *arr family takes its automatic backup at the equivalent moment, immediately before an
    /// update is installed (Readarr src/NzbDrone.Core/Update/InstallUpdateService.cs:144). That hook
    /// does not exist for a container, where the new binary is already running by the time anything
    /// of ours executes, so the last safe moment here is the one before Migrate().
    /// </remarks>
    public static class PreMigrationBackup
    {
        /// <summary>Configuration key that turns the pre-migration backup off.</summary>
        public const string EnabledConfigurationKey = "Listenarr:BackupBeforeMigrations";

        /// <summary>Environment variable that turns the pre-migration backup off.</summary>
        public const string EnabledEnvironmentVariable = "LISTENARR_BACKUP_BEFORE_MIGRATIONS";

        /// <summary>
        /// Reads whether the pre-migration backup is enabled. It is on unless explicitly disabled.
        /// </summary>
        /// <remarks>
        /// This is deliberately not an application setting. A failed backup refuses the migration
        /// and therefore refuses the start, and an operator in that state cannot reach the settings
        /// screen to change anything. The escape hatch has to be reachable from the compose file.
        /// </remarks>
        public static bool IsEnabled(IConfiguration? configuration)
        {
            var environmentOverride = Environment.GetEnvironmentVariable(EnabledEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(environmentOverride)
                && bool.TryParse(environmentOverride.Trim(), out var parsedEnvironment))
            {
                return parsedEnvironment;
            }

            return configuration?.GetValue<bool?>(EnabledConfigurationKey) ?? true;
        }

        /// <summary>
        /// Takes a backup when there is a schema change about to be applied.
        /// </summary>
        /// <param name="pendingMigrations">Migrations EF Core is about to apply.</param>
        /// <param name="appliedMigrations">Migrations already recorded in the database.</param>
        /// <param name="enabled">Result of <see cref="IsEnabled"/>.</param>
        /// <param name="backupService">
        /// Writes the archive. Evaluated only when a backup is actually going to be taken.
        /// </param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns>
        /// The archive that was written, or <see langword="null"/> when nothing was pending, the
        /// database is being created by this start, or the backup is disabled.
        /// </returns>
        /// <exception cref="Exception">
        /// Whatever the backup failed with. It is not swallowed: the caller must not migrate.
        /// Readarr behaves the same way, in that a throw out of Backup(BackupType.Update) is not
        /// among the exceptions handled in InstallUpdateService.Execute (:288-315), so the update
        /// it was protecting does not proceed. Refusing is also what this startup path already does
        /// for any other migration failure.
        /// </exception>
        public static async Task<BackupArchive?> ProtectAsync(
            IReadOnlyCollection<string> pendingMigrations,
            IReadOnlyCollection<string> appliedMigrations,
            bool enabled,
            Lazy<IBackupService> backupService,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(pendingMigrations);
            ArgumentNullException.ThrowIfNull(appliedMigrations);
            ArgumentNullException.ThrowIfNull(backupService);

            if (pendingMigrations.Count == 0)
            {
                // Every start would otherwise write an archive, and the overwhelming majority of
                // starts change nothing. Readarr is equally selective, backing up on update rather
                // than on boot.
                return null;
            }

            if (appliedMigrations.Count == 0)
            {
                // A database with no history is one this start is about to create. There is no
                // prior state to lose, so a copy of an empty file helps nobody. Readarr reaches the
                // same place from the other direction: a first install has no update to back up
                // before.
                Log.Logger.Debug(
                    "[Startup] Creating a new database, so no pre-migration backup is needed");
                return null;
            }

            if (!enabled)
            {
                Log.Logger.Warning(
                    "[Startup] {Count} pending migration(s) will be applied without a backup because {Key} is disabled",
                    pendingMigrations.Count,
                    EnabledConfigurationKey);
                return null;
            }

            Log.Logger.Information(
                "[Startup] Backing up the database before applying {Count} pending migration(s)",
                pendingMigrations.Count);

            // Resolved only now. A caller that never needs a backup is never asked for the service,
            // which keeps this helper usable from a provider that has only persistence in it.
            var archive = await backupService.Value.CreateAsync(BackupTrigger.Migration, cancellationToken);

            Log.Logger.Information("[Startup] Pre-migration backup written as {Name}", archive.Name);
            return archive;
        }
    }
}
