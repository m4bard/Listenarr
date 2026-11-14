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
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// A file-backed SQLite database migrated by the real migration chain, seeded with indexers,
/// for the indexer request state tests.
/// </summary>
internal sealed class IndexerRequestStateTestDatabase : IAsyncDisposable
{
    private readonly IInterceptor[] _interceptors;

    private IndexerRequestStateTestDatabase(string databasePath, IInterceptor[] interceptors)
    {
        DatabasePath = databasePath;
        ConnectionString = $"Data Source={databasePath};Pooling=False";
        _interceptors = interceptors;
        Factory = new DbContextFactory(this);
    }

    public string DatabasePath { get; }

    public string ConnectionString { get; }

    public IDbContextFactory<ListenArrDbContext> Factory { get; }

    public IReadOnlyList<int> IndexerIds { get; private set; } = [];

    public static async Task<IndexerRequestStateTestDatabase> CreateAsync(
        int indexerCount,
        string? targetMigration = null,
        params IInterceptor[] interceptors)
    {
        var databasePath = Path.Join(
            Path.GetTempPath(),
            "listenarr-tests",
            $"indexer-request-state-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var database = new IndexerRequestStateTestDatabase(databasePath, interceptors);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();

        var indexers = Enumerable.Range(1, indexerCount)
            .Select(number => new Indexer
            {
                Name = $"Indexer {number}",
                Type = "Torrent",
                Implementation = "Torznab",
                Url = $"https://indexer{number}.example.invalid"
            })
            .ToList();
        context.Indexers.AddRange(indexers);
        await context.SaveChangesAsync();
        database.IndexerIds = indexers.Select(indexer => indexer.Id).ToList();

        // The indexers are written through the current model, which can map columns an older
        // schema lacks, so an older target is reached by migrating down after seeding.
        if (targetMigration is not null)
        {
            await context.Database.MigrateAsync(targetMigration);
        }

        return database;
    }

    public ListenArrDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite(ConnectionString)
            .AddInterceptors(_interceptors)
            .Options);

    public ValueTask DisposeAsync()
    {
        if (File.Exists(DatabasePath))
        {
            File.Delete(DatabasePath);
        }

        return ValueTask.CompletedTask;
    }

    private sealed class DbContextFactory(IndexerRequestStateTestDatabase database)
        : IDbContextFactory<ListenArrDbContext>
    {
        public ListenArrDbContext CreateDbContext() => database.CreateContext();
    }
}
