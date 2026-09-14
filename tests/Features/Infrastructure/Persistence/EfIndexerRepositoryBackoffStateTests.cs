/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// The two persistence properties the ladder depends on and that no ladder test can catch: that a
/// block outlives the process, and that saving indexer settings does not quietly undo one.
/// </summary>
[Trait("Area", "Persistence")]
[Trait("Name", "EfIndexerRepositoryBackoffStateTests")]
[Trait("Category", "Infrastructure")]
public sealed class EfIndexerRepositoryBackoffStateTests : BaseTests
{
    private static readonly DateTime BlockedTill = new(2026, 9, 14, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FirstFailure = new(2026, 9, 14, 6, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Method", "UpdateBackoffStateAsync")]
    [Trait("Scenario", "SurvivesRestart")]
    public async Task UpdateBackoffStateAsync_WrittenState_SurvivesRebuildingTheContext()
    {
        // Given
        var databasePath = Path.Combine(FileService.GetTempPath(), $"backoff-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";

        await using (var context = await CreateContextAsync(connectionString))
        {
            context.Indexers.Add(Indexer(1));
            await context.SaveChangesAsync();

            var repository = new EfIndexerRepository(context);
            await repository.UpdateBackoffStateAsync(
                1,
                new IndexerBackoffState(FirstFailure, FirstFailure.AddHours(2), 7, BlockedTill, "Timeout"));
        }

        // When: a fresh context over the same file, which is what a restart looks like
        await using var reopened = await CreateContextAsync(connectionString);
        var stored = await reopened.Indexers.AsNoTracking().SingleAsync(i => i.Id == 1);

        // Then: an outage that causes restarts would otherwise reset the ladder on every one of
        // them, and the feature would do nothing in exactly the situation it exists for.
        Assert.Equal(7, stored.EscalationLevel);
        Assert.Equal(BlockedTill, stored.DisabledTill);
        Assert.Equal(FirstFailure, stored.InitialFailure);
        Assert.Equal(FirstFailure.AddHours(2), stored.MostRecentFailure);
        Assert.Equal("Timeout", stored.LastFailureReason);

        SqliteConnection.ClearAllPools();
    }

    [Fact]
    [Trait("Method", "UpdateAsync")]
    [Trait("Scenario", "StaleConfigSaveDoesNotClobberStatus")]
    public async Task UpdateAsync_WithAStaleDto_LeavesTheBackoffColumnsAlone()
    {
        // Given: the operator opened the indexer settings form before the indexer was blocked, so
        // the entity they are about to save carries the backoff columns as they stood back then.
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = await CreateContextAsync(connection);

        context.Indexers.Add(Indexer(1));
        await context.SaveChangesAsync();

        var repository = new EfIndexerRepository(context);
        var staleDto = await context.Indexers.AsNoTracking().SingleAsync(i => i.Id == 1);
        context.ChangeTracker.Clear();

        await repository.UpdateBackoffStateAsync(
            1,
            new IndexerBackoffState(FirstFailure, FirstFailure, 4, BlockedTill, "HttpStatus"));

        // When: the stale save lands
        staleDto.Name = "Renamed by the operator";
        await repository.UpdateAsync(staleDto);

        // Then: the rename took, and the cooldown did not silently lift. UpdateAsync is a whole-row
        // overwrite via CurrentValues.SetValues, which is why the status writer has a path of its
        // own rather than reading, mutating and calling this.
        context.ChangeTracker.Clear();
        var stored = await context.Indexers.AsNoTracking().SingleAsync(i => i.Id == 1);
        Assert.Equal("Renamed by the operator", stored.Name);
        Assert.Equal(4, stored.EscalationLevel);
        Assert.Equal(BlockedTill, stored.DisabledTill);
        Assert.Equal("HttpStatus", stored.LastFailureReason);
    }

    [Fact]
    [Trait("Method", "UpdateBackoffStateAsync")]
    [Trait("Scenario", "ConfigurationUntouched")]
    public async Task UpdateBackoffStateAsync_DoesNotDisturbConfigurationColumns()
    {
        // Given: the control for the test above, in the other direction.
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = await CreateContextAsync(connection);

        context.Indexers.Add(Indexer(1));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // When
        var repository = new EfIndexerRepository(context);
        await repository.UpdateBackoffStateAsync(
            1,
            new IndexerBackoffState(FirstFailure, FirstFailure, 2, BlockedTill, "Timeout"));

        // Then
        var stored = await context.Indexers.AsNoTracking().SingleAsync(i => i.Id == 1);
        Assert.Equal("Torznab One", stored.Name);
        Assert.Equal("https://indexer.invalid", stored.Url);
        Assert.Equal("secret-api-key", stored.ApiKey);
        Assert.True(stored.IsEnabled);
        Assert.Equal(2, stored.EscalationLevel);
    }

    [Fact]
    [Trait("Method", "UpdateBackoffStateAsync")]
    [Trait("Scenario", "ClearsABlock")]
    public async Task UpdateBackoffStateAsync_HealthyState_ClearsEveryColumn()
    {
        // Given
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = await CreateContextAsync(connection);

        context.Indexers.Add(Indexer(1));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var repository = new EfIndexerRepository(context);
        await repository.UpdateBackoffStateAsync(
            1,
            new IndexerBackoffState(FirstFailure, FirstFailure, 3, BlockedTill, "Timeout"));

        // When
        await repository.UpdateBackoffStateAsync(1, IndexerBackoffState.Healthy);

        // Then
        var stored = await context.Indexers.AsNoTracking().SingleAsync(i => i.Id == 1);
        Assert.Equal(0, stored.EscalationLevel);
        Assert.Null(stored.DisabledTill);
        Assert.Null(stored.InitialFailure);
        Assert.Null(stored.MostRecentFailure);
        Assert.Null(stored.LastFailureReason);
    }

    private static async Task<ListenArrDbContext> CreateContextAsync(string connectionString)
    {
        var context = new ListenArrDbContext(
            new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseSqlite(connectionString)
                .Options);
        await context.Database.MigrateAsync();
        return context;
    }

    private static async Task<ListenArrDbContext> CreateContextAsync(SqliteConnection connection)
    {
        var context = new ListenArrDbContext(
            new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseSqlite(connection)
                .Options);
        await context.Database.MigrateAsync();
        return context;
    }

    private static Indexer Indexer(int id) =>
        new IndexerBuilder()
            .WithId(id)
            .WithName("Torznab One")
            .WithType("Torrent")
            .WithImplementation("Torznab")
            .WithUrl("https://indexer.invalid")
            .WithApiKey("secret-api-key")
            .Build();
}
