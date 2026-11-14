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
using Listenarr.Infrastructure.DependencyInjection.Persistence;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Listenarr.Tests.Features.Infrastructure.HostedServices.Search.Sweep;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static Listenarr.Tests.Features.Infrastructure.Persistence.AutomaticSearchResetTestLibrary;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// The metadata-change reset at the context level: which edits reset a book, that the reset and
/// the edit commit together or not at all, and that the reset lands on migrated SQLite (where a
/// tracked write of the schedule columns is dropped) and on EF InMemory.
/// </summary>
[Trait("Name", "AutomaticSearchResetInterceptorTests")]
[Trait("Category", "Persistence")]
public sealed class AutomaticSearchResetInterceptorTests : BaseTests
{
    private const string ResetFailure = "the schedule reset failed";
    private const string EditFailure = "the audiobook update failed";

    private readonly SweepClock _clock = new(NowUtc);
    private AutomaticSearchScheduleTestDatabase _database = null!;
    private AutomaticSearchResetTestLibrary _library = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _database = new AutomaticSearchScheduleTestDatabase(FileService.GetTempPath());
        _library = new AutomaticSearchResetTestLibrary(_database);
        await _library.InitializeAsync();
    }

    public static TheoryData<string, bool> SearchInputEdits => new()
    {
        { "title", true },
        { "first author", true },
        { "series", true },
        { "asin", false },
        { "quality profile", true },
        { "monitored switched on", false },
    };

    [Theory]
    [MemberData(nameof(SearchInputEdits))]
    public async Task SearchInputEdited_ResetsTheBook_AndTheValuesLand(string edit, bool asynchronous)
    {
        var book = await _library.SeedBookInBackoffAsync(monitored: edit != "monitored switched on");
        var bystander = await _library.SeedBookInBackoffAsync();

        await EditAsync(book, edit, asynchronous);

        Assert.Equal(Reset, await _library.ReadScheduleAsync(book));
        Assert.Equal(Untouched, await _library.ReadScheduleAsync(bystander));
    }

    [Theory]
    [InlineData("identical title")]
    [InlineData("second author")]
    [InlineData("description")]
    [InlineData("monitored switched off")]
    public async Task OtherEdits_LeaveTheScheduleAlone(string edit)
    {
        var book = await _library.SeedBookInBackoffAsync();
        var capture = new CommandCapture();

        await EditAsync(book, edit, asynchronous: true, capture);

        Assert.Equal(Untouched, await _library.ReadScheduleAsync(book));
        Assert.DoesNotContain(capture.Commands, command => command.Contains("\"AudiobookIndexerSearchStates\"", StringComparison.Ordinal));
        Assert.Contains(capture.Commands, command => command.StartsWith("UPDATE \"Audiobooks\" SET", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResetThrows_TheEditIsNotPersisted_AndNoTransactionIsLeftOpen(bool asynchronous)
    {
        var book = await _library.SeedBookInBackoffAsync();

        await using (var context = CreateContext(new ThrowOn("UPDATE \"Audiobooks\" AS", ResetFailure)))
        {
            var tracked = await context.Audiobooks.SingleAsync(candidate => candidate.Id == book);
            tracked.Title = "The Gods of Mars";
            var error = asynchronous
                ? await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync())
                : Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
            Assert.Equal(ResetFailure, error.Message);
            Assert.Null(context.Database.CurrentTransaction);
        }

        Assert.Equal(Title, (await ReadBookAsync(book)).Title);
        Assert.Equal(Untouched, await _library.ReadScheduleAsync(book));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EditFails_NoTransactionIsLeftOpen(bool asynchronous)
    {
        var book = await _library.SeedBookInBackoffAsync();

        await using (var context = CreateContext(new ThrowOn("UPDATE \"Audiobooks\" SET", EditFailure)))
        {
            var tracked = await context.Audiobooks.SingleAsync(candidate => candidate.Id == book);
            tracked.Title = "The Gods of Mars";
            if (asynchronous)
            {
                await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync());
            }
            else
            {
                Assert.ThrowsAny<Exception>(() => context.SaveChanges());
            }

            Assert.Null(context.Database.CurrentTransaction);
        }

        Assert.Equal(Title, (await ReadBookAsync(book)).Title);
        Assert.Equal(Untouched, await _library.ReadScheduleAsync(book));
    }

    [Fact]
    public async Task InsideACallerTransaction_JoinsIt()
    {
        var book = await _library.SeedBookInBackoffAsync();

        await using (var context = CreateContext())
        {
            await using var callerTransaction = await context.Database.BeginTransactionAsync();
            var tracked = await context.Audiobooks.SingleAsync(candidate => candidate.Id == book);
            tracked.Title = "The Gods of Mars";
            await context.SaveChangesAsync();
            Assert.Same(callerTransaction, context.Database.CurrentTransaction);
            await callerTransaction.RollbackAsync();
        }

        Assert.Equal(Title, (await ReadBookAsync(book)).Title);
        Assert.Equal(Untouched, await _library.ReadScheduleAsync(book));
    }

    [Fact]
    public async Task OnInMemory_ResetsTheBook_AndDeletesPairRowsTheContextHadNotLoaded()
    {
        var options = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseInMemoryDatabase($"metadata-reset-{Guid.NewGuid():N}")
            .AddInterceptors(new AutomaticSearchResetInterceptor(_clock))
            .Options;
        int edited, bystander;
        await using (var context = new ListenArrDbContext(options))
        {
            var books = new[] { NewBookInBackoff(), NewBookInBackoff() };
            context.Audiobooks.AddRange(books);
            await context.SaveChangesAsync();
            (edited, bystander) = (books[0].Id, books[1].Id);
            foreach (var bookId in new[] { edited, bystander })
            {
                context.AudiobookIndexerSearchStates.AddRange(
                    new AudiobookIndexerSearchState { AudiobookId = bookId, IndexerId = 1, NextAttemptUtc = LaterUtc },
                    new AudiobookIndexerSearchState { AudiobookId = bookId, IndexerId = 2, NextAttemptUtc = LaterUtc });
            }

            await context.SaveChangesAsync();
        }

        await using (var context = new ListenArrDbContext(options))
        {
            // One of the edited book's pair rows is already tracked; the other is not.
            await context.AudiobookIndexerSearchStates.SingleAsync(pair => pair.AudiobookId == edited && pair.IndexerId == 1);
            var tracked = await context.Audiobooks.SingleAsync(candidate => candidate.Id == edited);
            tracked.Title = "The Gods of Mars";
            await context.SaveChangesAsync();
        }

        await using var reader = new ListenArrDbContext(options);
        var schedules = await reader.Audiobooks.AsNoTracking()
            .OrderBy(book => book.Id)
            .Select(book => new { book.AutomaticSearchDueUtc, book.AutomaticSearchFailureCount })
            .ToListAsync();
        Assert.Equal((NowUtc, 0), (schedules[0].AutomaticSearchDueUtc, schedules[0].AutomaticSearchFailureCount));
        Assert.Equal((LaterUtc, FailureCount), (schedules[1].AutomaticSearchDueUtc, schedules[1].AutomaticSearchFailureCount));
        Assert.Empty(await reader.AudiobookIndexerSearchStates.Where(pair => pair.AudiobookId == edited).ToListAsync());
        Assert.Equal(2, await reader.AudiobookIndexerSearchStates.CountAsync(pair => pair.AudiobookId == bystander));
    }

    [Fact]
    public async Task Registration_GivesTheFactoryAndTheScopedContextTheReset()
    {
        var throughFactory = await _library.SeedBookInBackoffAsync();
        var throughScope = await _library.SeedBookInBackoffAsync();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddPersistenceServices(options => options.UseSqlite(_database.ConnectionString));
        await using var provider = services.BuildServiceProvider();

        await using (var context = await provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>().CreateDbContextAsync())
        {
            (await context.Audiobooks.SingleAsync(candidate => candidate.Id == throughFactory)).Title = "The Gods of Mars";
            await context.SaveChangesAsync();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ListenArrDbContext>();
            (await context.Audiobooks.SingleAsync(candidate => candidate.Id == throughScope)).Title = "The Gods of Mars";
            await context.SaveChangesAsync();
        }

        Assert.Equal(Reset, await _library.ReadScheduleAsync(throughFactory));
        Assert.Equal(Reset, await _library.ReadScheduleAsync(throughScope));
    }

    private ListenArrDbContext CreateContext(params IInterceptor[] interceptors) =>
        _database.CreateContext([new AutomaticSearchResetInterceptor(_clock), .. interceptors]);

    private async Task EditAsync(int bookId, string edit, bool asynchronous, CommandCapture? capture = null)
    {
        await using var context = capture == null ? CreateContext() : CreateContext(capture);
        var book = await context.Audiobooks.SingleAsync(candidate => candidate.Id == bookId);
        switch (edit)
        {
            case "title": book.Title = "The Gods of Mars"; break;
            case "first author": book.Authors = ["Jules Verne"]; break;
            case "series": book.Series = "Tarzan"; break;
            case "asin": book.Asin = "B071YLS9YL"; break;
            case "quality profile": book.QualityProfileId = _library.OtherProfileId; break;
            case "monitored switched on": book.Monitored = true; break;
            case "identical title": book.Title = new string(Title.ToCharArray()); book.Description = "Refreshed."; break;
            case "second author": book.Authors = [Author, "Jules Verne"]; break;
            case "description": book.Description = "Refreshed."; break;
            case "monitored switched off": book.Monitored = false; break;
            default: throw new ArgumentOutOfRangeException(nameof(edit), edit, null);
        }

        if (asynchronous)
        {
            await context.SaveChangesAsync();
        }
        else
        {
            context.SaveChanges();
        }
    }

    private async Task<Audiobook> ReadBookAsync(int bookId)
    {
        await using var context = _database.CreateContext();
        return await context.Audiobooks.AsNoTracking().SingleAsync(candidate => candidate.Id == bookId);
    }

    private static Audiobook NewBookInBackoff()
    {
        var book = new AudiobookBuilder().WithTitle(Title).WithAuthor(Author).WithMonitored().Build();
        book.QualityProfileId = 1;
        book.AutomaticSearchDueUtc = LaterUtc;
        book.AutomaticSearchFailureCount = FailureCount;
        return book;
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

    /// <summary>Fails every command, sync or async, whose text starts with the given prefix.</summary>
    private sealed class ThrowOn(string prefix, string message) : DbCommandInterceptor
    {
        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result) =>
            Matches(command) ? throw new InvalidOperationException(message) : result;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            Matches(command) ? throw new InvalidOperationException(message) : ValueTask.FromResult(result);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result) =>
            Matches(command) ? throw new InvalidOperationException(message) : result;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            Matches(command) ? throw new InvalidOperationException(message) : ValueTask.FromResult(result);

        private bool Matches(DbCommand command) =>
            command.CommandText.TrimStart().StartsWith(prefix, StringComparison.Ordinal);
    }
}
