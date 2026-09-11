/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Repositories;

/// <summary>
/// Real SQLite, not the InMemory provider: the ordering has to survive query translation, and
/// the author match runs over a JSON-backed list column the provider cannot translate at all.
/// </summary>
[Trait("Area", "Persistence")]
[Trait("Name", "AudiobookRepository_MetadataRefreshQueryTests")]
[Trait("Category", "Infrastructure")]
public class AudiobookRepository_MetadataRefreshQueryTests : BaseTests
{
    /// <summary>Records the SQL that actually reached SQLite, so a test can assert on it.</summary>
    private sealed class CommandRecorder : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        /// <summary>
        /// The parameters each recorded statement carried, so a test can hand the same statement
        /// back to SQLite for an EXPLAIN QUERY PLAN with the values EF actually bound.
        /// </summary>
        public List<List<KeyValuePair<string, object?>>> Parameters { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Record(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        /// <summary>The last recorded statement whose text contains <paramref name="fragment"/>.</summary>
        public (string Sql, List<KeyValuePair<string, object?>> Parameters) Last(string fragment)
        {
            for (var index = Commands.Count - 1; index >= 0; index--)
            {
                if (Commands[index].Contains(fragment, StringComparison.Ordinal))
                {
                    return (Commands[index], Parameters[index]);
                }
            }

            throw new InvalidOperationException($"No recorded statement contained '{fragment}'.");
        }

        private void Record(DbCommand command)
        {
            Commands.Add(command.CommandText);
            Parameters.Add(command.Parameters
                .Cast<DbParameter>()
                .Select(parameter => new KeyValuePair<string, object?>(parameter.ParameterName, parameter.Value))
                .ToList());
        }
    }

    private sealed class TestDb : IDisposable
    {
        private readonly SqliteConnection _connection;
        public ListenArrDbContext Db { get; }

        public CommandRecorder Recorder { get; } = new();

        public TestDb()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            Db = new ListenArrDbContext(
                new DbContextOptionsBuilder<ListenArrDbContext>()
                    .UseSqlite(_connection)
                    .AddInterceptors(Recorder)
                    .Options);
            Db.Database.EnsureCreated();
        }

        public void Dispose()
        {
            Db.Dispose();
            _connection.Dispose();
        }
    }

    private static Audiobook Book(string title, string author, DateTime? refreshedAt) => new()
    {
        Title = title,
        Authors = [author],
        LastMetadataRefreshAt = refreshedAt
    };

    [Fact]
    [Trait("Scenario", "BackfillStartsTheClockRatherThanReadingItOffTheBook")]
    public async Task Backfill_GivesEveryNeverRefreshedRow_TheTimeOfTheBackfill()
    {
        using var context = new TestDb();
        var now = DateTime.UtcNow;

        var withOldFiles = Book("Added Years Ago", "One Author", null);
        var searchedLongAgo = Book("Searched Long Ago", "Two Author", null);
        searchedLongAgo.LastSearchTime = now.AddDays(-100);
        var undated = Book("No Dates At All", "Three Author", null);
        var alreadyRefreshed = Book("Refreshed Last Week", "Four Author", now.AddDays(-7));
        context.Db.Audiobooks.AddRange(withOldFiles, searchedLongAgo, undated, alreadyRefreshed);
        await context.Db.SaveChangesAsync();

        context.Db.AudiobookFiles.Add(new AudiobookFile
        {
            AudiobookId = withOldFiles.Id,
            Path = "/library/added-years-ago/book.m4b",
            CreatedAt = now.AddYears(-3)
        });
        await context.Db.SaveChangesAsync();

        var repository = new AudiobookRepository(context.Db);
        var written = await repository.BackfillMetadataRefreshTimestampsAsync();

        Assert.Equal(3, written);
        context.Db.ChangeTracker.Clear();
        var rows = await context.Db.Audiobooks.AsNoTracking().ToDictionaryAsync(
            book => book.Title!,
            book => book.LastMetadataRefreshAt);

        // Every backfilled row gets this moment, whatever its own dates say. Seeding from a
        // file's CreatedAt or from LastSearchTime put rows in before the staleness window and so
        // made them due on the first cycle after the upgrade, which is the opposite of starting
        // the clock.
        foreach (var title in new[] { "Added Years Ago", "Searched Long Ago", "No Dates At All" })
        {
            Assert.Equal(now, rows[title]!.Value, TimeSpan.FromMinutes(5));
        }

        // The control. A row that already has a timestamp is not touched, however old its files
        // are, or every restart would push the whole library back to the queue head.
        Assert.Equal(now.AddDays(-7), rows["Refreshed Last Week"]!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    [Trait("Scenario", "AnUpgradedLibraryGetsAFullStalenessWindow")]
    public async Task Backfill_LeavesNothingDue_EvenWhenEveryBookPredatesTheStalenessWindow()
    {
        using var context = new TestDb();
        var now = DateTime.UtcNow;

        var ancient = Book("Ancient", "One Author", null);
        ancient.LastSearchTime = now.AddYears(-4);
        var alsoAncient = Book("Also Ancient", "Two Author", null);
        alsoAncient.LastSearchTime = now.AddYears(-2);
        context.Db.Audiobooks.AddRange(ancient, alsoAncient);
        await context.Db.SaveChangesAsync();

        context.Db.AudiobookFiles.Add(new AudiobookFile
        {
            AudiobookId = ancient.Id,
            Path = "/library/ancient/book.m4b",
            CreatedAt = now.AddYears(-4)
        });
        await context.Db.SaveChangesAsync();

        var repository = new AudiobookRepository(context.Db);
        await repository.BackfillMetadataRefreshTimestampsAsync();
        context.Db.ChangeTracker.Clear();

        // The whole point of the backfill. An upgraded library is entitled to one full staleness
        // window of quiet, and then to age into the queue; it is not entitled to schedule itself
        // in its entirety on the first cycle after the upgrade.
        var due = await repository.GetAudiobooksDueForMetadataRefreshAsync(
            staleBefore: now.AddDays(-30),
            limit: 100);

        Assert.Empty(due);
    }

    [Fact]
    [Trait("Scenario", "BackfillIsANoOpOnASecondStart")]
    public async Task Backfill_WritesNothing_WhenEveryRowAlreadyHasATimestamp()
    {
        using var context = new TestDb();
        context.Db.Audiobooks.AddRange(
            Book("One", "One Author", null),
            Book("Two", "Two Author", null));
        await context.Db.SaveChangesAsync();
        var repository = new AudiobookRepository(context.Db);

        Assert.Equal(2, await repository.BackfillMetadataRefreshTimestampsAsync());

        // It runs on every start, so a second pass that wrote again would move the whole library
        // forward each restart and books would never come due.
        context.Db.ChangeTracker.Clear();
        Assert.Equal(0, await repository.BackfillMetadataRefreshTimestampsAsync());
    }

    [Fact]
    [Trait("Scenario", "NullsFirstThenOldest")]
    public async Task DueForRefresh_OrdersNeverRefreshedFirst_ThenOldest()
    {
        using var context = new TestDb();
        var now = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
        context.Db.Audiobooks.AddRange(
            Book("Refreshed Yesterday", "One Author", now.AddDays(-1)),
            Book("Never Refreshed", "Two Author", null),
            Book("Refreshed Long Ago", "Three Author", now.AddDays(-100)));
        await context.Db.SaveChangesAsync();
        var repository = new AudiobookRepository(context.Db);

        var due = await repository.GetAudiobooksDueForMetadataRefreshAsync(
            staleBefore: now.AddDays(-30),
            limit: 10);

        Assert.Equal(
            ["Never Refreshed", "Refreshed Long Ago"],
            due.Select(candidate => context.Db.Audiobooks
                .Single(book => book.Id == candidate.AudiobookId).Title));
    }

    [Fact]
    [Trait("Scenario", "LimitIsHonoured")]
    public async Task DueForRefresh_StopsAtTheLimit()
    {
        using var context = new TestDb();
        for (var i = 0; i < 5; i++)
        {
            context.Db.Audiobooks.Add(Book($"Book {i}", "One Author", null));
        }

        await context.Db.SaveChangesAsync();
        var repository = new AudiobookRepository(context.Db);

        var due = await repository.GetAudiobooksDueForMetadataRefreshAsync(
            staleBefore: DateTime.UtcNow,
            limit: 2);

        Assert.Equal(2, due.Count);
    }

    [Fact]
    [Trait("Scenario", "CandidateCarriesThePrimaryAuthor")]
    public async Task DueForRefresh_ReportsThePrimaryAuthor_SoAnAuthorCanBeGroupedTogether()
    {
        using var context = new TestDb();
        context.Db.Audiobooks.Add(Book("Grouped Book", "Grouping Author", null));
        await context.Db.SaveChangesAsync();
        var repository = new AudiobookRepository(context.Db);

        var due = await repository.GetAudiobooksDueForMetadataRefreshAsync(
            staleBefore: DateTime.UtcNow,
            limit: 10);

        Assert.Equal("Grouping Author", Assert.Single(due).PrimaryAuthor);
    }

    [Fact]
    [Trait("Scenario", "AuthorMatchIgnoresPunctuationAndCase")]
    public async Task IdsByAuthorName_MatchesOnTheNormalizedName()
    {
        using var context = new TestDb();
        context.Db.Audiobooks.AddRange(
            Book("Matching One", "A. Writer", null),
            Book("Matching Two", "a writer", null),
            Book("Not Matching", "Someone Else", null));
        await context.Db.SaveChangesAsync();
        var repository = new AudiobookRepository(context.Db);

        var ids = await repository.GetAudiobookIdsByAuthorNameAsync("A Writer");

        Assert.Equal(2, ids.Count);
        Assert.All(
            ids,
            id => Assert.StartsWith(
                "Matching",
                context.Db.Audiobooks.Single(book => book.Id == id).Title,
                StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Scenario", "AuthorMatchIsNarrowedInTheDatabase")]
    public async Task IdsByAuthorName_NarrowsInSql_RatherThanReadingEveryAuthorList()
    {
        using var context = new TestDb();
        context.Db.Audiobooks.AddRange(
            Book("Matching", "A. Writer", null),
            Book("Not Matching", "Someone Else", null),
            Book("Also Not Matching", "Another Person Entirely", null));
        await context.Db.SaveChangesAsync();
        var repository = new AudiobookRepository(context.Db);
        context.Recorder.Commands.Clear();

        var ids = await repository.GetAudiobookIdsByAuthorNameAsync("A Writer");

        Assert.Single(ids);

        // The match is narrowed by SQLite over the expanded JSON list.
        Assert.Contains(context.Recorder.Commands, sql => sql.Contains("json_each", StringComparison.Ordinal));

        // And nothing reads the author column of the whole table. Pulling every row back to
        // deserialize its author JSON in memory cost the library on every per-author trigger.
        Assert.DoesNotContain(
            context.Recorder.Commands,
            sql => sql.Contains("\"Authors\"", StringComparison.Ordinal)
                && !sql.Contains("WHERE", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Scenario", "AuthorMatchSurvivesNonAsciiNames")]
    public async Task IdsByAuthorName_StillMatches_WhenTheNameIsNotAscii()
    {
        using var context = new TestDb();
        context.Db.Audiobooks.AddRange(
            Book("Accented", "JULES VÉRNE", null),
            Book("Not Matching", "Someone Else", null));
        await context.Db.SaveChangesAsync();
        var repository = new AudiobookRepository(context.Db);

        // SQLite's lower() is ASCII-only, so a pattern built from the accented character would
        // disagree with ToLowerInvariant and silently drop this row. The narrowing leaves
        // non-ASCII characters out and lets the C# normalizer decide.
        var ids = await repository.GetAudiobookIdsByAuthorNameAsync("jules vérne");

        Assert.Single(ids);
        Assert.Equal("Accented", context.Db.Audiobooks.Single(book => book.Id == ids[0]).Title);
    }

    [Fact]
    [Trait("Scenario", "AuthorMatchSurvivesALegacyBareStringAuthorColumn")]
    public async Task IdsByAuthorName_StillMatches_WhenTheStoredAuthorListIsNotValidJson()
    {
        using var context = new TestDb();
        context.Db.Audiobooks.AddRange(
            Book("Legacy Row", "placeholder", null),
            Book("Not Matching", "Someone Else", null));
        await context.Db.SaveChangesAsync();

        // What an upgraded database can still hold: a bare string written before the column was
        // JSON. The value converter reads it, wrapping any value that starts with a digit or
        // with t, f, n or - into a single-item list, so this row matches in C#.
        var legacyId = context.Db.Audiobooks.Single(book => book.Title == "Legacy Row").Id;
        await context.Db.Database.ExecuteSqlRawAsync(
            """UPDATE "Audiobooks" SET "Authors" = '2001 Literary Trust' WHERE "Id" = {0}""",
            legacyId);
        context.Db.ChangeTracker.Clear();

        var repository = new AudiobookRepository(context.Db);
        var ids = await repository.GetAudiobookIdsByAuthorNameAsync("2001 Literary Trust");

        // json_each cannot expand a bare string, so requiring json_valid dropped the row in SQL
        // before the C# comparison ever saw it. That is the one way the narrowing could stop
        // being a superset of what the exact match would have kept.
        Assert.Equal([legacyId], ids);
    }

    [Fact]
    [Trait("Scenario", "IdFilteredStalenessQuery")]
    public async Task FilterIdsDueForRefresh_KeepsOnlyTheStaleOnesInTheIdSet()
    {
        using var context = new TestDb();
        var now = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
        var stale = Book("Stale And In The Set", "One Author", now.AddDays(-100));
        var fresh = Book("Fresh And In The Set", "One Author", now.AddDays(-1));
        var outsideTheSet = Book("Stale But Not Asked About", "Two Author", null);
        context.Db.Audiobooks.AddRange(stale, fresh, outsideTheSet);
        await context.Db.SaveChangesAsync();
        var repository = new AudiobookRepository(context.Db);

        var due = await repository.FilterAudiobookIdsDueForMetadataRefreshAsync(
            [stale.Id, fresh.Id],
            now.AddDays(-30));

        // The author scope used to fetch every due book in the library to intersect the ids.
        // This asks the same question of three rows instead, and has to answer it the same way.
        Assert.Equal([stale.Id], due);
    }

    [Fact]
    [Trait("Scenario", "NeverRefreshedCountsAsDue")]
    public async Task FilterIdsDueForRefresh_TakesABookThatHasNeverBeenRefreshed()
    {
        using var context = new TestDb();
        var never = Book("Never Refreshed", "One Author", null);
        context.Db.Audiobooks.Add(never);
        await context.Db.SaveChangesAsync();
        var repository = new AudiobookRepository(context.Db);

        var due = await repository.FilterAudiobookIdsDueForMetadataRefreshAsync(
            [never.Id],
            DateTime.UtcNow.AddDays(-30));

        Assert.Equal([never.Id], due);
    }

    [Fact]
    [Trait("Scenario", "NoIdsAsksNothing")]
    public async Task FilterIdsDueForRefresh_ReturnsEmpty_WithoutQuerying_ForAnEmptyIdSet()
    {
        using var context = new TestDb();
        context.Db.Audiobooks.Add(Book("Would Be Due", "One Author", null));
        await context.Db.SaveChangesAsync();
        var repository = new AudiobookRepository(context.Db);

        // An author nobody has any books for is the common case for this call, and it should
        // not cost a query with an empty IN clause to find that out.
        Assert.Empty(await repository.FilterAudiobookIdsDueForMetadataRefreshAsync(
            [],
            DateTime.UtcNow));
    }

    [Fact]
    [Trait("Scenario", "StampingRemovesABookFromTheQueue")]
    public async Task StampMetadataRefresh_SetsTheTimestamp_AndTheBookIsNoLongerDue()
    {
        using var context = new TestDb();
        context.Db.Audiobooks.Add(Book("Stamp Me", "One Author", null));
        await context.Db.SaveChangesAsync();
        var repository = new AudiobookRepository(context.Db);
        var id = context.Db.Audiobooks.Single().Id;
        var now = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(await repository.StampMetadataRefreshAsync(id, now));

        var due = await repository.GetAudiobooksDueForMetadataRefreshAsync(
            staleBefore: now.AddDays(-30),
            limit: 10);
        Assert.Empty(due);
    }

    [Fact]
    [Trait("Scenario", "ANewlyAddedBookIsNotStale")]
    public async Task AddAsync_StampsANewBook_SoItDoesNotSortToTheHeadOfTheQueue()
    {
        using var context = new TestDb();
        var repository = new AudiobookRepository(context.Db);
        var before = DateTime.UtcNow;

        var added = await repository.AddAsync(new Audiobook { Title = "Added Today", Authors = ["One Author"] });

        // A null timestamp reads as "never refreshed" and sorts ahead of every genuinely stale
        // book, so a book added on Tuesday was re-fetched on Tuesday, spending budget the stale
        // library needed. The metadata it was just built from came from the provider minutes ago.
        Assert.NotNull(added.LastMetadataRefreshAt);
        Assert.InRange(added.LastMetadataRefreshAt!.Value, before, DateTime.UtcNow);

        Assert.Empty(await repository.GetAudiobooksDueForMetadataRefreshAsync(
            staleBefore: DateTime.UtcNow.AddDays(-30),
            limit: 10));
    }

    [Fact]
    [Trait("Scenario", "ANewlyAddedBookIsNotStale")]
    public async Task AddAsync_LeavesATimestampTheCallerAlreadySet()
    {
        using var context = new TestDb();
        var repository = new AudiobookRepository(context.Db);
        var chosen = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        var added = await repository.AddAsync(new Audiobook
        {
            Title = "Restored From Elsewhere",
            Authors = ["One Author"],
            LastMetadataRefreshAt = chosen
        });

        // The stamp is a default, not an overwrite: an importer that knows when the metadata was
        // fetched keeps its own answer.
        Assert.Equal(chosen, added.LastMetadataRefreshAt);
    }

    [Fact]
    [Trait("Scenario", "ANewlyAddedBookIsNotStale")]
    public async Task AtomicAdd_StampsANewBook_TheSameWayTheRepositoryDoes()
    {
        using var context = new TestDb();
        var store = new EfLibraryAddCommitStore(context.Db);
        var audiobook = new Audiobook { Title = "Added Atomically", Authors = ["One Author"] };
        var before = DateTime.UtcNow;

        await store.CommitAsync(
            audiobook,
            new History { EventType = "Added", AudiobookTitle = audiobook.Title });

        // The other of the two insert sites. A book added through the atomic path is no more
        // stale than one added through the repository, and missing this one would leave half the
        // adds sorting to the front of the queue.
        Assert.NotNull(audiobook.LastMetadataRefreshAt);
        Assert.InRange(audiobook.LastMetadataRefreshAt!.Value, before, DateTime.UtcNow);
    }

    [Fact]
    [Trait("Scenario", "TheDueQueryRidesTheIndex")]
    public async Task DueForRefresh_IsPlannedOnTheCoveringIndex_WithNoTemporarySort()
    {
        using var context = new TestDb();
        context.Db.Audiobooks.AddRange(
            Book("One", "One Author", null),
            Book("Two", "Two Author", DateTime.UtcNow.AddDays(-100)));
        await context.Db.SaveChangesAsync();
        var repository = new AudiobookRepository(context.Db);
        context.Recorder.Commands.Clear();
        context.Recorder.Parameters.Clear();

        await repository.GetAudiobooksDueForMetadataRefreshAsync(
            staleBefore: DateTime.UtcNow.AddDays(-30),
            limit: 10);

        var (sql, parameters) = context.Recorder.Last("LastMetadataRefreshAt");

        // Belt and braces on the statement itself: the term this used to carry translated to a
        // synthetic first ORDER BY column, and no index can satisfy a sort that starts with one.
        Assert.DoesNotContain("IS NOT NULL", sql, StringComparison.OrdinalIgnoreCase);

        var plan = await ExplainQueryPlanAsync(context.Db, sql, parameters);

        // A non-unique SQLite index carries the rowid last, and Id is the rowid, so the index is
        // already in the order this query asks for. If it stops being used the plan grows a temp
        // B-tree, which sorts every due row in the library to hand back the first few.
        Assert.Contains("IX_Audiobooks_LastMetadataRefreshAt", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("TEMP B-TREE", plan, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ExplainQueryPlanAsync(
        ListenArrDbContext db,
        string sql,
        IEnumerable<KeyValuePair<string, object?>> parameters)
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        var plan = new System.Text.StringBuilder();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            plan.AppendLine(reader.GetString(reader.GetOrdinal("detail")));
        }

        return plan.ToString();
    }

    [Fact]
    [Trait("Scenario", "StampingAMissingBookIsFalse")]
    public async Task StampMetadataRefresh_ReturnsFalse_WhenTheBookIsGone()
    {
        using var context = new TestDb();
        var repository = new AudiobookRepository(context.Db);

        Assert.False(await repository.StampMetadataRefreshAsync(4242, DateTime.UtcNow));
    }
}
