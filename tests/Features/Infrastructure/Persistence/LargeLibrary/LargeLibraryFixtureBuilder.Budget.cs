/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Microsoft.Data.Sqlite;

namespace Listenarr.Tests.Features.Infrastructure.Persistence.LargeLibrary;

/// <summary>How much hourly usage history each budgeted indexer holds.</summary>
public enum IndexerRequestUsageVariant
{
    /// <summary>The hours the hourly prune keeps: the current hour and the 48 before it.</summary>
    Pruned,

    /// <summary>A year of hours, as a database whose prune never ran would hold.</summary>
    Unpruned
}

/// <summary>
/// The daily request budget's rows: indexers with a daily limit, the settings row holding the
/// interactive reserve, and an hourly usage row for every indexer and hour of the chosen
/// history. Nothing here runs unless a test asks after <see cref="BuildAsync"/>.
/// </summary>
public sealed partial class LargeLibraryFixtureBuilder
{
    public const int BudgetedIndexerCount = 5;
    public const int BudgetDailyRequestLimit = 1_000;
    public const int BudgetInteractiveReservePercent = 25;
    public const int PrunedUsageHoursPerIndexer = 49;
    public const int UnprunedUsageHoursPerIndexer = 8_760;

    // Hourly counts cycle 1..7, so a 25-hour window holds about 100 requests, well under the
    // daily limit.
    private const int UsageCountCycle = 7;

    /// <summary>
    /// Adds the budgeted indexers, the settings row and their usage history ending at the hour
    /// of <see cref="ClockUtc"/>, and returns the indexer ids.
    /// </summary>
    public async Task<IReadOnlyList<int>> AddIndexerRequestUsageAsync(
        LargeLibraryFixture fixture,
        IndexerRequestUsageVariant variant,
        CancellationToken cancellationToken = default)
    {
        var hours = variant == IndexerRequestUsageVariant.Pruned
            ? PrunedUsageHoursPerIndexer
            : UnprunedUsageHoursPerIndexer;
        var currentHour = new DateTime(
            ClockUtc.Year, ClockUtc.Month, ClockUtc.Day, ClockUtc.Hour, 0, 0, DateTimeKind.Utc);

        var indexerIds = await AddBudgetedIndexersAsync(fixture, cancellationToken);
        await using var connection = new SqliteConnection(fixture.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await InsertUsageAsync(connection, transaction, indexerIds, currentHour, hours, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return indexerIds;
    }

    /// <summary>The count the usage history stores for the hour <paramref name="hoursAgo"/> back.</summary>
    public static int UsageCountForHoursAgo(int hoursAgo) => 1 + hoursAgo % UsageCountCycle;

    // The indexers and the settings row go through EF, so every column the application
    // requires gets the value a saved entity would.
    private static async Task<IReadOnlyList<int>> AddBudgetedIndexersAsync(
        LargeLibraryFixture fixture,
        CancellationToken cancellationToken)
    {
        await using var context = new ListenArrDbContext(fixture.CreateOptions());
        var indexers = Enumerable.Range(1, BudgetedIndexerCount)
            .Select(id => new Indexer
            {
                Id = id,
                Name = $"Indexer {id}",
                Type = "Torrent",
                Implementation = "Torznab",
                Url = "https://indexer.example.invalid",
                DailyRequestLimit = BudgetDailyRequestLimit
            })
            .ToList();
        context.Indexers.AddRange(indexers);
        context.ApplicationSettings.Add(new ApplicationSettings
        {
            Id = 1,
            IndexerInteractiveReservePercent = BudgetInteractiveReservePercent
        });
        await context.SaveChangesAsync(cancellationToken);
        return indexers.Select(indexer => indexer.Id).ToList();
    }

    private static async Task InsertUsageAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<int> indexerIds,
        DateTime currentHour,
        int hours,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO "IndexerRequestUsages" ("IndexerId", "HourStartUtc", "Count")
            VALUES ($indexerId, $hourStartUtc, $count);
            """;
        var indexerId = command.Parameters.Add("$indexerId", SqliteType.Integer);
        // Bound as a DateTime, so the hour is stored in the text form EF writes for the key.
        var hourStartUtc = command.Parameters.Add("$hourStartUtc", SqliteType.Text);
        var count = command.Parameters.Add("$count", SqliteType.Integer);
        await command.PrepareAsync(cancellationToken);

        foreach (var id in indexerIds)
        {
            for (var hoursAgo = 0; hoursAgo < hours; hoursAgo++)
            {
                indexerId.Value = id;
                hourStartUtc.Value = currentHour.AddHours(-hoursAgo);
                count.Value = UsageCountForHoursAgo(hoursAgo);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }
}
