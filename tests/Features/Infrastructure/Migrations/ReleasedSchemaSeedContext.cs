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

namespace Listenarr.Tests.Features.Infrastructure.Migrations;

/// <summary>
/// Writes current entities into a database migrated only to the released frontier
/// (20260825021432_AddWeakStorageVerifiedCleanup), before the upgrade under test runs. Columns
/// added after that frontier do not exist yet, so its model leaves them out; the upgrade then
/// gives the seeded rows those columns' migration defaults, as it would on a real install. Add
/// each later column on a seeded table here.
/// </summary>
internal sealed class ReleasedSchemaSeedContext(DbContextOptions<ListenArrDbContext> options)
    : ListenArrDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Audiobook>()
            .Ignore(audiobook => audiobook.AutomaticSearchDueUtc)
            .Ignore(audiobook => audiobook.AutomaticSearchFailureCount);
    }
}
