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
using Listenarr.Domain.Search.Discovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Listenarr.Infrastructure.Persistence.Configurations;

public sealed class IndexerDiscoveryCheckpointConfiguration : IEntityTypeConfiguration<IndexerDiscoveryCheckpoint>
{
    public void Configure(EntityTypeBuilder<IndexerDiscoveryCheckpoint> builder)
    {
        builder.ToTable("IndexerDiscoveryCheckpoints");
        builder.HasKey(checkpoint => checkpoint.IndexerId);
        builder.Property(checkpoint => checkpoint.IndexerId).ValueGeneratedNever();
        builder.Property(checkpoint => checkpoint.NewestReleaseId).HasMaxLength(512);
        builder.Property(checkpoint => checkpoint.FutureDatedItemCount).HasDefaultValue(0);
        builder.Property(checkpoint => checkpoint.UndatedItemCount).HasDefaultValue(0);
        builder.HasOne<Indexer>()
            .WithOne()
            .HasForeignKey<IndexerDiscoveryCheckpoint>(checkpoint => checkpoint.IndexerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
