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
using System.IO.Compression;
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.SystemDiagnostics.Backups;

[Trait("Name", "BackupServiceTests")]
[Trait("Category", "Backup")]
public sealed class BackupServiceTests : BaseTests
{
    private const string ProbeValue = "a row that must survive the copy";

    private ListenArrDbContext? _context;

    public override async Task DisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();
        await base.DisposeAsync();
    }

    private (BackupService Service, IApplicationPathService Paths) CreateService(
        int retentionDays = BackupService.DefaultRetentionDays,
        string version = "9.9.9")
    {
        var contentRoot = FileService.GetTempPath();
        var paths = new ApplicationPathService(contentRoot);

        var databasePath = paths.ResolveFromConfig("database", "listenarr.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        var options = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        _context = new ListenArrDbContext(options);

        // A real table with a real row, so the assertion can be "the data came back", not
        // "a file exists". An empty file would satisfy the weaker assertion either way.
        _context.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "BackupProbe" ("Id" INTEGER PRIMARY KEY, "Value" TEXT NOT NULL);
            """);
        _context.Database.ExecuteSqlRaw(
            $"""INSERT INTO "BackupProbe" ("Id", "Value") VALUES (1, '{ProbeValue}');""");

        var settings = new Mock<IApplicationSettingsRepository>();
        settings
            .Setup(repository => repository.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationSettings { BackupRetentionDays = retentionDays });

        var versionService = new Mock<IApplicationVersionService>();
        versionService.Setup(service => service.Resolve()).Returns(version);

        var service = new BackupService(
            _context,
            settings.Object,
            paths,
            versionService.Object,
            Mock.Of<ILogger<BackupService>>());

        return (service, paths);
    }

    private static string TriggerDirectory(IApplicationPathService paths, BackupTrigger trigger)
        => paths.ResolveFromConfig("backups", trigger.ToString().ToLowerInvariant());

    [Fact]
    [Trait("Scenario", "ArchiveHoldsAUsableDatabase")]
    public async Task CreateAsync_ArchivesADatabaseTheDataCanBeReadBackOutOf()
    {
        // Given a live database carrying a row, and a config.json beside it
        var (service, paths) = CreateService();
        await File.WriteAllTextAsync(paths.ResolveFromConfig("config.json"), """{"ApiKey":"secret"}""");

        // When a manual backup is taken
        var archive = await service.CreateAsync(BackupTrigger.Manual);

        // Then the archive holds the database, the config and the version note
        var archivePath = Path.Combine(TriggerDirectory(paths, BackupTrigger.Manual), archive.Name);
        var extracted = Path.Combine(FileService.GetTempPath(), "extracted");
        ZipFile.ExtractToDirectory(archivePath, extracted);

        Assert.True(File.Exists(Path.Combine(extracted, "listenarr.db")));
        Assert.True(File.Exists(Path.Combine(extracted, "config.json")));
        Assert.Contains("v9.9.9", await File.ReadAllTextAsync(Path.Combine(extracted, "INFO")));

        // And the archived database is a working database with the row in it. This is the
        // assertion that separates a real online copy from a zero-byte file: a truncated or
        // absent copy cannot answer the query.
        await using var restored = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(extracted, "listenarr.db"),
                Pooling = false
            }.ToString());
        await restored.OpenAsync();
        await using var command = restored.CreateCommand();
        command.CommandText = """SELECT "Value" FROM "BackupProbe" WHERE "Id" = 1;""";
        Assert.Equal(ProbeValue, await command.ExecuteScalarAsync() as string);

        // And it needs no sidecar: the extracted file stands alone, with no -wal beside it.
        Assert.False(File.Exists(Path.Combine(extracted, "listenarr.db-wal")));
    }

    [Fact]
    [Trait("Scenario", "TriggersDoNotShareADirectory")]
    public async Task CreateAsync_KeepsManualAndMigrationArchivesApart()
    {
        // Given a live database
        var (service, paths) = CreateService();

        // When one backup of each kind is taken
        var manual = await service.CreateAsync(BackupTrigger.Manual);
        var migration = await service.CreateAsync(BackupTrigger.Migration);

        // Then each lands under its own trigger, which is what keeps the sweep from touching
        // the manual ones
        Assert.True(File.Exists(
            Path.Combine(TriggerDirectory(paths, BackupTrigger.Manual), manual.Name)));
        Assert.True(File.Exists(
            Path.Combine(TriggerDirectory(paths, BackupTrigger.Migration), migration.Name)));
    }

    [Fact]
    [Trait("Scenario", "SameSecondCollision")]
    public async Task CreateAsync_WritesBothArchives_WhenTwoAreTakenInTheSameSecond()
    {
        // Given a live database
        var (service, paths) = CreateService();

        // When two backups are taken back to back, which lands them in the same second
        var first = await service.CreateAsync(BackupTrigger.Migration);
        var second = await service.CreateAsync(BackupTrigger.Migration);

        // Then both exist under distinct names, rather than the second one failing
        Assert.NotEqual(first.Name, second.Name);
        var directory = TriggerDirectory(paths, BackupTrigger.Migration);
        Assert.True(File.Exists(Path.Combine(directory, first.Name)));
        Assert.True(File.Exists(Path.Combine(directory, second.Name)));

        // And both are still recognised as ours, so the sweep can see them
        var listed = await service.ListAsync();
        Assert.Equal(2, listed.Count);
    }

    [Fact]
    [Trait("Scenario", "RetentionSparesManualBackups")]
    public async Task ApplyRetentionAsync_RemovesExpiredMigrationArchives_ButNeverManualOnes()
    {
        // Given one expired archive of each kind, aged past the retention window
        var (service, paths) = CreateService(retentionDays: 28);
        var manual = await service.CreateAsync(BackupTrigger.Manual);
        var migration = await service.CreateAsync(BackupTrigger.Migration);

        var manualPath = Path.Combine(TriggerDirectory(paths, BackupTrigger.Manual), manual.Name);
        var migrationPath = Path.Combine(TriggerDirectory(paths, BackupTrigger.Migration), migration.Name);
        var longAgo = DateTime.UtcNow.AddDays(-40);
        File.SetLastWriteTimeUtc(manualPath, longAgo);
        File.SetLastWriteTimeUtc(migrationPath, longAgo);

        // When the sweep runs
        var removed = await service.ApplyRetentionAsync();

        // Then only the automatic one goes. The two outcomes differ in shape, so a sweep that did
        // nothing fails on the first assertion and one that deleted indiscriminately fails on the
        // second.
        Assert.Equal(1, removed);
        Assert.False(File.Exists(migrationPath));
        Assert.True(File.Exists(manualPath));
    }

    [Fact]
    [Trait("Scenario", "RetentionKeepsFreshArchives")]
    public async Task ApplyRetentionAsync_KeepsArchivesInsideTheWindow()
    {
        // Given an archive that is only a day old against a 28 day window
        var (service, paths) = CreateService(retentionDays: 28);
        var migration = await service.CreateAsync(BackupTrigger.Migration);
        var path = Path.Combine(TriggerDirectory(paths, BackupTrigger.Migration), migration.Name);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-1));

        // When the sweep runs
        var removed = await service.ApplyRetentionAsync();

        // Then it survives, which is the control for the expiry case above
        Assert.Equal(0, removed);
        Assert.True(File.Exists(path));
    }

    [Fact]
    [Trait("Scenario", "RetentionCanBeDisabled")]
    public async Task ApplyRetentionAsync_KeepsEverything_WhenRetentionIsZero()
    {
        // Given retention switched off and an archive far past any plausible window
        var (service, paths) = CreateService(retentionDays: 0);
        var migration = await service.CreateAsync(BackupTrigger.Migration);
        var path = Path.Combine(TriggerDirectory(paths, BackupTrigger.Migration), migration.Name);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddYears(-5));

        // When the sweep runs
        var removed = await service.ApplyRetentionAsync();

        // Then nothing is deleted
        Assert.Equal(0, removed);
        Assert.True(File.Exists(path));
    }

    [Fact]
    [Trait("Scenario", "ForeignFilesAreNeverDeleted")]
    public async Task ApplyRetentionAsync_LeavesFilesThisCodeDidNotWrite()
    {
        // Given an old archive of ours and two old files that are not ours, in the same directory
        var (service, paths) = CreateService(retentionDays: 1);
        var migration = await service.CreateAsync(BackupTrigger.Migration);
        var directory = TriggerDirectory(paths, BackupTrigger.Migration);

        var foreignZip = Path.Combine(directory, "an_operators_own_archive.zip");
        var foreignText = Path.Combine(directory, "notes.txt");
        await File.WriteAllTextAsync(foreignZip, "not a listenarr backup");
        await File.WriteAllTextAsync(foreignText, "keep me");

        var longAgo = DateTime.UtcNow.AddDays(-40);
        foreach (var path in new[] { Path.Combine(directory, migration.Name), foreignZip, foreignText })
        {
            File.SetLastWriteTimeUtc(path, longAgo);
        }

        // When the sweep runs
        var removed = await service.ApplyRetentionAsync();

        // Then only ours goes. The sweep deletes files, so this is the case that must come out
        // differently from the one above.
        Assert.Equal(1, removed);
        Assert.False(File.Exists(Path.Combine(directory, migration.Name)));
        Assert.True(File.Exists(foreignZip));
        Assert.True(File.Exists(foreignText));
    }

    [Fact]
    [Trait("Scenario", "ListingIsNewestFirstAndOnlyOurs")]
    public async Task ListAsync_ReturnsOurArchivesNewestFirst()
    {
        // Given two archives of ours and one file that is not
        var (service, paths) = CreateService();
        var older = await service.CreateAsync(BackupTrigger.Migration);
        var newer = await service.CreateAsync(BackupTrigger.Manual);

        var migrationDirectory = TriggerDirectory(paths, BackupTrigger.Migration);
        await File.WriteAllTextAsync(Path.Combine(migrationDirectory, "stray.zip"), "not ours");
        File.SetLastWriteTimeUtc(
            Path.Combine(migrationDirectory, older.Name),
            DateTime.UtcNow.AddDays(-2));

        // When the listing is read
        var listed = await service.ListAsync();

        // Then it carries exactly our two, newest first
        Assert.Equal([newer.Name, older.Name], listed.Select(entry => entry.Name));
        Assert.Equal(BackupTrigger.Manual, listed[0].Trigger);
        Assert.All(listed, entry => Assert.True(entry.SizeBytes > 0));
    }

    [Fact]
    [Trait("Scenario", "ArchivesStayOutOfTheWebRoot")]
    public async Task CreateAsync_WritesUnderTheConfigRoot_NotUnderTheServedWebRoot()
    {
        // Given a live database
        var (service, paths) = CreateService();

        // When a backup is taken
        var archive = await service.CreateAsync(BackupTrigger.Manual);
        var archivePath = Path.Combine(TriggerDirectory(paths, BackupTrigger.Manual), archive.Name);

        // Then it sits under the config root and nowhere near wwwroot, which is the only directory
        // the app serves files from. An archive holds the API key and every stored credential, so
        // landing inside a served directory would publish them.
        Assert.StartsWith(paths.ConfigRootPath, Path.GetFullPath(archivePath), StringComparison.Ordinal);
        Assert.DoesNotContain(paths.WwwRootPath, Path.GetFullPath(archivePath), StringComparison.Ordinal);

        // And the archive metadata carries no filesystem path for a caller to learn from
        Assert.DoesNotContain(Path.DirectorySeparatorChar, archive.Name);
    }
}
