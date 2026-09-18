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
            // that: "prefer moving indexes into configuration classes". This class is also what
            // registers the entity at all: ApplyConfigurationsFromAssembly picks it up, so the
            // table needs no DbSet property and this branch leaves ListenArrDbContext.cs alone.
            // That file is the one every pull request adding a table has to edit, which is
            // exactly why they collide there.
            // Named here because nothing else names it. EF takes a table name from the DbSet
            // property that exposes the entity, and there is no such property; left to itself it
            // would fall back to the type name and map this to "BlockedRelease", singular, while
            // the migration creates "BlockedReleases". That mismatch is not a compile error and
            // not a test of its own either. It surfaces as every migrating test failing with
            // PendingModelChangesWarning, which is a long way from the line that caused it.
            builder.ToTable("BlockedReleases");

            builder
                .HasIndex(entry => new { entry.AudiobookId, entry.ReleaseIdentifier })
                .IsUnique();
        }
    }
}
