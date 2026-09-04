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
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Persistence
{
    // Startup repair for author ASINs that were stamped onto co-authors before name-to-ASIN
    // resolution stopped attributing a book's ASIN bag to every credited name.
    //
    // The stored values do not heal on their own. AuthorMonitoringService only ever overwrites
    // an ASIN with a freshly resolved one and never clears a stale value, and the author catalog
    // returns a cached entry's ASIN without re-asking the metadata source, so a bad identifier
    // left in the cache is written straight back on the next sync.
    //
    // Author names below come from the public-domain test corpus. ASIN values are deliberately
    // shaped so they cannot be mistaken for real Audible identifiers.
    [Trait("Name", "AmbiguousAuthorAsinRepairTests")]
    [Trait("Category", "Infrastructure")]
    public class AmbiguousAuthorAsinRepairTests : BaseTests
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

        private static MonitoredAuthor Monitored(
            string name,
            string? asin,
            string region = "us",
            string language = "all") =>
            new()
            {
                AuthorName = name,
                AuthorNameNormalized = name.ToLowerInvariant(),
                AuthorAsin = asin,
                Region = region,
                Language = language
            };

        [Fact]
        public void ClearsAnAsinClaimedByMoreThanOneMonitoredName()
        {
            using var ctx = new TestDb();
            ctx.Db.MonitoredAuthors.AddRange(
                Monitored("Homer", "ASIN-HOMER"),
                Monitored("Samuel Butler - translator", "ASIN-HOMER"),
                Monitored("Jules Verne", "ASIN-VERNE"));
            ctx.Db.SaveChanges();

            var result = ListenarrDatabaseMigrationPreflight.RepairAmbiguousAuthorAsins(ctx.Db);

            Assert.Equal(2, result.MonitoredAuthorsRepaired);

            var rows = ctx.Db.MonitoredAuthors.AsNoTracking().ToList();
            Assert.Null(rows.Single(row => row.AuthorName == "Homer").AuthorAsin);
            Assert.Null(rows.Single(row => row.AuthorName == "Samuel Butler - translator").AuthorAsin);
            Assert.Equal("ASIN-VERNE", rows.Single(row => row.AuthorName == "Jules Verne").AuthorAsin);
        }

        [Fact]
        public void ClearsTheSameAsinFromTheAuthorCatalogCache()
        {
            using var ctx = new TestDb();
            ctx.Db.MonitoredAuthors.AddRange(
                Monitored("Fyodor Dostoevsky", "ASIN-DOSTOEVSKY"),
                Monitored("Constance Garnett - translator", "ASIN-DOSTOEVSKY"));
            ctx.Db.AuthorCacheEntries.AddRange(
                new AuthorCacheEntry
                {
                    AuthorName = "Constance Garnett - translator",
                    AuthorNameNormalized = "constance garnett - translator",
                    AuthorAsin = "ASIN-DOSTOEVSKY",
                    Region = "us"
                },
                new AuthorCacheEntry
                {
                    AuthorName = "Jules Verne",
                    AuthorNameNormalized = "jules verne",
                    AuthorAsin = "ASIN-VERNE",
                    Region = "us"
                });
            ctx.Db.SaveChanges();

            var result = ListenarrDatabaseMigrationPreflight.RepairAmbiguousAuthorAsins(ctx.Db);

            Assert.Equal(2, result.MonitoredAuthorsRepaired);
            Assert.Equal(1, result.CachedAuthorsRepaired);

            var cached = ctx.Db.AuthorCacheEntries.AsNoTracking().ToList();
            Assert.Null(cached.Single(entry => entry.AuthorName == "Constance Garnett - translator").AuthorAsin);
            Assert.Equal("ASIN-VERNE", cached.Single(entry => entry.AuthorName == "Jules Verne").AuthorAsin);
        }

        [Fact]
        public void LeavesTheSameNameMonitoredInTwoRegionsAlone()
        {
            using var ctx = new TestDb();
            ctx.Db.MonitoredAuthors.AddRange(
                Monitored("Jules Verne", "ASIN-VERNE"),
                Monitored("Jules Verne", "ASIN-VERNE", region: "fr"));
            ctx.Db.SaveChanges();

            var result = ListenarrDatabaseMigrationPreflight.RepairAmbiguousAuthorAsins(ctx.Db);

            Assert.Equal(0, result.MonitoredAuthorsRepaired);
            Assert.All(
                ctx.Db.MonitoredAuthors.AsNoTracking().ToList(),
                row => Assert.Equal("ASIN-VERNE", row.AuthorAsin));
        }

        // A monitored author is identified by normalized name, region and language, so one person
        // followed in two languages is two rows holding one ASIN by design. This is why the table
        // cannot carry a uniqueness constraint on AuthorAsin, and why the repair compares names
        // rather than counting rows.
        [Fact]
        public void LeavesOneNameMonitoredInTwoLanguagesAlone()
        {
            using var ctx = new TestDb();
            ctx.Db.MonitoredAuthors.AddRange(
                Monitored("Jules Verne", "ASIN-VERNE"),
                Monitored("Jules Verne", "ASIN-VERNE", language: "french"));
            ctx.Db.SaveChanges();

            var result = ListenarrDatabaseMigrationPreflight.RepairAmbiguousAuthorAsins(ctx.Db);

            Assert.Equal(0, result.MonitoredAuthorsRepaired);
            Assert.All(
                ctx.Db.MonitoredAuthors.AsNoTracking().ToList(),
                row => Assert.Equal("ASIN-VERNE", row.AuthorAsin));
        }

        [Fact]
        public void MatchesAsinsWithoutRegardToCase()
        {
            using var ctx = new TestDb();
            ctx.Db.MonitoredAuthors.AddRange(
                Monitored("Homer", "asin-homer"),
                Monitored("Samuel Butler - translator", "ASIN-HOMER"));
            ctx.Db.SaveChanges();

            var result = ListenarrDatabaseMigrationPreflight.RepairAmbiguousAuthorAsins(ctx.Db);

            Assert.Equal(2, result.MonitoredAuthorsRepaired);
        }

        [Fact]
        public void DoesNothingWhenEveryAsinBelongsToOneName()
        {
            using var ctx = new TestDb();
            ctx.Db.MonitoredAuthors.AddRange(
                Monitored("Homer", "ASIN-HOMER"),
                Monitored("Jules Verne", "ASIN-VERNE"),
                Monitored("Barbara Kindermann", null));
            ctx.Db.SaveChanges();

            var result = ListenarrDatabaseMigrationPreflight.RepairAmbiguousAuthorAsins(ctx.Db);

            Assert.Equal(0, result.MonitoredAuthorsRepaired);
            Assert.Equal(0, result.CachedAuthorsRepaired);
        }

        [Fact]
        public void SecondRunFindsNothingLeftToRepair()
        {
            using var ctx = new TestDb();
            ctx.Db.MonitoredAuthors.AddRange(
                Monitored("Homer", "ASIN-HOMER"),
                Monitored("Samuel Butler - translator", "ASIN-HOMER"));
            ctx.Db.SaveChanges();

            ListenarrDatabaseMigrationPreflight.RepairAmbiguousAuthorAsins(ctx.Db);
            var second = ListenarrDatabaseMigrationPreflight.RepairAmbiguousAuthorAsins(ctx.Db);

            Assert.Equal(0, second.MonitoredAuthorsRepaired);
            Assert.Equal(0, second.CachedAuthorsRepaired);
        }

        // A pen name monitored beside the legal name is one person and legitimately shares an
        // identifier, but nothing stored locally separates that from two unrelated authors
        // wearing the same ASIN. The value is cleared here too and re-resolved per name, which
        // costs a lookup; keeping it would leave an identifier that is wrong for every row but
        // one, and a folder rename driven off this table would then misattribute an author.
        [Fact]
        public void ClearsAPenNamePairAsWellAndSaysSoDeliberately()
        {
            using var ctx = new TestDb();
            ctx.Db.MonitoredAuthors.AddRange(
                Monitored("O. Henry", "ASIN-OHENRY"),
                Monitored("William Sydney Porter", "ASIN-OHENRY"));
            ctx.Db.SaveChanges();

            var result = ListenarrDatabaseMigrationPreflight.RepairAmbiguousAuthorAsins(ctx.Db);

            Assert.Equal(2, result.MonitoredAuthorsRepaired);
            Assert.All(
                ctx.Db.MonitoredAuthors.AsNoTracking().ToList(),
                row => Assert.Null(row.AuthorAsin));
        }
    }
}
