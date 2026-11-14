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
using Listenarr.Application.Search.Scheduling;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// The automatic search's schedule writes, on migrated file-backed SQLite. Every value is read
/// back through a fresh context, because on SQLite the book's schedule columns are ignored after
/// save and a write made through a tracked save would be dropped without an error.
/// </summary>
[Trait("Name", "EfAutomaticSearchScheduleStoreWritesTests")]
[Trait("Category", "Persistence")]
public sealed class EfAutomaticSearchScheduleStoreWritesTests : BaseTests
{
    private static readonly DateTime NowUtc = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SeededUtc = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EarlierUtc = new(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Cooldown = TimeSpan.FromHours(24);

    private AutomaticSearchScheduleTestDatabase _database = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _database = new AutomaticSearchScheduleTestDatabase(FileService.GetTempPath());
        await _database.MigrateAsync();
    }

    public static TheoryData<PairScheduleResult, double?, int, int, double, bool> ExistingPairCases => new()
    {
        // result, not-before (hours from now), no-result count, failure count, next attempt (hours), completed now
        { PairScheduleResult.Satisfied, null, 0, 0, 24, true },
        { PairScheduleResult.NoAcceptableResults, null, 3, 0, 96, true },
        { PairScheduleResult.GrabNotCompleted, 0.5, 2, 1, 0.5, true },
        { PairScheduleResult.Failed, null, 2, 2, 2, false },
        { PairScheduleResult.Failed, 5, 2, 2, 5, false },
        { PairScheduleResult.Failed, 0.5, 2, 2, 2, false },
        { PairScheduleResult.Deferred, 3, 2, 1, 3, false }
    };

    [Theory]
    [MemberData(nameof(ExistingPairCases))]
    public async Task PersistPairAsync_UpdatesAnExistingPair_AndTheValuesLand(
        PairScheduleResult result, double? notBeforeHours, int noResults, int failures, double nextHours, bool completedNow)
    {
        var (book, indexer) = await SeedBookAndIndexerAsync();
        await _database.SeedPairAsync(new AudiobookIndexerSearchState
        {
            AudiobookId = book,
            IndexerId = indexer,
            NextAttemptUtc = SeededUtc,
            NoResultCount = 2,
            FailureCount = 1,
            LastOutcome = "Seeded",
            LastCompletedUtc = EarlierUtc
        });

        await PersistPairAsync(new PairScheduleUpdate(book, indexer, result, HoursFromNow(notBeforeHours)));

        var pair = await _database.ReadPairAsync(book, indexer);
        Assert.NotNull(pair);
        Assert.Equal(
            (noResults, failures, NowUtc.AddHours(nextHours), completedNow ? NowUtc : EarlierUtc, result.ToString()),
            (pair.NoResultCount, pair.FailureCount, pair.NextAttemptUtc, pair.LastCompletedUtc, pair.LastOutcome));
    }

    public static TheoryData<PairScheduleResult, double?, int, int, double, bool> MissingPairCases => new()
    {
        { PairScheduleResult.Satisfied, null, 0, 0, 24, true },
        { PairScheduleResult.NoAcceptableResults, null, 1, 0, 24, true },
        { PairScheduleResult.GrabNotCompleted, 0.5, 0, 0, 0.5, true },
        { PairScheduleResult.Failed, null, 0, 1, 1, false },
        { PairScheduleResult.Deferred, 3, 0, 0, 3, false }
    };

    [Theory]
    [MemberData(nameof(MissingPairCases))]
    public async Task PersistPairAsync_InsertsAMissingPair_AndTheValuesLand(
        PairScheduleResult result, double? notBeforeHours, int noResults, int failures, double nextHours, bool completedNow)
    {
        var (book, indexer) = await SeedBookAndIndexerAsync();

        await PersistPairAsync(new PairScheduleUpdate(book, indexer, result, HoursFromNow(notBeforeHours)));

        var pair = await _database.ReadPairAsync(book, indexer);
        Assert.NotNull(pair);
        Assert.Equal(
            (noResults, failures, NowUtc.AddHours(nextHours), completedNow ? NowUtc : (DateTime?)null, result.ToString()),
            (pair.NoResultCount, pair.FailureCount, pair.NextAttemptUtc, pair.LastCompletedUtc, pair.LastOutcome));
    }

    [Theory]
    [InlineData(PairScheduleResult.GrabNotCompleted)]
    [InlineData(PairScheduleResult.Deferred)]
    public async Task PersistPairAsync_WithoutTheRequiredTime_ThrowsAndWritesNothing(PairScheduleResult result)
    {
        var (book, indexer) = await SeedBookAndIndexerAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            PersistPairAsync(new PairScheduleUpdate(book, indexer, result)));

        Assert.Null(await _database.ReadPairAsync(book, indexer));
    }

    public static TheoryData<BookSearchOutcome, bool, double?, double?, int, bool> BookCases => new()
    {
        // outcome, any pair completed, not-before (hours), due (hours from now; null = unchanged), failure count, stamped
        { BookSearchOutcome.Satisfied, true, null, 2, 0, true },
        { BookSearchOutcome.NoAcceptableResults, true, null, 2, 0, true },
        { BookSearchOutcome.PartiallyCompleted, true, null, 2, 2, true },
        { BookSearchOutcome.Failed, false, null, 2, 2, false },
        { BookSearchOutcome.Deferred, false, null, 2, 2, false },
        { BookSearchOutcome.GrabNotCompleted, true, null, 1, 3, true },
        { BookSearchOutcome.GrabNotCompleted, true, 0.75, 0.75, 2, true },
        { BookSearchOutcome.BookFailed, false, null, 1, 3, false },
        { BookSearchOutcome.NotNeeded, false, null, 24, 2, false },
        { BookSearchOutcome.Cancelled, true, null, null, 2, true },
        { BookSearchOutcome.Cancelled, false, null, null, 2, false }
    };

    [Theory]
    [MemberData(nameof(BookCases))]
    public async Task PersistBookAsync_WritesDueFailureCountAndLastSearch_AndTheValuesLand(
        BookSearchOutcome outcome, bool anyPairCompleted, double? notBeforeHours, double? dueHours, int failures, bool stamped)
    {
        var book = await _database.SeedBookAsync(SeededUtc, failureCount: 2, lastSearchUtc: EarlierUtc);
        await SeedPairAsync(book, await _database.SeedIndexerAsync("Later"), NowUtc.AddHours(5));
        await SeedPairAsync(book, await _database.SeedIndexerAsync("Earliest enabled"), NowUtc.AddHours(2));
        await SeedPairAsync(book, await _database.SeedIndexerAsync("Disabled", enabled: false), NowUtc.AddHours(1));
        await _database.SeedIndexerAsync("Interactive only", automaticSearch: false);

        await PersistBookAsync(new BookScheduleUpdate(book, outcome, anyPairCompleted, HoursFromNow(notBeforeHours)));

        var stored = await _database.ReadBookAsync(book);
        Assert.Equal(
            (dueHours is { } hours ? NowUtc.AddHours(hours) : SeededUtc, failures, stamped ? NowUtc : EarlierUtc),
            (stored.DueUtc, stored.FailureCount, stored.LastSearchUtc));
    }

    [Fact]
    public async Task PersistBookAsync_CountsAnEnabledIndexerWithNoPairRowAsDueNow()
    {
        var book = await _database.SeedBookAsync(SeededUtc);
        await SeedPairAsync(book, await _database.SeedIndexerAsync("Searched"), NowUtc.AddHours(5));
        await _database.SeedIndexerAsync("Never searched");

        await PersistBookAsync(new BookScheduleUpdate(book, BookSearchOutcome.PartiallyCompleted, AnyPairCompleted: true));

        Assert.Equal(NowUtc, (await _database.ReadBookAsync(book)).DueUtc);
    }

    [Fact]
    public async Task PersistBookAsync_NotNeeded_WaitsForTheEarliestPairWhenItIsAfterTheCooldown()
    {
        var book = await _database.SeedBookAsync(SeededUtc);
        await SeedPairAsync(book, await _database.SeedIndexerAsync("Backed off"), NowUtc.AddHours(30));

        await PersistBookAsync(new BookScheduleUpdate(book, BookSearchOutcome.NotNeeded, AnyPairCompleted: false));

        Assert.Equal(NowUtc.AddHours(30), (await _database.ReadBookAsync(book)).DueUtc);
    }

    [Fact]
    public async Task PersistBookAsync_WithNoIndexerEnabledForAutomaticSearch_WaitsTheCooldown()
    {
        var book = await _database.SeedBookAsync(SeededUtc);
        await _database.SeedIndexerAsync("Disabled", enabled: false);

        await PersistBookAsync(new BookScheduleUpdate(book, BookSearchOutcome.Failed, AnyPairCompleted: false));

        Assert.Equal(NowUtc.Add(Cooldown), (await _database.ReadBookAsync(book)).DueUtc);
    }

    [Fact]
    public async Task PersistBookAsync_RunsItsReadsAndItsUpdateInOneTransaction()
    {
        var book = await _database.SeedBookAsync(SeededUtc);
        await SeedPairAsync(book, await _database.SeedIndexerAsync("Searched"), NowUtc.AddHours(5));
        var capture = new CommandCapture();

        await using (var context = _database.CreateContext(capture))
        {
            await new EfAutomaticSearchScheduleStore(context).PersistBookAsync(
                new BookScheduleUpdate(book, BookSearchOutcome.BookFailed, AnyPairCompleted: true), NowUtc, Cooldown);
        }

        var update = Assert.Single(capture.Commands, command => command.Text.StartsWith("UPDATE", StringComparison.Ordinal));
        Assert.Contains("\"AutomaticSearchDueUtc\"", update.Text);
        Assert.Contains("\"AutomaticSearchFailureCount\"", update.Text);
        Assert.Contains("\"LastSearchTime\"", update.Text);
        Assert.NotNull(update.Transaction);
        Assert.All(capture.Commands, command => Assert.Same(update.Transaction, command.Transaction));
    }

    [Fact]
    public async Task PersistBookAsync_JoinsTheCallersTransaction()
    {
        var book = await _database.SeedBookAsync(SeededUtc);
        await _database.SeedIndexerAsync("Never searched");

        await using (var context = _database.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await new EfAutomaticSearchScheduleStore(context).PersistBookAsync(
                new BookScheduleUpdate(book, BookSearchOutcome.BookFailed, AnyPairCompleted: true), NowUtc, Cooldown);
            await transaction.RollbackAsync();
        }

        Assert.Equal((SeededUtc, 0, (DateTime?)null), DueCountAndStamp(await _database.ReadBookAsync(book)));
    }

    [Fact]
    public async Task CallerCancel_DuringAFinishedPairsWrite_KeepsThePairAndLeavesTheOtherDue()
    {
        var book = await _database.SeedBookAsync(SeededUtc);
        var finished = await _database.SeedIndexerAsync("Finished");
        var notStarted = await _database.SeedIndexerAsync("Not started");
        using var cycle = new CancellationTokenSource();

        await using (var context = _database.CreateContext(new CancelOnWrite(cycle)))
        {
            await new EfAutomaticSearchScheduleStore(context).PersistPairAsync(
                new PairScheduleUpdate(book, finished, PairScheduleResult.NoAcceptableResults), NowUtc, Cooldown, cycle.Token);
        }

        Assert.True(cycle.IsCancellationRequested);
        var pair = await _database.ReadPairAsync(book, finished);
        Assert.NotNull(pair);
        Assert.Equal((1, NowUtc.Add(Cooldown)), (pair.NoResultCount, pair.NextAttemptUtc));
        Assert.Null(await _database.ReadPairAsync(book, notStarted));
        Assert.Equal(SeededUtc, (await _database.ReadBookAsync(book)).DueUtc);
    }

    [Fact]
    public async Task CallerCancel_DuringTheBookWrite_KeepsTheBookWrite()
    {
        var book = await _database.SeedBookAsync(SeededUtc);
        await _database.SeedIndexerAsync("Never searched");
        using var cycle = new CancellationTokenSource();

        await using (var context = _database.CreateContext(new CancelOnWrite(cycle)))
        {
            await new EfAutomaticSearchScheduleStore(context).PersistBookAsync(
                new BookScheduleUpdate(book, BookSearchOutcome.BookFailed, AnyPairCompleted: true), NowUtc, Cooldown, cycle.Token);
        }

        Assert.True(cycle.IsCancellationRequested);
        Assert.Equal((NowUtc.AddMinutes(15), 1, (DateTime?)NowUtc), DueCountAndStamp(await _database.ReadBookAsync(book)));
    }

    [Fact]
    public async Task CallerCancel_BeforeTheWrite_ThrowsAndWritesNothing()
    {
        var (book, indexer) = await SeedBookAndIndexerAsync();
        using var cycle = new CancellationTokenSource();
        await cycle.CancelAsync();

        await using var context = _database.CreateContext();
        var store = new EfAutomaticSearchScheduleStore(context);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.PersistPairAsync(
            new PairScheduleUpdate(book, indexer, PairScheduleResult.NoAcceptableResults), NowUtc, Cooldown, cycle.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.PersistBookAsync(
            new BookScheduleUpdate(book, BookSearchOutcome.BookFailed, AnyPairCompleted: true), NowUtc, Cooldown, cycle.Token));

        Assert.Null(await _database.ReadPairAsync(book, indexer));
        Assert.Equal((SeededUtc, 0, (DateTime?)null), DueCountAndStamp(await _database.ReadBookAsync(book)));
    }

    [Fact]
    public async Task StaleDetachedSave_AfterAStoreWrite_KeepsTheSchedule()
    {
        var (book, _) = await SeedBookAndIndexerAsync();
        Audiobook stale;
        await using (var reader = _database.CreateContext())
        {
            stale = await reader.Audiobooks.AsNoTracking().SingleAsync(candidate => candidate.Id == book);
        }

        await PersistBookAsync(new BookScheduleUpdate(book, BookSearchOutcome.BookFailed, AnyPairCompleted: false));
        var written = (NowUtc.AddMinutes(15), 1);
        var afterWrite = await _database.ReadBookAsync(book);
        Assert.Equal(written, (afterWrite.DueUtc, afterWrite.FailureCount));

        stale.Monitored = false;
        await using (var editor = _database.CreateContext())
        {
            Assert.True(await new AudiobookRepository(editor).UpdateAsync(stale));
        }

        var afterSave = await _database.ReadBookAsync(book);
        Assert.Equal(written, (afterSave.DueUtc, afterSave.FailureCount));
        Assert.False(afterSave.Monitored);
    }

    private static DateTime? HoursFromNow(double? hours) => hours is { } value ? NowUtc.AddHours(value) : null;

    private static (DateTime, int, DateTime?) DueCountAndStamp(
        (DateTime DueUtc, int FailureCount, DateTime? LastSearchUtc, bool Monitored) book) =>
        (book.DueUtc, book.FailureCount, book.LastSearchUtc);

    private async Task<(int Book, int Indexer)> SeedBookAndIndexerAsync() =>
        (await _database.SeedBookAsync(SeededUtc), await _database.SeedIndexerAsync("Only"));

    private Task SeedPairAsync(int book, int indexer, DateTime nextAttemptUtc) =>
        _database.SeedPairAsync(new AudiobookIndexerSearchState
        {
            AudiobookId = book,
            IndexerId = indexer,
            NextAttemptUtc = nextAttemptUtc,
            LastOutcome = "Seeded"
        });

    private async Task PersistPairAsync(PairScheduleUpdate update)
    {
        await using var context = _database.CreateContext();
        await new EfAutomaticSearchScheduleStore(context).PersistPairAsync(update, NowUtc, Cooldown);
    }

    private async Task PersistBookAsync(BookScheduleUpdate update)
    {
        await using var context = _database.CreateContext();
        await new EfAutomaticSearchScheduleStore(context).PersistBookAsync(update, NowUtc, Cooldown);
    }

    private sealed record CapturedCommand(string Text, DbTransaction? Transaction);

    private sealed class CommandCapture : DbCommandInterceptor
    {
        public List<CapturedCommand> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(new CapturedCommand(command.CommandText.TrimStart(), command.Transaction));
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(new CapturedCommand(command.CommandText.TrimStart(), command.Transaction));
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>Cancels the cycle the moment the store issues its first write.</summary>
    private sealed class CancelOnWrite(CancellationTokenSource cycle) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            CancelIfWrite(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            CancelIfWrite(command);
            return ValueTask.FromResult(result);
        }

        private void CancelIfWrite(DbCommand command)
        {
            var text = command.CommandText.TrimStart();
            if (text.StartsWith("UPDATE", StringComparison.Ordinal) || text.StartsWith("INSERT", StringComparison.Ordinal))
            {
                cycle.Cancel();
            }
        }
    }
}
