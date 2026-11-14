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

using Listenarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Search.RequestBudget;

/// <summary>
/// Each indexer's rolling daily request limit, enforced in the database. The usage rows are the
/// only count: a charge is one statement that adds the request only while the window is below
/// the origin's ceiling, so concurrent callers can never take more than the ceiling between
/// them and a restart starts from the usage already spent. The gate is a singleton and so is
/// this; every call opens its own short-lived context from the factory and runs in autocommit.
/// A storage failure while charging is not caught here: the gate defers the request.
/// </summary>
public sealed class EfIndexerRequestReservation : IIndexerRequestReservation
{
    // The settings table holds one row with this key; a database without it gets the reserve a
    // new settings row would.
    private const int ApplicationSettingsId = 1;
    private const int DefaultInteractiveReservePercent = 25;

    // A peek carries no kind; it stands in for the search request it checks ahead of.
    private const IndexerRequestKind PeekKind = IndexerRequestKind.Search;

    private readonly IDbContextFactory<ListenArrDbContext> _dbContextFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EfIndexerRequestReservation> _logger;
    private readonly Lock _refusalLogLock = new();
    private readonly Dictionary<(int IndexerId, SearchOrigin Origin), DateTime> _refusalLoggedForHour = new();

    public EfIndexerRequestReservation(
        IDbContextFactory<ListenArrDbContext> dbContextFactory,
        TimeProvider timeProvider,
        ILogger<EfIndexerRequestReservation> logger)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<RequestDeferral?> TryReserveAsync(
        IndexerRequestContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!IndexerRequestBudgetKinds.IsCharged(context.Kind))
        {
            return null;
        }

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await ReadWindowAsync(db, context.IndexerId, context.Origin, cancellationToken) is not { } window)
        {
            return null;
        }

        var charged = await db.Database.ExecuteSqlInterpolatedAsync(
            IndexerRequestBudgetSql.ChargeIfBelowCeiling(
                context.IndexerId,
                window.HourStartUtc,
                window.WindowStartUtc,
                window.Ceiling),
            cancellationToken);
        if (charged != 0)
        {
            return null;
        }

        var (deferral, spent) = await RefusalAsync(db, context.IndexerId, window, cancellationToken);
        LogRefusal(context.IndexerId, context.Origin, window, spent, deferral.UntilUtc);
        return deferral;
    }

    public async Task<RequestDeferral?> PeekAsync(
        int indexerId,
        SearchOrigin origin,
        CancellationToken cancellationToken)
    {
        if (!IndexerRequestBudgetKinds.IsCharged(PeekKind))
        {
            return null;
        }

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            if (await ReadWindowAsync(db, indexerId, origin, cancellationToken) is not { } window)
            {
                return null;
            }

            var spent = await InWindow(db, indexerId, window).SumAsync(usage => usage.Count, cancellationToken);
            if (spent < window.Ceiling)
            {
                return null;
            }

            return (await RefusalAsync(db, indexerId, window, cancellationToken)).Deferral;
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            // A peek only advises; the charge before dispatch still enforces the limit.
            _logger.LogWarning(
                ex,
                "Could not read the daily request usage of indexer {IndexerId}; the check reports no limit",
                indexerId);
            return null;
        }
    }

    /// <summary>
    /// The limit, the reserve and the clock, read once for one call: null when the indexer has
    /// no limit, otherwise the ceiling for <paramref name="origin"/> and the hour every
    /// statement of the call uses.
    /// </summary>
    private async Task<BudgetWindow?> ReadWindowAsync(
        ListenArrDbContext db,
        int indexerId,
        SearchOrigin origin,
        CancellationToken cancellationToken)
    {
        var dailyLimit = await db.Indexers
            .Where(indexer => indexer.Id == indexerId)
            .Select(indexer => indexer.DailyRequestLimit)
            .SingleOrDefaultAsync(cancellationToken);
        if (dailyLimit is not { } limit || limit <= 0)
        {
            return null;
        }

        var reservePercent = await db.ApplicationSettings
            .Where(settings => settings.Id == ApplicationSettingsId)
            .Select(settings => (int?)settings.IndexerInteractiveReservePercent)
            .SingleOrDefaultAsync(cancellationToken) ?? DefaultInteractiveReservePercent;

        var hour = IndexerRequestBudgetPolicy.HourStartUtc(_timeProvider.GetUtcNow());
        return new BudgetWindow(
            limit,
            IndexerRequestBudgetPolicy.CeilingFor(limit, origin, reservePercent),
            hour,
            IndexerRequestBudgetPolicy.WindowStartUtc(hour));
    }

    private static IQueryable<IndexerRequestUsage> InWindow(
        ListenArrDbContext db,
        int indexerId,
        BudgetWindow window) =>
        db.IndexerRequestUsages
            .AsNoTracking()
            .Where(usage => usage.IndexerId == indexerId && usage.HourStartUtc >= window.WindowStartUtc);

    private static async Task<(RequestDeferral Deferral, int Spent)> RefusalAsync(
        ListenArrDbContext db,
        int indexerId,
        BudgetWindow window,
        CancellationToken cancellationToken)
    {
        var buckets = await InWindow(db, indexerId, window)
            .Select(usage => new { usage.HourStartUtc, usage.Count })
            .ToListAsync(cancellationToken);
        var until = IndexerRequestBudgetPolicy.NextAdmissionUtc(
            buckets.Select(bucket => (bucket.HourStartUtc, bucket.Count)),
            window.Ceiling,
            window.HourStartUtc);
        return (
            new RequestDeferral(until, RequestDeferralReason.AllowanceExhausted),
            buckets.Sum(bucket => bucket.Count));
    }

    // One refusal line per indexer, origin and hour, so an exhausted sweep does not log a line
    // for every remaining request. Only the last logged hour is kept per pair.
    private void LogRefusal(int indexerId, SearchOrigin origin, BudgetWindow window, int spent, DateTime untilUtc)
    {
        lock (_refusalLogLock)
        {
            var key = (indexerId, origin);
            if (_refusalLoggedForHour.TryGetValue(key, out var loggedHour) && loggedHour == window.HourStartUtc)
            {
                return;
            }

            _refusalLoggedForHour[key] = window.HourStartUtc;
        }

        _logger.LogInformation(
            "Indexer {IndexerId} has used its daily request limit for {Origin} requests ({Spent} of {Ceiling} in the last 24 hours, limit {DailyLimit}); requests are deferred until {UntilUtc:u}",
            indexerId,
            origin,
            spent,
            window.Ceiling,
            window.DailyLimit,
            untilUtc);
    }

    private readonly record struct BudgetWindow(
        int DailyLimit,
        int Ceiling,
        DateTime HourStartUtc,
        DateTime WindowStartUtc);
}
