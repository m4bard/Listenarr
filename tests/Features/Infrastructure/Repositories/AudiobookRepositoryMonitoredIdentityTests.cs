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
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Repositories
{
    [Trait("Name", "AudiobookRepositoryMonitoredIdentityTests")]
    [Trait("Category", "Infrastructure")]
    public class AudiobookRepositoryMonitoredIdentityTests : BaseTests
    {
        [Fact]
        public async Task GetMonitoredBookIdentitiesAsync_ReturnsOnlyMonitoredBooksWithAProfile()
        {
            var options = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ListenArrDbContext(options);
            var wanted = new AudiobookBuilder()
                .WithTitle("A Princess of Mars").WithAuthor("Edgar Rice Burroughs")
                .WithAsin("B008DFUGCQ").WithMonitored().Build();
            wanted.QualityProfileId = 1;
            var unmonitored = new AudiobookBuilder()
                .WithTitle("A Princess of Mars").WithAuthor("Edgar Rice Burroughs")
                .WithAsin("B071YLS9YL").WithMonitored(false).Build();
            unmonitored.QualityProfileId = 1;
            var noProfile = new AudiobookBuilder()
                .WithTitle("A Princess of Mars").WithMonitored().Build();
            db.Audiobooks.AddRange(wanted, unmonitored, noProfile);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var identities = await new AudiobookRepository(db).GetMonitoredBookIdentitiesAsync();

            var identity = Assert.Single(identities);
            Assert.Equal(wanted.Id, identity.Id);
            Assert.Equal("A Princess of Mars", identity.Title);
            Assert.Equal(["Edgar Rice Burroughs"], identity.Authors);
            Assert.Equal("B008DFUGCQ", identity.Asin);
            Assert.Empty(db.ChangeTracker.Entries());
        }
    }
}
