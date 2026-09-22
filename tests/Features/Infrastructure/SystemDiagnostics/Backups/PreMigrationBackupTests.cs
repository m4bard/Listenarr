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
using Listenarr.Tests.Common;
using Microsoft.Extensions.Configuration;

namespace Listenarr.Tests.Features.Infrastructure.SystemDiagnostics.Backups;

[Trait("Name", "PreMigrationBackupTests")]
[Trait("Category", "Backup")]
public sealed class PreMigrationBackupTests : BaseTests
{
    private static readonly string[] OneApplied = ["20260101000000_Something"];
    private static readonly string[] OnePending = ["20260825021432_AddWeakStorageVerifiedCleanup"];
    private static readonly string[] None = [];

    private static Mock<IBackupService> StubBackupService()
    {
        var mock = new Mock<IBackupService>();
        mock.Setup(service => service.CreateAsync(It.IsAny<BackupTrigger>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackupArchive
            {
                Name = "listenarr_backup_v1.0.0_2026.01.01_00.00.00.zip",
                Trigger = BackupTrigger.Migration,
                SizeBytes = 1,
                CreatedAtUtc = DateTime.UtcNow
            });
        return mock;
    }

    [Fact]
    [Trait("Scenario", "BacksUpBeforeASchemaChange")]
    public async Task ProtectAsync_TakesAMigrationBackup_WhenAPopulatedDatabaseHasPendingMigrations()
    {
        // Given a database that already has history and a schema change about to be applied
        var backupService = StubBackupService();

        // When the startup path asks for protection
        var archive = await PreMigrationBackup.ProtectAsync(
            OnePending,
            OneApplied,
            enabled: true,
            new Lazy<IBackupService>(() => backupService.Object));

        // Then a backup is taken, and it is recorded as a migration backup
        Assert.NotNull(archive);
        backupService.Verify(
            service => service.CreateAsync(BackupTrigger.Migration, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    [Trait("Scenario", "OrdinaryRestartTakesNothing")]
    public async Task ProtectAsync_TakesNothing_WhenNoMigrationsArePending()
    {
        // Given the control for the case above: the same populated database, nothing pending,
        // which is what an ordinary restart looks like
        var backupService = StubBackupService();
        var lazy = new Lazy<IBackupService>(() => backupService.Object);

        // When the startup path asks for protection
        var archive = await PreMigrationBackup.ProtectAsync(None, OneApplied, enabled: true, lazy);

        // Then nothing is written, and the service was never even resolved
        Assert.Null(archive);
        Assert.False(lazy.IsValueCreated);
        backupService.Verify(
            service => service.CreateAsync(It.IsAny<BackupTrigger>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    [Trait("Scenario", "FirstInstallTakesNothing")]
    public async Task ProtectAsync_TakesNothing_WhenTheDatabaseIsBeingCreatedByThisStart()
    {
        // Given a first install: everything is pending because nothing has ever been applied
        var backupService = StubBackupService();
        var lazy = new Lazy<IBackupService>(() => backupService.Object);

        // When the startup path asks for protection
        var archive = await PreMigrationBackup.ProtectAsync(OnePending, None, enabled: true, lazy);

        // Then nothing is written: there is no prior state to lose
        Assert.Null(archive);
        Assert.False(lazy.IsValueCreated);
    }

    [Fact]
    [Trait("Scenario", "OperatorOptOut")]
    public async Task ProtectAsync_TakesNothing_WhenTheOperatorHasDisabledIt()
    {
        // Given the escape hatch turned off, with a schema change genuinely pending
        var backupService = StubBackupService();

        // When the startup path asks for protection
        var archive = await PreMigrationBackup.ProtectAsync(
            OnePending,
            OneApplied,
            enabled: false,
            new Lazy<IBackupService>(() => backupService.Object));

        // Then the migration proceeds unprotected, because that is what was asked for
        Assert.Null(archive);
        backupService.Verify(
            service => service.CreateAsync(It.IsAny<BackupTrigger>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    [Trait("Scenario", "FailureRefusesTheMigration")]
    public async Task ProtectAsync_PropagatesTheFailure_SoTheCallerDoesNotMigrate()
    {
        // Given a backup that cannot be written, which is what a full or read-only config
        // directory looks like
        var backupService = new Mock<IBackupService>();
        backupService
            .Setup(service => service.CreateAsync(It.IsAny<BackupTrigger>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("No space left on device"));

        // When the startup path asks for protection
        // Then the failure is not swallowed. This is the assertion that fails if anyone wraps the
        // call in a try/catch: the irreversible migration must not run without the copy.
        await Assert.ThrowsAsync<IOException>(() => PreMigrationBackup.ProtectAsync(
            OnePending,
            OneApplied,
            enabled: true,
            new Lazy<IBackupService>(() => backupService.Object)));
    }

    [Fact]
    [Trait("Scenario", "EnabledByDefault")]
    public void IsEnabled_IsOn_WhenNothingSaysOtherwise()
    {
        // Given no configuration at all
        // When the flag is read
        // Then it is on: protection is not something an operator has to discover
        Assert.True(PreMigrationBackup.IsEnabled(null));
        Assert.True(PreMigrationBackup.IsEnabled(new ConfigurationBuilder().Build()));
    }

    [Fact]
    [Trait("Scenario", "ConfigurationOptOut")]
    public void IsEnabled_IsOff_WhenConfigurationSaysFalse()
    {
        // Given the documented configuration key set to false
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PreMigrationBackup.EnabledConfigurationKey] = "false"
            })
            .Build();

        // When the flag is read
        // Then it is off, and the control above shows the same reader returns true without it
        Assert.False(PreMigrationBackup.IsEnabled(configuration));
    }
}
