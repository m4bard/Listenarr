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
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Search.RequestGate;

/// <summary>
/// The request gate's per-indexer state, loaded once from the database and then kept in memory.
/// Every change is written to the database before the in-memory copy is replaced; if the write
/// fails, the in-memory copy is replaced anyway, so a cooldown is still enforced until restart.
///
/// Changes for one indexer are serialised by that indexer's own lock: inside it the current
/// state is read, the new state computed, persisted and cached, so two answers that arrive
/// together can never lose each other's update. The lock is only taken after a request has been
/// answered, never across a dispatch or a slot wait.
/// </summary>
public sealed class IndexerRequestStateCache
{
    private readonly IIndexerRequestStateStore _store;
    private readonly ILogger<IndexerRequestStateCache> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly ConcurrentDictionary<int, CachedState> _states = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _stateLocks = new();
    private volatile bool _loaded;

    public IndexerRequestStateCache(IIndexerRequestStateStore store, ILogger<IndexerRequestStateCache> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Test seam: runs once the gate has decided what a result means, before the indexer's lock
    /// is requested.
    /// </summary>
    internal Func<IndexerRequestTransition, Task>? OnStateComputed { get; set; }

    /// <summary>
    /// Loads the stored state unless it is already loaded. A failed load leaves the cache
    /// unloaded, so the next caller tries again. A load cancelled through the caller's token
    /// throws; any other failure is logged and returns false.
    /// </summary>
    /// <param name="onLoaded">Receives the stored rows once, after the first successful load.</param>
    /// <param name="cancellationToken">The caller's own token.</param>
    /// <returns>True when the state is loaded.</returns>
    public async Task<bool> EnsureLoadedAsync(
        Action<IReadOnlyList<IndexerRequestState>> onLoaded,
        CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return true;
        }

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            if (_loaded)
            {
                return true;
            }

            IReadOnlyList<IndexerRequestState> rows;
            try
            {
                rows = await _store.LoadAllAsync(cancellationToken);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                _logger.LogError(ex, "Could not load indexer request state; indexer requests are deferred until it loads");
                return false;
            }

            foreach (var row in rows)
            {
                _states[row.IndexerId] = CachedState.FromRow(row);
            }

            onLoaded(rows);
            _loaded = true;
            return true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// The cooldown in force for the indexer at <paramref name="nowUtc"/>, carrying the reason
    /// that set it, or null.
    /// </summary>
    public RequestDeferral? GetCooldown(int indexerId, DateTime nowUtc)
        => _states.TryGetValue(indexerId, out var state) ? state.CooldownAt(nowUtc) : null;

    /// <summary>
    /// Applies one result to the indexer's state under the indexer's lock and returns the
    /// cooldown in force afterwards. Waits for the lock without a token: the request has already
    /// been answered, so its effect is recorded even if the caller has since given up.
    /// </summary>
    public async Task<RequestDeferral?> ApplyAsync(IndexerRequestTransition transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        if (OnStateComputed is { } onStateComputed)
        {
            await onStateComputed(transition);
        }

        var stateLock = _stateLocks.GetOrAdd(transition.IndexerId, _ => new SemaphoreSlim(1, 1));
        await stateLock.WaitAsync(CancellationToken.None);
        try
        {
            var current = _states.GetValueOrDefault(transition.IndexerId, CachedState.Empty);
            var next = current.Apply(transition);
            await PersistAsync(transition, current, next);
            _states[transition.IndexerId] = next;
            LogCooldownChange(transition.IndexerId, current, next);
            return next.CooldownAt(transition.ObservedUtc);
        }
        finally
        {
            stateLock.Release();
        }
    }

    private async Task PersistAsync(IndexerRequestTransition transition, CachedState current, CachedState next)
    {
        try
        {
            if (transition.Outcome == IndexerRequestOutcome.Succeeded)
            {
                // Only the count is written, so a success never shortens a cooldown in force.
                if (current.ConsecutiveFailures > 0)
                {
                    await _store.ClearConsecutiveFailuresAsync(transition.IndexerId, transition.ObservedUtc, CancellationToken.None);
                }

                return;
            }

            await _store.UpsertAsync(next.ToRow(transition.IndexerId, transition.ObservedUtc), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not save request state for indexer {IndexerId} after a {Outcome} request; it is kept in memory only until restart",
                transition.IndexerId,
                transition.Outcome);
        }
    }

    private void LogCooldownChange(int indexerId, CachedState current, CachedState next)
    {
        if (next.CooldownUntilUtc is { } untilUtc && next.CooldownUntilUtc != current.CooldownUntilUtc)
        {
            _logger.LogWarning(
                "Indexer {IndexerId} is in cooldown until {CooldownUntilUtc:o} ({Reason})",
                indexerId,
                untilUtc,
                next.CooldownReason);
        }
    }

    /// <summary>
    /// One indexer's state as last written. Immutable, so a reader outside the lock always sees
    /// a whole state. <see cref="ConsecutiveRateLimits"/> is kept in memory only.
    /// </summary>
    private sealed record CachedState(
        int ConsecutiveFailures,
        DateTime? CooldownUntilUtc,
        RequestDeferralReason CooldownReason,
        DateTime? LastDispatchUtc,
        int ConsecutiveRateLimits)
    {
        public static readonly CachedState Empty = new(0, null, RequestDeferralReason.FailureLadder, null, 0);

        public static CachedState FromRow(IndexerRequestState row)
            => new(
                row.ConsecutiveFailures,
                AsUtc(row.CooldownUntilUtc),
                Enum.TryParse<RequestDeferralReason>(row.CooldownReason, out var reason)
                    ? reason
                    : RequestDeferralReason.RateLimited,
                AsUtc(row.LastDispatchUtc),
                0);

        public RequestDeferral? CooldownAt(DateTime nowUtc)
            => CooldownUntilUtc is { } untilUtc && untilUtc > nowUtc
                ? new RequestDeferral(untilUtc, CooldownReason)
                : null;

        public CachedState Apply(IndexerRequestTransition transition)
        {
            switch (transition.Outcome)
            {
                case IndexerRequestOutcome.Succeeded:
                    return this with { ConsecutiveFailures = 0, ConsecutiveRateLimits = 0 };

                case IndexerRequestOutcome.Failed:
                    var failures = ConsecutiveFailures + 1;
                    var failed = this with
                    {
                        ConsecutiveFailures = failures,
                        LastDispatchUtc = Latest(LastDispatchUtc, transition.DispatchStartUtc)
                    };
                    return IndexerRequestPolicy.FailureCooldown(failures) is { } ladder
                        ? failed.WithCooldown(transition.ObservedUtc + ladder, RequestDeferralReason.FailureLadder)
                        : failed;

                default:
                    var delay = transition.RetryAfter ?? IndexerRequestPolicy.RateLimitCooldown(ConsecutiveRateLimits);
                    return (this with
                    {
                        ConsecutiveRateLimits = ConsecutiveRateLimits + 1,
                        LastDispatchUtc = Latest(LastDispatchUtc, transition.DispatchStartUtc)
                    }).WithCooldown(transition.ObservedUtc + delay, RequestDeferralReason.RateLimited);
            }
        }

        public IndexerRequestState ToRow(int indexerId, DateTime updatedUtc)
            => new()
            {
                IndexerId = indexerId,
                ConsecutiveFailures = ConsecutiveFailures,
                CooldownUntilUtc = CooldownUntilUtc,
                CooldownReason = CooldownUntilUtc is null ? null : CooldownReason.ToString(),
                LastDispatchUtc = LastDispatchUtc,
                UpdatedUtc = updatedUtc
            };

        // A later, shorter cooldown never replaces a longer one already set.
        private CachedState WithCooldown(DateTime untilUtc, RequestDeferralReason reason)
            => CooldownUntilUtc is { } existing && existing >= untilUtc
                ? this
                : this with { CooldownUntilUtc = untilUtc, CooldownReason = reason };

        private static DateTime Latest(DateTime? existing, DateTime candidate)
            => existing is { } value && value > candidate ? value : candidate;

        private static DateTime? AsUtc(DateTime? value)
            => value is { } stored ? DateTime.SpecifyKind(stored, DateTimeKind.Utc) : null;
    }
}
