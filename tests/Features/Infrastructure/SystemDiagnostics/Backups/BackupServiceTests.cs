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
using System.Runtime.Versioning;
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
        // WAL on purpose. Without it the journal-mode assertion below would pass whether or not
        // the copier normalises anything, since a fresh SQLite file is already in delete mode.
        _context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
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

        // And the copy's journal mode was normalised rather than inherited. The source was put
        // into WAL above, so without the pragma this reads "wal"; the assertion that there is no
        // sidecar is not enough on its own, because closing the connection removes one anyway.
        await using var journalMode = restored.CreateCommand();
        journalMode.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("delete", (await journalMode.ExecuteScalarAsync() as string)?.ToLowerInvariant());
        Assert.False(File.Exists(Path.Combine(extracted, "listenarr.db-wal")));

        // And nothing is left behind. Staging holds an unzipped copy of the whole database, so a
        // leak here quietly accumulates one of those per backup.
        var staging = paths.ResolveFromConfig("backups", ".staging");
        Assert.Empty(Directory.Exists(staging) ? Directory.GetFileSystemEntries(staging) : []);
    }

    [LinuxFact]
    [SupportedOSPlatform("linux")]
    [Trait("Scenario", "ArchivesAreNotWorldReadable")]
    public async Task CreateAsync_WritesAnArchiveOnlyItsOwnerCanRead()
    {
        // Given a live database, on a platform with Unix permissions
        var (service, paths) = CreateService();

        // When a backup is taken
        var archive = await service.CreateAsync(BackupTrigger.Manual);
        var directory = TriggerDirectory(paths, BackupTrigger.Manual);
        var archivePath = Path.Combine(directory, archive.Name);

        // Then neither the archive nor the directory holding it carries group or other bits. One
        // file concentrates the API key, the SSL certificate password, indexer keys, download
        // client credentials and the admin password hash.
        var groupAndOther =
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

        Assert.Equal(UnixFileMode.None, File.GetUnixFileMode(archivePath) & groupAndOther);
        Assert.Equal(UnixFileMode.None, File.GetUnixFileMode(directory) & groupAndOther);

        // Control: the owner can still read and write it, so this is not simply "no permissions".
        Assert.True(File.GetUnixFileMode(archivePath).HasFlag(UnixFileMode.UserRead));
        Assert.True(File.GetUnixFileMode(archivePath).HasFlag(UnixFileMode.UserWrite));
    }

    [Fact]
    [Trait("Scenario", "RetentionSurvivesAnAbsurdSetting")]
    public async Task ApplyRetentionAsync_DoesNotThrow_WhenRetentionIsBeyondWhatADateCanHold()
    {
        // Given a retention value the settings screen would never send but the settings endpoint
        // will happily persist, since it binds the entity straight off the wire
        var (service, paths) = CreateService(retentionDays: int.MaxValue);
        var migration = await service.CreateAsync(BackupTrigger.Migration);
        var path = Path.Combine(TriggerDirectory(paths, BackupTrigger.Migration), migration.Name);

        // When the sweep runs
        var removed = await service.ApplyRetentionAsync();

        // Then it clamps instead of throwing out of DateTime.AddDays, and keeps everything, which
        // is what an enormous window means
        Assert.Equal(0, removed);
        Assert.True(File.Exists(path));
    }

    [Fact]
    [Trait("Scenario", "ManualBackupsAreBoundedByRefusal")]
    public async Task CreateAsync_RefusesAManualBackup_WhenTheLimitIsAlreadyMet_AndKeepsEveryExistingOne()
    {
        // Given as many manual archives as are kept
        var (service, paths) = CreateService();
        var directory = TriggerDirectory(paths, BackupTrigger.Manual);
        for (var index = 0; index < BackupService.MaxManualArchives; index++)
        {
            await service.CreateAsync(BackupTrigger.Manual);
        }

        var before = Directory.GetFiles(directory).Select(Path.GetFileName).Order().ToList();

        // When another is asked for
        var refusal = await Assert.ThrowsAsync<BackupLimitReachedException>(
            () => service.CreateAsync(BackupTrigger.Manual));

        // Then it is refused, and every existing archive survives. Making room by deleting the
        // oldest would hand anyone who can reach the endpoint the power to destroy an operator's
        // backups, and on a default install that is anyone on the network.
        Assert.Equal(BackupService.MaxManualArchives, refusal.Limit);
        Assert.Equal(before, Directory.GetFiles(directory).Select(Path.GetFileName).Order());

        // Control: the limit is on manual archives only, so the pre-migration backup, which is the
        // one that protects an irreversible schema change, is never refused because of it.
        var migration = await service.CreateAsync(BackupTrigger.Migration);
        Assert.True(File.Exists(
            Path.Combine(TriggerDirectory(paths, BackupTrigger.Migration), migration.Name)));
    }

    [Fact]
    [Trait("Scenario", "NothingEverRemovesAManualBackup")]
    public async Task ApplyRetentionAsync_NeverRemovesAManualBackup_HoweverOldItIs()
    {
        // Given a manual archive from years ago
        var (service, paths) = CreateService(retentionDays: 1);
        var manual = await service.CreateAsync(BackupTrigger.Manual);
        var path = Path.Combine(TriggerDirectory(paths, BackupTrigger.Manual), manual.Name);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddYears(-5));

        // And an automatic one of the same age, as the control
        var migration = await service.CreateAsync(BackupTrigger.Migration);
        var migrationPath = Path.Combine(TriggerDirectory(paths, BackupTrigger.Migration), migration.Name);
        File.SetLastWriteTimeUtc(migrationPath, DateTime.UtcNow.AddYears(-5));

        // When the sweep runs
        var removed = await service.ApplyRetentionAsync();

        // Then only the automatic one goes, whatever the retention window says
        Assert.Equal(1, removed);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(migrationPath));
    }

    [Fact]
    [Trait("Scenario", "NoPartialArchiveIsEverListed")]
    public async Task CreateAsync_PublishesTheArchiveOnlyOnceItIsComplete()
    {
        // Given a live database
        var (service, paths) = CreateService();
        var directory = TriggerDirectory(paths, BackupTrigger.Manual);

        // And one backup already taken, so the directory exists to be watched
        await service.CreateAsync(BackupTrigger.Manual);
        var alreadyThere = Directory.GetFiles(directory).Length;

        // And a watcher on the trigger directory, recording every event while the next one runs
        var events = new List<WatcherChangeTypes>();
        using (var watcher = new FileSystemWatcher(directory, "*" + BackupArchiveNaming.Extension))
        {
            watcher.Created += (_, _) => { lock (events) { events.Add(WatcherChangeTypes.Created); } };
            watcher.Changed += (_, _) => { lock (events) { events.Add(WatcherChangeTypes.Changed); } };
            watcher.Renamed += (_, _) => { lock (events) { events.Add(WatcherChangeTypes.Renamed); } };
            watcher.EnableRaisingEvents = true;

            // When a backup is taken
            await service.CreateAsync(BackupTrigger.Manual);

            // FileSystemWatcher delivers on a background thread, so give it a moment to drain
            // before reading. A miss here would make this test weaker, never falsely strict.
            await Task.Delay(500);
        }

        Assert.Equal(alreadyThere + 1, Directory.GetFiles(directory).Length);
        var archive = Path.GetFileName(
            Directory.GetFiles(directory).OrderByDescending(File.GetLastWriteTimeUtc).First());

        // Then the archive appeared in one step and was never written to in place. A zip streamed
        // straight into this directory raises Created followed by a run of Changed as it grows,
        // which is the window in which a listing sees a partial file and a killed process leaves
        // one behind for good.
        lock (events)
        {
            Assert.DoesNotContain(WatcherChangeTypes.Changed, events);
            Assert.NotEmpty(events);
        }

        using var opened = ZipFile.OpenRead(Path.Combine(directory, archive!));
        Assert.Contains("listenarr.db", opened.Entries.Select(entry => entry.FullName));
    }

    [LinuxFact]
    [SupportedOSPlatform("linux")]
    [Trait("Scenario", "EveryDirectoryLevelIsPrivate")]
    public async Task CreateAsync_MakesEveryDirectoryItCreatesPrivate_NotJustTheLeaf()
    {
        // Given a live database
        var (service, paths) = CreateService();

        // When a backup is taken
        await service.CreateAsync(BackupTrigger.Manual);

        // Then backups, .staging and the trigger directory are all private. Passing a mode to
        // Directory.CreateDirectory applies it to the leaf only, so creating backups/manual in one
        // call leaves backups itself at the umask, with a world-traversable path to the archive.
        var groupAndOther =
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

        foreach (var directory in new[]
        {
            paths.ResolveFromConfig("backups"),
            paths.ResolveFromConfig("backups", ".staging"),
            TriggerDirectory(paths, BackupTrigger.Manual)
        })
        {
            Assert.True(Directory.Exists(directory), $"{directory} was not created.");
            Assert.Equal(UnixFileMode.None, File.GetUnixFileMode(directory) & groupAndOther);
        }
    }

    [Fact]
    [Trait("Scenario", "AbandonedStagingIsSweptUp")]
    public async Task CreateAsync_RemovesStagingLeftBehindByARunThatDidNotFinish()
    {
        // Given the wreckage of an earlier backup killed mid-copy: an uncompressed copy of the
        // whole database in staging, which the finally block never got to run over
        var (service, paths) = CreateService();
        var stagingRoot = paths.ResolveFromConfig("backups", ".staging");
        Directory.CreateDirectory(stagingRoot);

        var abandoned = Path.Combine(stagingRoot, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        Directory.CreateDirectory(Path.Combine(abandoned, "payload"));
        await File.WriteAllTextAsync(Path.Combine(abandoned, "payload", "listenarr.db"), "a whole database");
        Directory.SetCreationTimeUtc(abandoned, DateTime.UtcNow.AddHours(-3));

        var recent = Path.Combine(stagingRoot, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        Directory.CreateDirectory(recent);

        // When the next backup runs
        await service.CreateAsync(BackupTrigger.Manual);

        // Then the abandoned one is gone. Nothing else ever looks in here, so a restart loop during
        // a migration would otherwise leave one database-sized copy per attempt.
        Assert.False(Directory.Exists(abandoned));

        // And a directory young enough to belong to a backup still running is left alone, which is
        // the case that must come out differently
        Assert.True(Directory.Exists(recent));
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

    [Theory]
    [Trait("Scenario", "RetentionCanBeDisabled")]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ApplyRetentionAsync_KeepsEverything_WhenRetentionIsZeroOrLess(int retentionDays)
    {
        // Given retention switched off and an archive far past any plausible window. Negative as
        // well as zero, because nothing stops the settings endpoint persisting a negative and the
        // entity comment promises that zero or less disables the sweep.
        var (service, paths) = CreateService(retentionDays);
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
