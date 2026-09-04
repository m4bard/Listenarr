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
using Listenarr.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Repositories
{
    // Name-to-ASIN resolution off the audiobook table.
    //
    // Audiobook.AuthorAsins is a de-duplicated bag with no association back to the credited
    // names in Audiobook.Authors: enrichment skips names the metadata source cannot resolve
    // and drops repeats, so nothing lines the two lists up. Resolution used to match any
    // credited name and then return the first ASIN in the bag, which handed one author's
    // identifier to every co-author on the same book. That value then reached MonitoredAuthors
    // through the author catalog and stuck, because the sync path never clears a stale ASIN.
    //
    // Book and author fixtures below come from the public-domain test corpus. ASIN values are
    // deliberately shaped so they cannot be mistaken for real Audible identifiers.
    [Trait("Name", "AudiobookRepository_AuthorAsinAttributionTests")]
    [Trait("Category", "Infrastructure")]
    public class AudiobookRepository_AuthorAsinAttributionTests : BaseTests
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

        private static Audiobook Book(string title, string[] authors, string[] authorAsins) =>
            new()
            {
                Title = title,
                Authors = authors.ToList(),
                AuthorAsins = authorAsins.ToList()
            };

        [Fact]
        public async Task CoAuthorDoesNotInheritTheOtherAuthorsAsin()
        {
            using var ctx = new TestDb();
            ctx.Db.Audiobooks.Add(Book(
                "The Odyssey",
                ["Homer", "Samuel Butler - translator"],
                ["ASIN-HOMER"]));
            await ctx.Db.SaveChangesAsync();

            var repository = new AudiobookRepository(ctx.Db);

            Assert.Null(await repository.GetAuthorAsinByNameAsync("Samuel Butler - translator"));
        }

        [Fact]
        public async Task NeitherCreditedNameClaimsTheAsinWhenTheBookCannotAttributeIt()
        {
            using var ctx = new TestDb();
            ctx.Db.Audiobooks.Add(Book(
                "Crime and Punishment",
                ["Fyodor Dostoevsky", "Constance Garnett - translator"],
                ["ASIN-DOSTOEVSKY"]));
            await ctx.Db.SaveChangesAsync();

            var repository = new AudiobookRepository(ctx.Db);

            Assert.Null(await repository.GetAuthorAsinByNameAsync("Fyodor Dostoevsky"));
            Assert.Null(await repository.GetAuthorAsinByNameAsync("Constance Garnett - translator"));
        }

        [Fact]
        public async Task TwoNamesOnTheSameBookDoNotResolveToOneIdentifier()
        {
            using var ctx = new TestDb();
            ctx.Db.Audiobooks.Add(Book(
                "Around the World in Eighty Days",
                ["Jules Verne", "George Makepeace Towle - translator"],
                ["ASIN-VERNE", "ASIN-TOWLE"]));
            ctx.Db.Audiobooks.Add(Book(
                "Faust. Weltliteratur fuer Kinder",
                ["Johann Wolfgang von Goethe", "Barbara Kindermann"],
                ["ASIN-GOETHE"]));
            await ctx.Db.SaveChangesAsync();

            var repository = new AudiobookRepository(ctx.Db);

            var verne = await repository.GetAuthorAsinByNameAsync("Jules Verne");
            var towle = await repository.GetAuthorAsinByNameAsync("George Makepeace Towle - translator");
            var goethe = await repository.GetAuthorAsinByNameAsync("Johann Wolfgang von Goethe");
            var kindermann = await repository.GetAuthorAsinByNameAsync("Barbara Kindermann");

            Assert.Null(verne);
            Assert.Null(towle);
            Assert.Null(goethe);
            Assert.Null(kindermann);
        }

        // A pen name credited beside the legal name is one person, so sharing an identifier is
        // arguably right here. The book still cannot say so: nothing in the row distinguishes
        // this from two unrelated co-authors. Resolution declines rather than guessing, and the
        // name is looked up against the metadata source on its own instead.
        [Fact]
        public async Task PenNameAndLegalNameOnOneBookAreStillNotAttributable()
        {
            using var ctx = new TestDb();
            ctx.Db.Audiobooks.Add(Book(
                "O. Henry: Complete Short Stories Collection",
                ["O. Henry", "William Sydney Porter"],
                ["ASIN-OHENRY"]));
            await ctx.Db.SaveChangesAsync();

            var repository = new AudiobookRepository(ctx.Db);

            Assert.Null(await repository.GetAuthorAsinByNameAsync("O. Henry"));
            Assert.Null(await repository.GetAuthorAsinByNameAsync("William Sydney Porter"));
        }

        [Fact]
        public async Task SingleAuthorBookStillResolvesItsOwnAsin()
        {
            using var ctx = new TestDb();
            ctx.Db.Audiobooks.Add(Book("The Iliad", ["Homer"], ["ASIN-HOMER"]));
            await ctx.Db.SaveChangesAsync();

            var repository = new AudiobookRepository(ctx.Db);

            Assert.Equal("ASIN-HOMER", await repository.GetAuthorAsinByNameAsync("Homer"));
        }

        [Fact]
        public async Task SingleAuthorBookResolvesEvenWhenAMultiAuthorBookIsScannedFirst()
        {
            using var ctx = new TestDb();
            ctx.Db.Audiobooks.Add(Book(
                "The Odyssey",
                ["Homer", "Samuel Butler - translator"],
                ["ASIN-HOMER"]));
            ctx.Db.Audiobooks.Add(Book(
                "The Way of All Flesh",
                ["Samuel Butler - translator"],
                ["ASIN-BUTLER"]));
            await ctx.Db.SaveChangesAsync();

            var repository = new AudiobookRepository(ctx.Db);

            Assert.Equal(
                "ASIN-BUTLER",
                await repository.GetAuthorAsinByNameAsync("Samuel Butler - translator"));
        }

        // A single credited name whose bag holds more than one identifier cannot be attributed
        // either: the extra entries have no owner, so there is no reason to prefer the first.
        [Fact]
        public async Task SingleAuthorBookWithSeveralAsinsIsNotAttributable()
        {
            using var ctx = new TestDb();
            ctx.Db.Audiobooks.Add(Book("The Iliad", ["Homer"], ["ASIN-HOMER", "ASIN-UNKNOWN"]));
            await ctx.Db.SaveChangesAsync();

            var repository = new AudiobookRepository(ctx.Db);

            Assert.Null(await repository.GetAuthorAsinByNameAsync("Homer"));
        }

        [Fact]
        public async Task RepeatedCreditOfOneNameStillResolves()
        {
            using var ctx = new TestDb();
            ctx.Db.Audiobooks.Add(Book("The Iliad", ["Homer", "homer"], ["ASIN-HOMER"]));
            await ctx.Db.SaveChangesAsync();

            var repository = new AudiobookRepository(ctx.Db);

            Assert.Equal("ASIN-HOMER", await repository.GetAuthorAsinByNameAsync("Homer"));
        }
    }
}
