/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Tests.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Listenarr.Tests.Features.Infrastructure.Migrations;

/// <summary>
/// PreferredReleaseShape is the first enum column on QualityProfiles, so this checks both
/// halves of adding one: that an upgrade leaves every profile that already exists behaving
/// exactly as it did, and that a chosen value survives a write and a fresh read.
/// </summary>
[Trait("Name", "PreferredReleaseShapeUpgradeTests")]
[Trait("Category", "Infrastructure")]
public sealed class PreferredReleaseShapeUpgradeTests : BaseTests
{
    private const string MigrationBeforeReleaseShape = "20260825021432_AddWeakStorageVerifiedCleanup";

    [Fact]
    public async Task UpgradingAnExistingDatabase_LeavesEveryProfileWithNoPreference()
    {
        var databasePath = Path.Join(
            Path.GetTempPath(),
            "listenarr-tests",
            $"release-shape-upgrade-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var options = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;

        try
        {
            await using (var db = new ListenArrDbContext(options))
            {
                var migrator = db.GetService<IMigrator>();
                await migrator.MigrateAsync(MigrationBeforeReleaseShape);

                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "QualityProfiles" (
                        "Name", "Qualities", "PreferredFormats", "PreferredLanguages",
                        "MustContain", "MustNotContain", "PreferredWords",
                        "MinimumSize", "MaximumSize", "MinimumSeeders", "MinimumScore",
                        "MaximumAge", "IsDefault", "PreferNewerReleases",
                        "CreatedAt", "UpdatedAt")
                    VALUES (
                        {"Profile from before the column existed"}, {"[]"}, {"[\"m4b\"]"}, {"[\"English\"]"},
                        {"[]"}, {"[]"}, {"[]"},
                        {0}, {0}, {1}, {0},
                        {0}, {0}, {1},
                        {DateTime.UtcNow}, {DateTime.UtcNow});
                    """);

                await migrator.MigrateAsync();
            }

            await using (var upgraded = new ListenArrDbContext(options))
            {
                var profile = await upgraded.QualityProfiles.AsNoTracking().SingleAsync();
                Assert.Equal(ReleaseShapePreference.NoPreference, profile.PreferredReleaseShape);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Theory]
    [InlineData(ReleaseShapePreference.NoPreference)]
    [InlineData(ReleaseShapePreference.PreferIndividual)]
    [InlineData(ReleaseShapePreference.PreferBundle)]
    public async Task EachPreference_SurvivesAWriteAndAFreshRead(ReleaseShapePreference preference)
    {
        var databasePath = Path.Join(
            Path.GetTempPath(),
            "listenarr-tests",
            $"release-shape-roundtrip-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var options = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;

        try
        {
            await using (var db = new ListenArrDbContext(options))
            {
                await db.Database.MigrateAsync();
                db.QualityProfiles.Add(new QualityProfile
                {
                    Name = "Round trip",
                    PreferredReleaseShape = preference
                });
                await db.SaveChangesAsync();
            }

            await using (var reread = new ListenArrDbContext(options))
            {
                var profile = await reread.QualityProfiles.AsNoTracking().SingleAsync();
                Assert.Equal(preference, profile.PreferredReleaseShape);

                // Stored as the enum's integer value, which is what makes NoPreference = 0 the
                // same thing as the column default an upgrade writes.
                var stored = await reread.Database
                    .SqlQuery<int>($"SELECT \"PreferredReleaseShape\" AS \"Value\" FROM \"QualityProfiles\"")
                    .SingleAsync();
                Assert.Equal((int)preference, stored);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
}
