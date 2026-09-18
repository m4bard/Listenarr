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
            // Named here because nothing else names it. EF takes a table name from the DbSet
            // property that exposes the entity, and there is no such property: this class is what
            // registers the entity at all, through ApplyConfigurationsFromAssembly. Left to
            // itself EF falls back to the type name and maps this to "BlockedRelease", singular,
            // while the migration creates "BlockedReleases". That mismatch is not a compile error
            // and has no test of its own. It surfaces as every migrating test failing with
            // PendingModelChangesWarning, a long way from the line that caused it.
            builder.ToTable("BlockedReleases");

            // The identifier is a value type on the entity and the same TEXT it always was in the
            // column. Nothing about the stored bytes changes, which is the point: this is a
            // modelling change and not a schema one.
            //
            // The model snapshot needs no edit for it either, and that is a property of the
            // generator rather than luck: EF writes the PROVIDER type into a snapshot for a
            // converted property, so this stays Property<string> there. Two existing examples in
            // this repo, DownloadProcessingJob.JobData (a dictionary behind a JSON converter) and
            // MoveScanHandoff.Status (an enum behind HasConversion<string>), both appear as
            // Property<string>. The check that this is actually true is upstream's own
            // MigrationHistory_HasNoPendingModelChanges, which fails on any drift between the
            // model and the snapshot.
            //
            // Read throws away nothing and refuses nothing; see ReleaseIdentifier.FromStorage for
            // why the read path has to be the lenient one. Write goes through Key, which throws
            // rather than storing a default.
            builder
                .Property(entry => entry.ReleaseIdentifier)
                .HasConversion(
                    identifier => identifier.Key,
                    stored => ReleaseIdentifier.FromStorage(stored));

            // One entry per release per book, and the lookup on the search path is always "what is
            // blocked for this book", so the pair is both the uniqueness rule and the access path.
            //
            // Here rather than in ListenArrDbContext.OnModelCreating, which asks for exactly that:
            // "prefer moving indexes into configuration classes".
            builder
                .HasIndex(entry => new { entry.AudiobookId, entry.ReleaseIdentifier })
                .IsUnique();
        }
    }
}
