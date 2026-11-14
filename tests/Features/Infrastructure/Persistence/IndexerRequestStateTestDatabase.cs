/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

/// <summary>
/// A file-backed SQLite database migrated by the real migration chain, seeded with indexers,
/// for the indexer request state tests. Connection pooling is off unless a test asks for it;
/// production keeps Microsoft.Data.Sqlite's default, which pools.
/// </summary>
internal sealed class IndexerRequestStateTestDatabase : IAsyncDisposable
{
    private readonly IInterceptor[] _interceptors;

    private IndexerRequestStateTestDatabase(string databasePath, bool pooling, IInterceptor[] interceptors)
    {
        DatabasePath = databasePath;
        ConnectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = pooling }.ToString();
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
        bool pooling = false,
        params IInterceptor[] interceptors)
    {
        var databasePath = Path.Join(
            Path.GetTempPath(),
            "listenarr-tests",
            $"indexer-request-state-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var database = new IndexerRequestStateTestDatabase(databasePath, pooling, interceptors);
        await using var context = database.CreateContext();
        if (targetMigration is null)
        {
            await context.Database.MigrateAsync();
        }
        else
        {
            await context.Database.MigrateAsync(targetMigration);
        }

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
        return database;
    }

    public ListenArrDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite(ConnectionString)
            .AddInterceptors(_interceptors)
            .Options);

    public ValueTask DisposeAsync()
    {
        // A pooled connection keeps the file open after its context is disposed.
        using (var connection = new SqliteConnection(ConnectionString))
        {
            SqliteConnection.ClearPool(connection);
        }

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
