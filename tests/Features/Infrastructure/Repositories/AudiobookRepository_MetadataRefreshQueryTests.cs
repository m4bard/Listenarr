/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
    private sealed class TestDb : IDisposable
    {
        private readonly SqliteConnection _connection;
        public ListenArrDbContext Db { get; }

        public TestDb()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            Db = new ListenArrDbContext(
                new DbContextOptionsBuilder<ListenArrDbContext>().UseSqlite(_connection).Options);
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
    [Trait("Scenario", "StampingAMissingBookIsFalse")]
    public async Task StampMetadataRefresh_ReturnsFalse_WhenTheBookIsGone()
    {
        using var context = new TestDb();
        var repository = new AudiobookRepository(context.Db);

        Assert.False(await repository.StampMetadataRefreshAsync(4242, DateTime.UtcNow));
    }
}
