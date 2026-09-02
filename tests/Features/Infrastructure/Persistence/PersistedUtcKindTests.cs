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
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// A UTC timestamp has to still be UTC after a round trip through SQLite.
///
/// These run against real SQLite on purpose. The rest of the suite uses the in-memory provider,
/// which hands back the same CLR instance and so preserves Kind for free. That is why this defect
/// class has no failing test anywhere else: in memory it cannot happen.
/// </summary>
[Trait("Area", "Persistence")]
[Trait("Name", "PersistedUtcKindTests")]
[Trait("Category", "DateTimeKind")]
public sealed class PersistedUtcKindTests : BaseTests
{
    private readonly string _databasePath =
        Path.Join(Path.GetTempPath(), "listenarr-tests", $"utc-kind-{Guid.NewGuid():N}.db");
    private DbContextOptions<ListenArrDbContext> _options = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        _options = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite($"Data Source={_databasePath};Pooling=False")
            .Options;
        await using var db = new ListenArrDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public override async Task DisposeAsync()
    {
        try { File.Delete(_databasePath); } catch (IOException) { }
        await base.DisposeAsync();
    }

    private async Task<int> SeedAudiobookAsync(string title)
    {
        await using var db = new ListenArrDbContext(_options);
        var audiobook = new Audiobook { Title = title };
        db.Audiobooks.Add(audiobook);
        await db.SaveChangesAsync();
        return audiobook.Id;
    }

    private async Task<AudiobookFile> WriteThenReadAsync(string path, string title, string identity)
    {
        var file = AudiobookFile.CreateUnresolved(path);
        file.AudiobookId = await SeedAudiobookAsync(title);
        file.ApplyPhysicalObjectIdentity(identity, DateTime.UtcNow);

        await using (var write = new ListenArrDbContext(_options))
        {
            write.AudiobookFiles.Add(file);
            await write.SaveChangesAsync();
        }

        await using var read = new ListenArrDbContext(_options);
        return await read.AudiobookFiles.AsNoTracking().SingleAsync(candidate => candidate.Path == path);
    }

    [Fact]
    [Trait("Scenario", "A persisted physical identity timestamp comes back as UTC")]
    public async Task PhysicalIdentityObservedAt_SurvivesTheRoundTripAsUtc()
    {
        var reloaded = await WriteThenReadAsync("/library/book.m4b", "Round Trip", "dev:1234:5678");

        Assert.True(reloaded.PhysicalIdentityObservedAtUtc.HasValue);
        Assert.Equal(DateTimeKind.Utc, reloaded.PhysicalIdentityObservedAtUtc!.Value.Kind);
    }

    [Fact]
    [Trait("Scenario", "Re-applying a persisted timestamp does not throw")]
    public async Task ReapplyingAPersistedIdentity_DoesNotThrow()
    {
        // The failure as it actually occurs. AudiobookFileService clones a loaded file by copying
        // PhysicalIdentityObservedAtUtc off it and handing it back to the same guard that produced
        // it, which is what a rescan of an already registered file does.
        var reloaded = await WriteThenReadAsync("/library/book2.m4b", "Reapply", "dev:1234:9999");

        var clone = AudiobookFile.CreateUnresolved(reloaded.Path);
        var exception = Record.Exception(() => clone.ApplyPhysicalObjectIdentity(
            reloaded.PhysicalObjectIdentity!,
            reloaded.PhysicalIdentityObservedAtUtc!.Value));

        Assert.Null(exception);
    }

    [Fact]
    [Trait("Scenario", "A row written before the converter existed still comes back as UTC")]
    public async Task TextWithNoOffset_WrittenDirectly_StillComesBackAsUtc()
    {
        // The stored text carries no offset, so the fix has to be on the read side to cover rows
        // that are already in the database. Written through raw SQL to be sure this does not
        // depend on the write path.
        var audiobookId = await SeedAudiobookAsync("Legacy");
        await using (var raw = new ListenArrDbContext(_options))
        {
            var connection = raw.Database.GetDbConnection();
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText =
                "insert into AudiobookFiles (AudiobookId, Path, PhysicalObjectIdentity, " +
                "PhysicalIdentityVersion, PhysicalIdentityObservedAtUtc, CreatedAt) values " +
                $"({audiobookId}, '/library/legacy.m4b', 'dev:1:legacy', 1, " +
                "'2026-09-01 20:47:07.303729', '2026-01-01 00:00:00')";
            await command.ExecuteNonQueryAsync();
        }

        await using var read = new ListenArrDbContext(_options);
        var reloaded = await read.AudiobookFiles.AsNoTracking()
            .SingleAsync(candidate => candidate.Path == "/library/legacy.m4b");

        Assert.Equal(DateTimeKind.Utc, reloaded.PhysicalIdentityObservedAtUtc!.Value.Kind);
    }
}
