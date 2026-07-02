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

using Listenarr.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

public sealed class MoveJobSchemaRepairTests : IAsyncLifetime
{
    private readonly string _databasePath =
        Path.Join(Path.GetTempPath(), "listenarr-tests", $"move-schema-repair-{Guid.NewGuid():N}.db");

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task EnsureSourcePathColumn_WhenMoveJobsTableMissedMigration_AddsNullableColumn()
    {
        await CreateMoveJobsTableWithoutSourcePathAsync();
        var options = CreateOptions();

        await using (var db = new ListenArrDbContext(options))
        {
            var repaired = MoveJobSchemaRepair.EnsureSourcePathColumn(db);

            Assert.True(repaired);
            Assert.DoesNotContain(
                "SourcePath",
                MoveJobSchemaRepair.GetMissingMoveJobColumns(db));
        }

        await using var verified = new ListenArrDbContext(options);
        var persistence = new EfMoveQueuePersistence(new TestDbContextFactory(options));
        var activeJob = await persistence.GetActiveByKeyAsync("42:/library/book");

        Assert.NotNull(activeJob);
        Assert.Null(activeJob.SourcePath);
    }

    [Fact]
    public async Task EnsureSourcePathColumn_WhenColumnAlreadyExists_DoesNotMutateSchema()
    {
        var options = CreateOptions();
        await using (var created = new ListenArrDbContext(options))
        {
            await created.Database.EnsureCreatedAsync();
        }

        await using var db = new ListenArrDbContext(options);
        var repaired = MoveJobSchemaRepair.EnsureSourcePathColumn(db);

        Assert.False(repaired);
        Assert.DoesNotContain(
            "SourcePath",
            MoveJobSchemaRepair.GetMissingMoveJobColumns(db));
    }

    private DbContextOptions<ListenArrDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite($"Data Source={_databasePath};Pooling=False")
            .Options;

    private async Task CreateMoveJobsTableWithoutSourcePathAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE ""MoveJobs"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_MoveJobs"" PRIMARY KEY,
    ""AudiobookId"" INTEGER NOT NULL,
    ""RequestedPath"" TEXT NULL,
    ""EnqueuedAt"" TEXT NOT NULL,
    ""Status"" TEXT NOT NULL,
    ""Error"" TEXT NULL,
    ""AttemptCount"" INTEGER NOT NULL,
    ""UpdatedAt"" TEXT NULL,
    ""ActiveDeduplicationKey"" TEXT NULL
);
CREATE UNIQUE INDEX ""IX_MoveJobs_ActiveDeduplicationKey""
    ON ""MoveJobs"" (""ActiveDeduplicationKey"")
    WHERE ""ActiveDeduplicationKey"" IS NOT NULL;
INSERT INTO ""MoveJobs"" (
    ""Id"",
    ""AudiobookId"",
    ""RequestedPath"",
    ""EnqueuedAt"",
    ""Status"",
    ""AttemptCount"",
    ""ActiveDeduplicationKey""
) VALUES (
    '11111111-1111-1111-1111-111111111111',
    42,
    '/library/book',
    '2026-07-02T14:33:02Z',
    'Queued',
    0,
    '42:/library/book'
);";
        await command.ExecuteNonQueryAsync();
    }

    private sealed class TestDbContextFactory(DbContextOptions<ListenArrDbContext> options)
        : IDbContextFactory<ListenArrDbContext>
    {
        public ListenArrDbContext CreateDbContext() => new(options);

        public Task<ListenArrDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
