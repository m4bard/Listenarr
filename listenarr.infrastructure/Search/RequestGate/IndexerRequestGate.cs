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

using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Search.RequestGate;

/// <summary>
/// The request gate: every indexer request passes through it, and it sends each one at most
/// once. In order it checks the indexer's cooldown, takes a unit of the cycle allowance, waits
/// for a concurrency slot and the pacing interval, checks the cooldown again, asks each
/// reservation, dispatches, and records what the answer means for the indexer. It never
/// retries. A request with an unattributed indexer id (0 or less) takes only the global
/// concurrency slot: it is not paced, never deferred by a cooldown and never recorded.
/// </summary>
public sealed class IndexerRequestGate : IIndexerRequestGate
{
    private readonly IndexerRequestStateCache _stateCache;
    private readonly IReadOnlyList<IIndexerRequestReservation> _reservations;
    private readonly IndexerConcurrencyLimiter _limiter;
    private readonly IndexerPacingSchedule _pacing;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IndexerRequestGate> _logger;

    public IndexerRequestGate(
        IndexerRequestStateCache stateCache,
        IEnumerable<IIndexerRequestReservation> reservations,
        TimeProvider timeProvider,
        ILogger<IndexerRequestGate> logger)
        : this(
            stateCache,
            reservations,
            new IndexerConcurrencyLimiter(
                IndexerRequestPolicy.MaxConcurrentPerIndexer,
                IndexerRequestPolicy.MaxConcurrentGlobal),
            new IndexerPacingSchedule(IndexerRequestPolicy.Pacing, timeProvider),
            timeProvider,
            logger)
    {
    }

    internal IndexerRequestGate(
        IndexerRequestStateCache stateCache,
        IEnumerable<IIndexerRequestReservation> reservations,
        IndexerConcurrencyLimiter limiter,
        IndexerPacingSchedule pacing,
        TimeProvider timeProvider,
        ILogger<IndexerRequestGate> logger)
    {
        _stateCache = stateCache ?? throw new ArgumentNullException(nameof(stateCache));
        _reservations = reservations?.ToList() ?? throw new ArgumentNullException(nameof(reservations));
        _limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
        _pacing = pacing ?? throw new ArgumentNullException(nameof(pacing));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IndexerRequestResult> SendAsync(
        IndexerRequestContext context,
        Func<CancellationToken, Task<HttpResponseMessage>> dispatch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dispatch);

        var indexerId = context.IndexerId;
        var attributed = !IndexerConcurrencyLimiter.IsUnattributed(indexerId);
        if (attributed && await GetStateDeferralAsync(indexerId, cancellationToken) is { } stateDeferral)
        {
            return new IndexerRequestResult.Deferred(stateDeferral);
        }

        if (context.CycleAllowance is { } allowance && !allowance.TryTake())
        {
            return new IndexerRequestResult.Deferred(new RequestDeferral(UtcNow, RequestDeferralReason.CycleRequestBound));
        }

        // The lease is disposed on every path out, so a deferral, a throw or a cancellation
        // below never keeps the slot.
        using var lease = await _limiter.AcquireAsync(indexerId, context.Origin, cancellationToken);
        await _pacing.WaitForStartAsync(indexerId, cancellationToken);

        // A request that queued behind one that drew a cooldown is deferred here, not sent.
        if (attributed && _stateCache.GetCooldown(indexerId, UtcNow) is { } cooldown)
        {
            return new IndexerRequestResult.Deferred(cooldown);
        }

        if (await ReserveAsync(context, cancellationToken) is { } reservationDeferral)
        {
            return new IndexerRequestResult.Deferred(reservationDeferral);
        }

        return await DispatchAsync(indexerId, attributed, dispatch, cancellationToken);
    }

    public async Task<RequestDeferral?> GetDeferralAsync(
        int indexerId,
        SearchOrigin origin,
        CancellationToken cancellationToken)
    {
        var latest = IndexerConcurrencyLimiter.IsUnattributed(indexerId)
            ? null
            : await GetStateDeferralAsync(indexerId, cancellationToken);

        foreach (var reservation in _reservations)
        {
            RequestDeferral? peeked;
            try
            {
                peeked = await reservation.PeekAsync(indexerId, origin, cancellationToken);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                peeked = ReservationUnavailable(ex, reservation, indexerId);
            }

            latest = Latest(latest, peeked);
        }

        return latest;
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    private async Task<RequestDeferral?> GetStateDeferralAsync(int indexerId, CancellationToken cancellationToken)
    {
        if (!await _stateCache.EnsureLoadedAsync(SeedPacing, cancellationToken))
        {
            return new RequestDeferral(
                UtcNow + IndexerRequestPolicy.UnavailableRetry,
                RequestDeferralReason.RequestStateUnavailable);
        }

        return _stateCache.GetCooldown(indexerId, UtcNow);
    }

    private void SeedPacing(IReadOnlyList<IndexerRequestState> rows)
    {
        foreach (var row in rows)
        {
            if (row.LastDispatchUtc is { } lastDispatch)
            {
                _pacing.Seed(row.IndexerId, new DateTimeOffset(DateTime.SpecifyKind(lastDispatch, DateTimeKind.Utc)));
            }
        }
    }

    private async Task<RequestDeferral?> ReserveAsync(IndexerRequestContext context, CancellationToken cancellationToken)
    {
        foreach (var reservation in _reservations)
        {
            RequestDeferral? deferral;
            try
            {
                deferral = await reservation.TryReserveAsync(context, cancellationToken);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                return ReservationUnavailable(ex, reservation, context.IndexerId);
            }

            if (deferral is not null)
            {
                return deferral;
            }
        }

        return null;
    }

    private RequestDeferral ReservationUnavailable(Exception ex, IIndexerRequestReservation reservation, int indexerId)
    {
        _logger.LogWarning(
            ex,
            "Request reservation {Reservation} failed for indexer {IndexerId}; the request is deferred",
            reservation.GetType().Name,
            indexerId);
        return new RequestDeferral(
            UtcNow + IndexerRequestPolicy.UnavailableRetry,
            RequestDeferralReason.ReservationUnavailable);
    }

    private async Task<IndexerRequestResult> DispatchAsync(
        int indexerId,
        bool attributed,
        Func<CancellationToken, Task<HttpResponseMessage>> dispatch,
        CancellationToken cancellationToken)
    {
        var dispatchStartUtc = UtcNow;
        HttpResponseMessage response;
        try
        {
            response = await dispatch(cancellationToken);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            if (attributed)
            {
                await _stateCache.ApplyAsync(new IndexerRequestTransition(
                    indexerId, IndexerRequestOutcome.Failed, dispatchStartUtc, UtcNow));
            }

            throw;
        }

        var outcome = IndexerResponseClassifier.Classify(response);
        if (outcome == IndexerRequestOutcome.RateLimited)
        {
            var observedUtc = UtcNow;
            var retryAfter = RetryAfterParser.Parse(response.Headers.RetryAfter, _timeProvider);
            _logger.LogWarning(
                "Indexer {IndexerId} rate limited the request with status {Status}",
                indexerId,
                (int)response.StatusCode);
            response.Dispose();

            var cooldown = attributed
                ? await _stateCache.ApplyAsync(new IndexerRequestTransition(
                    indexerId, IndexerRequestOutcome.RateLimited, dispatchStartUtc, observedUtc, retryAfter))
                : null;
            return new IndexerRequestResult.Deferred(cooldown ?? new RequestDeferral(
                observedUtc + (retryAfter ?? IndexerRequestPolicy.RateLimitCooldown(0)),
                RequestDeferralReason.RateLimited));
        }

        if (attributed && outcome is { } recorded)
        {
            await _stateCache.ApplyAsync(new IndexerRequestTransition(indexerId, recorded, dispatchStartUtc, UtcNow));
        }

        return new IndexerRequestResult.Sent(response);
    }

    /// <summary>
    /// Connection errors and timeouts count against the indexer. A timeout is an
    /// <see cref="OperationCanceledException"/> while the caller's own token is not cancelled;
    /// the caller's cancellation is decided by the token, never by the exception type.
    /// </summary>
    private static bool IsTransportFailure(Exception ex, CancellationToken cancellationToken)
        => ex is HttpRequestException or IOException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private static RequestDeferral? Latest(RequestDeferral? current, RequestDeferral? candidate)
        => candidate is { } next && (current is not { } existing || next.UntilUtc > existing.UntilUtc)
            ? candidate
            : current;
}
