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

public sealed class IndexerCoverageGapConfiguration : IEntityTypeConfiguration<IndexerCoverageGap>
{
    public void Configure(EntityTypeBuilder<IndexerCoverageGap> builder)
    {
        builder.ToTable("IndexerCoverageGaps");
        // Status lists an indexer's newest gaps first.
        builder.HasIndex(gap => new { gap.IndexerId, gap.DetectedUtc });
        builder.HasOne<Indexer>()
            .WithMany()
            .HasForeignKey(gap => gap.IndexerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
