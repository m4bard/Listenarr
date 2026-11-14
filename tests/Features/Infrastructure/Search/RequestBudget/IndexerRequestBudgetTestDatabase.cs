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

using Listenarr.Infrastructure.DependencyInjection.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static Listenarr.Tests.Features.Infrastructure.Search.RequestGate.IndexerRequestGateHarness;

namespace Listenarr.Tests.Features.Infrastructure.Search.RequestBudget;

/// <summary>
/// A file-backed SQLite database migrated by the real migration chain, with the reservation
/// resolved from the application's own registrations: the context factory as production
/// configures it and the search infrastructure that registers the reservation. Each
/// CreateProvider call stands for another process over the same file.
/// </summary>
internal sealed class IndexerRequestBudgetTestDatabase : IAsyncDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 3, 1, 10, 59, 0, TimeSpan.Zero);

    private readonly List<ServiceProvider> _providers = [];

    private IndexerRequestBudgetTestDatabase(string databasePath)
    {
        DatabasePath = databasePath;

        // Unpooled: under the concurrency tests' load the SQLite connection pool can return a
        // native connection while another caller still has a statement running on it, and the
        // open or close that touches it fails. The reservation's caller defers that request
        // rather than admitting it.
        ConnectionString = $"Data Source={databasePath};Pooling=False";
    }

    public string DatabasePath { get; }

    public string ConnectionString { get; }

    public ManualClock Clock { get; } = new(Start);

    public IIndexerRequestReservation Reservation { get; private set; } = null!;

    public IDbContextFactory<ListenArrDbContext> Factory { get; private set; } = null!;

    public static async Task<IndexerRequestBudgetTestDatabase> CreateAsync(
        bool writeAheadLog = false,
        params IInterceptor[] interceptors)
    {
        var databasePath = Path.Join(
            Path.GetTempPath(),
            "listenarr-tests",
            $"indexer-request-budget-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var database = new IndexerRequestBudgetTestDatabase(databasePath);
        var provider = database.CreateProvider(interceptors);
        database.Factory = provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
        database.Reservation = provider.GetRequiredService<IIndexerRequestReservation>();

        await using var context = await database.Factory.CreateDbContextAsync();
        await context.Database.MigrateAsync();
        if (writeAheadLog)
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }

        return database;
    }

    public ServiceProvider CreateProvider(params IInterceptor[] interceptors) =>
        CreateProvider(Clock, interceptors);

    public ServiceProvider CreateProvider(TimeProvider clock, params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddDbContextFactory<ListenArrDbContext>(
            options => options
                .UseSqlite(ConnectionString, sqlite =>
                    sqlite.MigrationsAssembly(typeof(ListenArrDbContext).Assembly.GetName().Name))
                .AddInterceptors(interceptors),
            ServiceLifetime.Singleton);
        services.AddSearchInfrastructure();
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    public async Task<int> AddIndexerAsync(int? dailyLimit)
    {
        await using var context = await Factory.CreateDbContextAsync();
        var indexer = new Indexer
        {
            Name = "Indexer",
            Type = "Torrent",
            Implementation = "Torznab",
            Url = "https://indexer.example.invalid",
            DailyRequestLimit = dailyLimit
        };
        context.Indexers.Add(indexer);
        await context.SaveChangesAsync();
        return indexer.Id;
    }

    public async Task SetDailyLimitAsync(int indexerId, int? dailyLimit)
    {
        await using var context = await Factory.CreateDbContextAsync();
        await context.Indexers
            .Where(indexer => indexer.Id == indexerId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(indexer => indexer.DailyRequestLimit, dailyLimit));
    }

    public async Task SetReservePercentAsync(int reservePercent)
    {
        await using var context = await Factory.CreateDbContextAsync();
        context.ApplicationSettings.Add(new ApplicationSettings
        {
            Id = 1,
            IndexerInteractiveReservePercent = reservePercent
        });
        await context.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<IndexerRequestUsage>> UsageAsync()
    {
        await using var context = await Factory.CreateDbContextAsync();
        return await context.IndexerRequestUsages
            .AsNoTracking()
            .OrderBy(usage => usage.IndexerId)
            .ThenBy(usage => usage.HourStartUtc)
            .ToListAsync();
    }

    public async Task<int> SpentAsync(int indexerId) =>
        (await UsageAsync()).Where(usage => usage.IndexerId == indexerId).Sum(usage => usage.Count);

    public Task<RequestDeferral?> ReserveAsync(
        int indexerId,
        SearchOrigin origin,
        IndexerRequestKind kind = IndexerRequestKind.Search,
        IIndexerRequestReservation? reservation = null) =>
        (reservation ?? Reservation).TryReserveAsync(
            new IndexerRequestContext(indexerId, origin, kind),
            CancellationToken.None);

    /// <summary>
    /// Charges until the first refusal or <paramref name="stopAfter"/> admissions, and returns
    /// how many were admitted.
    /// </summary>
    public async Task<int> ReserveUntilRefusedAsync(
        int indexerId,
        SearchOrigin origin,
        int stopAfter = int.MaxValue,
        IIndexerRequestReservation? reservation = null)
    {
        var admitted = 0;
        while (admitted < stopAfter && await ReserveAsync(indexerId, origin, reservation: reservation) is null)
        {
            admitted++;
            Assert.True(admitted <= 100_000, "the reservation never refused");
        }

        return admitted;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }

        foreach (var path in new[] { DatabasePath, DatabasePath + "-wal", DatabasePath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
