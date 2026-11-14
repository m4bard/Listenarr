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
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Listenarr.Tests.Features.Infrastructure.HostedServices.Search.Sweep;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// The indexer-set hook: a write that makes an indexer newly eligible for automatic search pulls
/// every monitored book with a quality profile forward to due now, in the same transaction as the
/// indexer write. Values are read back through a fresh context on migrated SQLite, where a tracked
/// save of the due time would be dropped without an error.
/// </summary>
[Trait("Name", "EfIndexerRepositoryIndexerSetTests")]
[Trait("Category", "Persistence")]
public sealed class EfIndexerRepositoryIndexerSetTests : BaseTests
{
    private static readonly DateTime NowUtc = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LaterUtc = NowUtc.AddHours(24);
    private static readonly DateTime EarlierUtc = NowUtc.AddHours(-3);
    private const string DueUpdateFailure = "the due-time update failed";

    private readonly SweepClock _clock = new(NowUtc);
    private AutomaticSearchScheduleTestDatabase _database = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _database = new AutomaticSearchScheduleTestDatabase(FileService.GetTempPath());
        await _database.MigrateAsync();
    }

    [Fact]
    public async Task AddAsync_EligibleIndexer_PullsDueBooksForward_AndTheValuesLand()
    {
        var library = await SeedLibraryAsync();

        await using (var context = _database.CreateContext())
        {
            await new EfIndexerRepository(context, _clock).AddAsync(NewIndexer("Added", enabled: true, automaticSearch: true));
        }

        // Only the monitored, profiled book due later moves; an earlier due time is kept.
        Assert.Equal(new[] { NowUtc, EarlierUtc, LaterUtc, LaterUtc }, await ReadDueAsync(library));
        Assert.True(await IndexerExistsAsync("Added"));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task AddAsync_IndexerNotEligible_LeavesBooksAlone(bool enabled, bool automaticSearch)
    {
        var library = await SeedLibraryAsync();

        await using (var context = _database.CreateContext())
        {
            await new EfIndexerRepository(context, _clock).AddAsync(NewIndexer("Added", enabled, automaticSearch));
        }

        Assert.Equal(new[] { LaterUtc, EarlierUtc, LaterUtc, LaterUtc }, await ReadDueAsync(library));
        Assert.True(await IndexerExistsAsync("Added"));
    }

    [Theory]
    [InlineData(nameof(Indexer.IsEnabled), true)]
    [InlineData(nameof(Indexer.EnableAutomaticSearch), true)]
    [InlineData(nameof(Indexer.IsEnabled), false)]
    public async Task UpdateAsync_EnablingAnIndexer_PullsDueBooksForward(string flag, bool tracked)
    {
        var library = await SeedLibraryAsync();
        var id = await _database.SeedIndexerAsync(
            "Enabled later",
            enabled: flag != nameof(Indexer.IsEnabled),
            automaticSearch: flag != nameof(Indexer.EnableAutomaticSearch));

        await using (var context = _database.CreateContext())
        {
            var repository = new EfIndexerRepository(context, _clock);
            // Tracked, as the toggle and edit endpoints do: the instance the repository finds is
            // the one the caller has already changed.
            var indexer = tracked
                ? (await repository.GetByIdAsync(id))!
                : (await repository.GetAllAsync()).Single(candidate => candidate.Id == id);
            indexer.IsEnabled = true;
            indexer.EnableAutomaticSearch = true;
            await repository.UpdateAsync(indexer);
        }

        Assert.Equal(new[] { NowUtc, EarlierUtc, LaterUtc, LaterUtc }, await ReadDueAsync(library));
        await using var reader = _database.CreateContext();
        var stored = await reader.Indexers.AsNoTracking().SingleAsync(candidate => candidate.Id == id);
        Assert.True(stored.IsEnabled && stored.EnableAutomaticSearch);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateAsync_EligibilityNotGained_DoesNotTouchAudiobooks(bool cookieRefresh)
    {
        var library = await SeedLibraryAsync();
        var id = await _database.SeedIndexerAsync("Already eligible");
        var capture = new CommandCapture();

        await using (var context = _database.CreateContext(capture))
        {
            var repository = new EfIndexerRepository(context, _clock);
            var indexer = (await repository.GetByIdAsync(id))!;
            if (cookieRefresh)
            {
                // The MyAnonamouse cookie refresh: an eligible indexer saved with new settings.
                indexer.AdditionalSettings = "{\"mam_id\":\"refreshed\"}";
            }
            else
            {
                indexer.IsEnabled = false;
            }

            await repository.UpdateAsync(indexer);
        }

        Assert.DoesNotContain(capture.Commands, command => command.Contains("\"Audiobooks\"", StringComparison.Ordinal));
        Assert.Contains(capture.Commands, command => command.StartsWith("UPDATE \"Indexers\"", StringComparison.Ordinal));
        Assert.Equal(new[] { LaterUtc, EarlierUtc, LaterUtc, LaterUtc }, await ReadDueAsync(library));
    }

    [Fact]
    public async Task AddAsync_WhenTheDueUpdateThrows_TheIndexerIsNotSaved()
    {
        var library = await SeedLibraryAsync();

        await using (var context = _database.CreateContext(new ThrowOnDueUpdate()))
        {
            var indexer = NewIndexer("Rolled back", enabled: true, automaticSearch: true);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new EfIndexerRepository(context, _clock).AddAsync(indexer));
            Assert.Equal(DueUpdateFailure, error.Message);
            Assert.Null(context.Database.CurrentTransaction);
        }

        Assert.False(await IndexerExistsAsync("Rolled back"));
        Assert.Equal(new[] { LaterUtc, EarlierUtc, LaterUtc, LaterUtc }, await ReadDueAsync(library));
    }

    [Fact]
    public async Task UpdateAsync_WhenTheDueUpdateThrows_TheIndexerIsNotSaved()
    {
        var library = await SeedLibraryAsync();
        var id = await _database.SeedIndexerAsync("Rolled back", enabled: false);

        await using (var context = _database.CreateContext(new ThrowOnDueUpdate()))
        {
            var repository = new EfIndexerRepository(context, _clock);
            var indexer = (await repository.GetByIdAsync(id))!;
            indexer.IsEnabled = true;
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpdateAsync(indexer));
            Assert.Equal(DueUpdateFailure, error.Message);
            Assert.Null(context.Database.CurrentTransaction);
        }

        await using var reader = _database.CreateContext();
        Assert.False((await reader.Indexers.AsNoTracking().SingleAsync(candidate => candidate.Id == id)).IsEnabled);
        Assert.Equal(new[] { LaterUtc, EarlierUtc, LaterUtc, LaterUtc }, await ReadDueAsync(library));
    }

    [Fact]
    public async Task AddAsync_InsideACallerTransaction_JoinsIt()
    {
        var library = await SeedLibraryAsync();

        await using (var context = _database.CreateContext())
        {
            await using var callerTransaction = await context.Database.BeginTransactionAsync();
            await new EfIndexerRepository(context, _clock).AddAsync(NewIndexer("Joined", enabled: true, automaticSearch: true));
            Assert.Same(callerTransaction, context.Database.CurrentTransaction);
            await callerTransaction.RollbackAsync();
        }

        Assert.False(await IndexerExistsAsync("Joined"));
        Assert.Equal(new[] { LaterUtc, EarlierUtc, LaterUtc, LaterUtc }, await ReadDueAsync(library));
    }

    [Fact]
    public async Task AddAsync_OnInMemory_PullsDueBooksForward()
    {
        var options = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseInMemoryDatabase($"indexer-set-{Guid.NewGuid():N}")
            .Options;
        int dueLater, unmonitored;
        await using (var context = new ListenArrDbContext(options))
        {
            var monitored = new AudiobookBuilder().WithTitle("A Princess of Mars").WithMonitored().Build();
            var skipped = new AudiobookBuilder().WithTitle("A Princess of Mars").Build();
            monitored.Monitored = true;
            skipped.Monitored = false;
            (monitored.QualityProfileId, skipped.QualityProfileId) = (1, 1);
            (monitored.AutomaticSearchDueUtc, skipped.AutomaticSearchDueUtc) = (LaterUtc, LaterUtc);
            context.Audiobooks.AddRange(monitored, skipped);
            await context.SaveChangesAsync();
            (dueLater, unmonitored) = (monitored.Id, skipped.Id);
        }

        await using (var context = new ListenArrDbContext(options))
        {
            await new EfIndexerRepository(context, _clock).AddAsync(NewIndexer("Added", enabled: true, automaticSearch: true));
        }

        await using var reader = new ListenArrDbContext(options);
        var due = await reader.Audiobooks.AsNoTracking()
            .Where(book => book.Id == dueLater || book.Id == unmonitored)
            .OrderBy(book => book.Id)
            .Select(book => book.AutomaticSearchDueUtc)
            .ToListAsync();
        Assert.Equal(new[] { NowUtc, LaterUtc }, due);
    }

    private static Indexer NewIndexer(string name, bool enabled, bool automaticSearch)
    {
        var builder = new IndexerBuilder().WithName(name);
        var indexer = (enabled ? builder.WithEnabled() : builder.WithDisabled()).Build();
        indexer.EnableAutomaticSearch = automaticSearch;
        return indexer;
    }

    /// <summary>
    /// Four books: monitored with a profile due later (the one the hook moves), the same due
    /// earlier, unmonitored due later, and without a quality profile due later.
    /// </summary>
    private async Task<int[]> SeedLibraryAsync()
    {
        var dueLater = await _database.SeedBookAsync(LaterUtc);
        var dueEarlier = await _database.SeedBookAsync(EarlierUtc);
        var unmonitored = await _database.SeedBookAsync(LaterUtc);
        var noProfile = await _database.SeedBookAsync(LaterUtc);
        await using var context = _database.CreateContext();
        await context.Audiobooks.Where(book => book.Id == unmonitored)
            .ExecuteUpdateAsync(setters => setters.SetProperty(book => book.Monitored, false));
        await context.Audiobooks.Where(book => book.Id == noProfile)
            .ExecuteUpdateAsync(setters => setters.SetProperty(book => book.QualityProfileId, (int?)null));
        return new[] { dueLater, dueEarlier, unmonitored, noProfile };
    }

    private async Task<DateTime[]> ReadDueAsync(int[] library)
    {
        var due = new DateTime[library.Length];
        for (var index = 0; index < library.Length; index++)
        {
            due[index] = (await _database.ReadBookAsync(library[index])).DueUtc;
        }

        return due;
    }

    private async Task<bool> IndexerExistsAsync(string name)
    {
        await using var reader = _database.CreateContext();
        return await reader.Indexers.AsNoTracking().AnyAsync(indexer => indexer.Name == name);
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
            Commands.Add(command.CommandText.TrimStart());
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText.TrimStart());
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>Fails the set-based due-time update after the indexer write has been sent.</summary>
    private sealed class ThrowOnDueUpdate : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            command.CommandText.TrimStart().StartsWith("UPDATE \"Audiobooks\"", StringComparison.Ordinal)
                ? throw new InvalidOperationException(DueUpdateFailure)
                : ValueTask.FromResult(result);
    }
}
