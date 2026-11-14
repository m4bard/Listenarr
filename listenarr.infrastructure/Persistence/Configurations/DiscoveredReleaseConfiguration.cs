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

public sealed class DiscoveredReleaseConfiguration : IEntityTypeConfiguration<DiscoveredRelease>
{
    public void Configure(EntityTypeBuilder<DiscoveredRelease> builder)
    {
        builder.ToTable("DiscoveredReleases");
        builder.Property(release => release.ReleaseId).IsRequired().HasMaxLength(512);
        builder.Property(release => release.Title).IsRequired();
        builder.Property(release => release.Payload).IsRequired();
        builder.Property(release => release.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(release => release.Outcome).HasMaxLength(32);

        // The pair is the dedup rule: a feed item seen again on a later page or poll is a no-op.
        builder.HasIndex(release => new { release.IndexerId, release.ReleaseId }).IsUnique();
        // The handler reads one state at a time, oldest first, and prune deletes by state and age.
        builder.HasIndex(release => new { release.State, release.FirstSeenUtc });
        builder.HasIndex(release => release.MatchedAudiobookId);

        builder.HasOne<Indexer>()
            .WithMany()
            .HasForeignKey(release => release.IndexerId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Audiobook>()
            .WithMany()
            .HasForeignKey(release => release.MatchedAudiobookId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
