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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Listenarr.Infrastructure.Persistence.Configurations
{
    public class BlockedReleaseConfiguration : IEntityTypeConfiguration<BlockedRelease>
    {
        public void Configure(EntityTypeBuilder<BlockedRelease> builder)
        {
            // One entry per release per book, and the lookup on the search path is always
            // "what is blocked for this book", so the pair is both the uniqueness rule and
            // the access path.
            //
            // Here rather than in ListenArrDbContext.OnModelCreating, which asks for exactly
            // that: "prefer moving indexes into configuration classes". It also keeps this
            // branch's edit to that file down to the one DbSet line, which matters because
            // every open PR adding a table touches it.
            builder
                .HasIndex(entry => new { entry.AudiobookId, entry.ReleaseIdentifier })
                .IsUnique();
        }
    }
}
