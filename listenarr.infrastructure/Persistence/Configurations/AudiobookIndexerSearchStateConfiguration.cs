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
    public class AudiobookIndexerSearchStateConfiguration : IEntityTypeConfiguration<AudiobookIndexerSearchState>
    {
        public void Configure(EntityTypeBuilder<AudiobookIndexerSearchState> builder)
        {
            builder.ToTable("AudiobookIndexerSearchStates");

            // The schedule reads a page of books' pairs by book id, so the book leads the key.
            builder.HasKey(x => new { x.AudiobookId, x.IndexerId });

            builder.Property(x => x.NoResultCount)
                .HasDefaultValue(0);

            builder.Property(x => x.FailureCount)
                .HasDefaultValue(0);

            builder.Property(x => x.LastOutcome)
                .HasMaxLength(32)
                .IsRequired();

            builder.HasOne<Audiobook>()
                .WithMany()
                .HasForeignKey(x => x.AudiobookId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne<Indexer>()
                .WithMany()
                .HasForeignKey(x => x.IndexerId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(x => x.IndexerId);
        }
    }
}
