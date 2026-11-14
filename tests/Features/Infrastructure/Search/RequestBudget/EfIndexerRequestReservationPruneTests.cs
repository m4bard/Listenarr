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

using System.Collections.Concurrent;
using System.Data.Common;
using Listenarr.Infrastructure.Search.RequestBudget;
using Listenarr.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static Listenarr.Tests.Features.Infrastructure.Search.RequestBudget.IndexerRequestBudgetTestDatabase;

namespace Listenarr.Tests.Features.Infrastructure.Search.RequestBudget;

// The hourly clean-up of old usage rows: it keeps the window plus a margin, runs once per hour
// after a charge is admitted, and nothing it does or fails to do changes the admission.
[Trait("Name", "EfIndexerRequestReservationPruneTests")]
[Trait("Category", "Infrastructure")]
public sealed class EfIndexerRequestReservationPruneTests : BaseTests
{
    private static readonly DateTime Hour = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Prune_NeverDeletesACountedRow_OlderRowsGo()
    {
        // Given: rows on both sides of hour - 48h for two indexers, one of them still counted
        // at the window's first hour.
        await using var database = await CreateAsync();
        var indexerId = await database.AddIndexerAsync(10);
        var otherIndexerId = await database.AddIndexerAsync(null);
        await database.AddUsageAsync(indexerId, Hour.AddHours(-72), 9);
        await database.AddUsageAsync(indexerId, Hour.AddHours(-49), 9);
        await database.AddUsageAsync(indexerId, Hour.AddHours(-48), 9);
        await database.AddUsageAsync(indexerId, Hour.AddHours(-24), 3);
        await database.AddUsageAsync(otherIndexerId, Hour.AddHours(-49), 9);
        await database.AddUsageAsync(otherIndexerId, Hour.AddHours(-30), 9);

        // When
        var deferral = await database.ReserveAsync(indexerId, SearchOrigin.Interactive);

        // Then: everything from hour - 48h on is kept, so the window still holds 3 + 1.
        Assert.Null(deferral);
        Assert.Equal(
            [
                (indexerId, Hour.AddHours(-48), 9),
                (indexerId, Hour.AddHours(-24), 3),
                (indexerId, Hour, 1),
                (otherIndexerId, Hour.AddHours(-30), 9)
            ],
            Rows(await database.UsageAsync()));
        Assert.Equal(6, await database.ReserveUntilRefusedAsync(indexerId, SearchOrigin.Interactive));
    }

    [Fact]
    public async Task Prune_Failing_StillAdmitsAndLogsAWarning()
    {
        // Given
        await using var database = await CreateAsync();
        var indexerId = await database.AddIndexerAsync(10);
        await database.AddUsageAsync(indexerId, Hour.AddHours(-49), 9);
        var probe = new PruneProbe { DeleteFailure = () => new InvalidOperationException("simulated storage failure") };
        await using var provider = database.CreateProvider(probe);
        var logger = new RecordingLogger();
        var reservation = new EfIndexerRequestReservation(
            provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>(),
            database.Clock,
            logger);

        // When
        var deferral = await database.ReserveAsync(indexerId, SearchOrigin.Sweep, reservation: reservation);

        // Then
        Assert.Null(deferral);
        Assert.Equal(1, probe.Deletes);
        Assert.Equal([LogLevel.Warning], logger.Levels);
        Assert.Equal(
            [(indexerId, Hour.AddHours(-49), 9), (indexerId, Hour, 1)],
            Rows(await database.UsageAsync()));
    }

    [Fact]
    public async Task Prune_IgnoresTheCallersCancellation()
    {
        // Given: the caller's token is cancelled once the charge has been written, before the
        // prune starts.
        await using var database = await CreateAsync();
        var indexerId = await database.AddIndexerAsync(10);
        await database.AddUsageAsync(indexerId, Hour.AddHours(-49), 9);
        using var cancellation = new CancellationTokenSource();
        var probe = new PruneProbe { AfterCharge = cancellation.Cancel };
        await using var provider = database.CreateProvider(probe);
        var reservation = provider.GetRequiredService<IIndexerRequestReservation>();

        // When
        var deferral = await reservation.TryReserveAsync(
            new IndexerRequestContext(indexerId, SearchOrigin.Sweep, IndexerRequestKind.Search),
            cancellation.Token);

        // Then
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Null(deferral);
        Assert.Equal([(indexerId, Hour, 1)], Rows(await database.UsageAsync()));
    }

    [Fact]
    public async Task Prune_RunsOncePerHour()
    {
        // Given
        await using var database = await CreateAsync();
        var indexerId = await database.AddIndexerAsync(10);
        var probe = new PruneProbe();
        await using var provider = database.CreateProvider(probe);
        var reservation = provider.GetRequiredService<IIndexerRequestReservation>();
        Assert.Null(await database.ReserveAsync(indexerId, SearchOrigin.Sweep, reservation: reservation));
        await database.AddUsageAsync(indexerId, Hour.AddHours(-49), 9);

        // When: a second charge in the same hour, then one in the next hour.
        Assert.Null(await database.ReserveAsync(indexerId, SearchOrigin.Sweep, reservation: reservation));
        var deletesInTheFirstHour = probe.Deletes;
        var rowsInTheFirstHour = Rows(await database.UsageAsync());
        database.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Null(await database.ReserveAsync(indexerId, SearchOrigin.Sweep, reservation: reservation));

        // Then
        Assert.Equal(1, deletesInTheFirstHour);
        Assert.Equal([(indexerId, Hour.AddHours(-49), 9), (indexerId, Hour, 2)], rowsInTheFirstHour);
        Assert.Equal(2, probe.Deletes);
        Assert.Equal([(indexerId, Hour, 2), (indexerId, Hour.AddHours(1), 1)], Rows(await database.UsageAsync()));
    }

    [Fact]
    public async Task Prune_ConcurrentChargesInANewHour_DeleteOnce()
    {
        // Given
        await using var database = await CreateAsync(writeAheadLog: true);
        var indexerId = await database.AddIndexerAsync(100);
        var probe = new PruneProbe();
        await using var provider = database.CreateProvider(probe);
        var reservation = provider.GetRequiredService<IIndexerRequestReservation>();

        // When
        var deferrals = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ =>
            Task.Run(() => database.ReserveAsync(indexerId, SearchOrigin.Sweep, reservation: reservation))));

        // Then
        Assert.All(deferrals, deferral => Assert.Null(deferral));
        Assert.Equal(1, probe.Deletes);
        Assert.Equal(32, await database.SpentAsync(indexerId));
    }

    [Fact]
    public async Task Prune_NotAdmitted_DoesNotRun()
    {
        // Given: an indexer with no limit, an uncharged kind and an exhausted indexer.
        await using var database = await CreateAsync();
        var unlimitedId = await database.AddIndexerAsync(null);
        var exhaustedId = await database.AddIndexerAsync(1);
        await database.AddUsageAsync(exhaustedId, Hour, 1);
        await database.AddUsageAsync(exhaustedId, Hour.AddHours(-49), 9);
        var probe = new PruneProbe();
        await using var provider = database.CreateProvider(probe);
        var reservation = provider.GetRequiredService<IIndexerRequestReservation>();

        // When
        var unlimited = await database.ReserveAsync(unlimitedId, SearchOrigin.Sweep, reservation: reservation);
        var uncharged = await database.ReserveAsync(
            exhaustedId,
            SearchOrigin.Sweep,
            IndexerRequestKind.DownloadFetch,
            reservation);
        var refused = await database.ReserveAsync(exhaustedId, SearchOrigin.Interactive, reservation: reservation);

        // Then
        Assert.Null(unlimited);
        Assert.Null(uncharged);
        Assert.NotNull(refused);
        Assert.Equal(0, probe.Deletes);
        Assert.Equal(2, (await database.UsageAsync()).Count);
    }

    private static List<(int IndexerId, DateTime HourStartUtc, int Count)> Rows(IEnumerable<IndexerRequestUsage> usage) =>
        usage.Select(row => (row.IndexerId, row.HourStartUtc, row.Count)).ToList();

    // Counts the prune's deletes, can fail them, and can act once the charge has been written.
    private sealed class PruneProbe : DbCommandInterceptor
    {
        private int _deletes;

        public int Deletes => Volatile.Read(ref _deletes);

        public Func<Exception>? DeleteFailure { get; init; }

        public Action? AfterCharge { get; init; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Starts(command, "DELETE FROM \"IndexerRequestUsages\""))
            {
                Interlocked.Increment(ref _deletes);
                if (DeleteFailure is { } failure)
                {
                    throw failure();
                }
            }

            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (Starts(command, "INSERT INTO \"IndexerRequestUsages\""))
            {
                AfterCharge?.Invoke();
            }

            return ValueTask.FromResult(result);
        }

        private static bool Starts(DbCommand command, string prefix) =>
            command.CommandText.TrimStart().StartsWith(prefix, StringComparison.Ordinal);
    }

    private sealed class RecordingLogger : ILogger<EfIndexerRequestReservation>
    {
        private readonly ConcurrentQueue<LogLevel> _levels = new();

        public IReadOnlyList<LogLevel> Levels => _levels.ToArray();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _levels.Enqueue(logLevel);
    }
}
