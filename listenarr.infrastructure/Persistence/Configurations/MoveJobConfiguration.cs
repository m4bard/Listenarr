/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Listenarr.Infrastructure.Persistence.Configurations;

public sealed class MoveJobConfiguration : IEntityTypeConfiguration<MoveJob>
{
    public void Configure(EntityTypeBuilder<MoveJob> builder)
    {
        builder.Property(job => job.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(job => job.Phase).HasConversion<string>().HasMaxLength(32);
        builder.Property(job => job.FailureKind).HasConversion<string>().HasMaxLength(32);
        builder.Property(job => job.ActiveDeduplicationKey).HasMaxLength(1024);
        builder.Property(job => job.LeaseOwner).HasMaxLength(200);
        builder.Property(job => job.LeaseGeneration).HasDefaultValue(0);
        builder.HasIndex(job => job.ActiveDeduplicationKey)
            .IsUnique()
            .HasFilter("\"ActiveDeduplicationKey\" IS NOT NULL");
        builder.HasIndex(job => new { job.Status, job.NextAttemptAt, job.LeaseExpiresAt });
        builder.HasMany(job => job.Entries)
            .WithOne(entry => entry.MoveJob)
            .HasForeignKey(entry => entry.MoveJobId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(job => job.Relocation)
            .WithMany(relocation => relocation.MoveJobs)
            .HasForeignKey(job => job.RelocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(job => job.Entries).AutoInclude(false);
    }
}

public sealed class RootFolderRelocationConfiguration : IEntityTypeConfiguration<RootFolderRelocation>
{
    public void Configure(EntityTypeBuilder<RootFolderRelocation> builder)
    {
        builder.ToTable("RootFolderRelocations");
        builder.Property(relocation => relocation.Mode).HasConversion<string>().HasMaxLength(24);
        builder.Property(relocation => relocation.Status).HasConversion<string>().HasMaxLength(24);
        builder.Property(relocation => relocation.TargetCaseSensitivityMode).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(relocation => relocation.ActiveRootFolderId)
            .IsUnique()
            .HasFilter("\"ActiveRootFolderId\" IS NOT NULL");
        builder.HasOne(relocation => relocation.RootFolder)
            .WithMany(root => root.Relocations)
            .HasForeignKey(relocation => relocation.RootFolderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MoveJobEntryConfiguration : IEntityTypeConfiguration<MoveJobEntry>
{
    public void Configure(EntityTypeBuilder<MoveJobEntry> builder)
    {
        builder.ToTable("MoveJobEntries");
        builder.Property(entry => entry.EntryType).HasConversion<string>().HasMaxLength(16);
        builder.Property(entry => entry.CopyState).HasConversion<string>().HasMaxLength(16);
        builder.Property(entry => entry.CleanupState).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(entry => new { entry.MoveJobId, entry.RelativePath }).IsUnique();
    }
}
