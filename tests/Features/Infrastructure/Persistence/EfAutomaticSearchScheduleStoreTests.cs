/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using System.Data.Common;
using System.Text.RegularExpressions;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// The automatic search's schedule reads, on migrated SQLite: only due, eligible books leave
/// SQL, oldest due first and then by id, one page at a time; pair rows are read for one page.
/// </summary>
[Trait("Name", "EfAutomaticSearchScheduleStoreTests")]
[Trait("Category", "Persistence")]
public sealed class EfAutomaticSearchScheduleStoreTests : BaseTests
{
    private const string DueIndexName = "IX_Audiobooks_AutomaticSearchDue";
    private static readonly DateTime NowUtc = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly CommandCapture _capture = new();
    private readonly ITestOutputHelper _output;

    public EfAutomaticSearchScheduleStoreTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await _connection.OpenAsync();
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public override async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task GetDuePageAsync_OrdersByDueThenId()
    {
        var profile = await SeedProfileAsync();
        var dueAnHourAgo = await SeedBookAsync(NowUtc.AddHours(-1), profile);
        var dueTwoHoursAgo = await SeedBookAsync(NowUtc.AddHours(-2), profile);
        var alsoDueAnHourAgo = await SeedBookAsync(NowUtc.AddHours(-1), profile);
        var dueNow = await SeedBookAsync(NowUtc, profile);
        await SeedBookAsync(NowUtc.AddHours(1), profile);
        await SeedBookAsync(NowUtc.AddHours(-3), profile, monitored: false);
        await SeedBookAsync(NowUtc.AddHours(-3), profile: null);

        await using var context = CreateContext();
        var page = await new EfAutomaticSearchScheduleStore(context).GetDuePageAsync(NowUtc, [], 10);

        Assert.Equal([dueTwoHoursAgo, dueAnHourAgo, alsoDueAnHourAgo, dueNow], page.Select(book => book.Id));
        Assert.All(page, book => Assert.Equal(profile, book.QualityProfile?.Id));
    }

    [Fact]
    public async Task GetDuePageAsync_SkipsExcludedBooksAndStopsAtThePageSize()
    {
        var profile = await SeedProfileAsync();
        var first = await SeedBookAsync(NowUtc.AddHours(-4), profile);
        var second = await SeedBookAsync(NowUtc.AddHours(-3), profile);
        var third = await SeedBookAsync(NowUtc.AddHours(-2), profile);
        await SeedBookAsync(NowUtc.AddHours(-1), profile);

        await using var context = CreateContext();
        var page = await new EfAutomaticSearchScheduleStore(context).GetDuePageAsync(NowUtc, [first], 2);

        Assert.Equal([second, third], page.Select(book => book.Id));
    }

    [Fact]
    public async Task GetDuePageAsync_RejectsAPageSizeBelowOne()
    {
        await using var context = CreateContext();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new EfAutomaticSearchScheduleStore(context).GetDuePageAsync(NowUtc, [], 0));
    }

    [Fact]
    public async Task GetDuePageAsync_FiltersDueTimeAndExclusionsInSql()
    {
        var profile = await SeedProfileAsync();
        var excluded = new[]
        {
            await SeedBookAsync(NowUtc.AddHours(-2), profile),
            await SeedBookAsync(NowUtc.AddHours(-1), profile)
        };

        await using var context = CreateContext(_capture);
        await new EfAutomaticSearchScheduleStore(context).GetDuePageAsync(NowUtc, excluded, 5);

        var sql = Assert.Single(_capture.Commands, command => command.Contains("FROM \"Audiobooks\""));
        _output.WriteLine(sql);
        Assert.Matches("\"AutomaticSearchDueUtc\" <= @\\w+", sql);
        Assert.Matches("\"Id\" NOT IN \\(@\\w+, @\\w+\\)", sql);
        Assert.Matches("ORDER BY \"a\"\\.\"AutomaticSearchDueUtc\", \"a\"\\.\"Id\"", sql);
        Assert.Matches("LIMIT @\\w+", sql);
    }

    [Fact]
    public async Task CapturedDuePage_CarriesTheIndexFilterTermForTerm()
    {
        await using var context = CreateContext(_capture);
        var filter = context.Model.FindEntityType(typeof(Audiobook))!
            .GetIndexes()
            .Single(index => index.GetDatabaseName() == DueIndexName)
            .GetFilter();
        Assert.False(string.IsNullOrEmpty(filter));

        await new EfAutomaticSearchScheduleStore(context).GetDuePageAsync(NowUtc, [], 5);

        // SQLite uses a partial index only when the query's WHERE carries each of the index
        // filter's terms in the same form, so the filter is written as EF emits the predicate.
        var sql = Assert.Single(_capture.Commands, command => command.Contains("FROM \"Audiobooks\""));
        _output.WriteLine(sql);
        var where = Regex.Match(sql, "WHERE (.+?)\\s+ORDER BY", RegexOptions.Singleline).Groups[1].Value;
        Assert.Contains(filter, where.Replace("\"a\".", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetPairStatesAsync_ReturnsOnlyThePageBooksRows()
    {
        var profile = await SeedProfileAsync();
        var first = await SeedBookAsync(NowUtc, profile);
        var second = await SeedBookAsync(NowUtc, profile);
        var third = await SeedBookAsync(NowUtc, profile);
        var indexers = new[] { await SeedIndexerAsync("First"), await SeedIndexerAsync("Second") };
        await using (var context = CreateContext())
        {
            foreach (var book in new[] { first, second, third })
            {
                foreach (var indexer in indexers)
                {
                    context.AudiobookIndexerSearchStates.Add(new AudiobookIndexerSearchState
                    {
                        AudiobookId = book,
                        IndexerId = indexer,
                        NextAttemptUtc = NowUtc,
                        LastOutcome = "Completed"
                    });
                }
            }

            await context.SaveChangesAsync();
        }

        await using var reader = CreateContext();
        var states = await new EfAutomaticSearchScheduleStore(reader).GetPairStatesAsync([first, third]);

        Assert.Equal(
            [(first, indexers[0]), (first, indexers[1]), (third, indexers[0]), (third, indexers[1])],
            states.Select(state => (state.AudiobookId, state.IndexerId)).Order());
    }

    private ListenArrDbContext CreateContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptors)
            .Options);

    private async Task<int> SeedProfileAsync()
    {
        await using var context = CreateContext();
        var profile = new QualityProfile { Name = "Any" };
        context.QualityProfiles.Add(profile);
        await context.SaveChangesAsync();
        return profile.Id;
    }

    private async Task<int> SeedBookAsync(DateTime dueUtc, int? profile, bool monitored = true)
    {
        await using var context = CreateContext();
        var book = new AudiobookBuilder().WithTitle("A Princess of Mars").WithMonitored(monitored).Build();
        book.QualityProfileId = profile;
        book.AutomaticSearchDueUtc = dueUtc;
        context.Audiobooks.Add(book);
        await context.SaveChangesAsync();
        return book.Id;
    }

    private async Task<int> SeedIndexerAsync(string name)
    {
        await using var context = CreateContext();
        var indexer = new IndexerBuilder().WithName(name).Build();
        context.Indexers.Add(indexer);
        await context.SaveChangesAsync();
        return indexer.Id;
    }

    private sealed class CommandCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
