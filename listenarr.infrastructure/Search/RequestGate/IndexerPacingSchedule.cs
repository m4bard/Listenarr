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

namespace Listenarr.Infrastructure.Search.RequestGate;

/// <summary>
/// Spaces dispatches to the same indexer at least a minimum interval apart, measured from
/// dispatch start. Each caller reserves its start time under a lock, so callers that arrive
/// together are given distinct starts rather than all reading the same last start.
/// </summary>
public sealed class IndexerPacingSchedule
{
    private readonly object _sync = new();
    private readonly Dictionary<int, DateTimeOffset> _lastReservedStarts = new();
    private readonly TimeSpan _interval;
    private readonly TimeProvider _timeProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

    public IndexerPacingSchedule(TimeSpan interval, TimeProvider timeProvider)
        : this(
            interval,
            timeProvider,
            (delay, cancellationToken) => Task.Delay(delay, timeProvider, cancellationToken))
    {
    }

    internal IndexerPacingSchedule(
        TimeSpan interval,
        TimeProvider timeProvider,
        Func<TimeSpan, CancellationToken, Task> delayAsync)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, TimeSpan.Zero);

        _interval = interval;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _delayAsync = delayAsync ?? throw new ArgumentNullException(nameof(delayAsync));
    }

    /// <summary>
    /// Records a dispatch start this schedule did not reserve itself, such as one persisted
    /// before a restart, so the next start keeps the interval from it. A later start already
    /// reserved is kept. An unattributed id is ignored.
    /// </summary>
    public void Seed(int indexerId, DateTimeOffset dispatchStart)
    {
        if (IndexerConcurrencyLimiter.IsUnattributed(indexerId))
        {
            return;
        }

        lock (_sync)
        {
            if (!_lastReservedStarts.TryGetValue(indexerId, out var lastStart) || dispatchStart > lastStart)
            {
                _lastReservedStarts[indexerId] = dispatchStart;
            }
        }
    }

    /// <summary>
    /// Reserves the next start for the indexer, max(now, last reserved start + interval), and
    /// waits until it. An unattributed id (see <see cref="IndexerConcurrencyLimiter.IsUnattributed"/>)
    /// is not paced. A cancelled wait keeps its reservation, so the next start is never earlier
    /// than the interval allows.
    /// </summary>
    public async Task WaitForStartAsync(int indexerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IndexerConcurrencyLimiter.IsUnattributed(indexerId))
        {
            return;
        }

        TimeSpan wait;
        lock (_sync)
        {
            var now = _timeProvider.GetUtcNow();
            var start = _lastReservedStarts.TryGetValue(indexerId, out var lastStart) && lastStart + _interval > now
                ? lastStart + _interval
                : now;
            _lastReservedStarts[indexerId] = start;
            wait = start - now;
        }

        if (wait > TimeSpan.Zero)
        {
            await _delayAsync(wait, cancellationToken);
        }
    }
}
