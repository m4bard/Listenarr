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

namespace Listenarr.Infrastructure.Search.RequestGate;

/// <summary>
/// Caps how many indexer requests run at once, per indexer and across all indexers. A request
/// takes its indexer's slot first and the global slot second, and gives them back in reverse.
/// When requests wait, an Interactive request is served before any unattended one; within each
/// class the wait is first come, first served.
/// </summary>
public sealed class IndexerConcurrencyLimiter
{
    private readonly int _perIndexerCapacity;
    private readonly SlotPool _globalSlots;
    private readonly ConcurrentDictionary<int, SlotPool> _indexerSlots = new();

    public IndexerConcurrencyLimiter(int perIndexerCapacity, int globalCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(perIndexerCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(globalCapacity, 1);

        _perIndexerCapacity = perIndexerCapacity;
        _globalSlots = new SlotPool(globalCapacity);
    }

    /// <summary>
    /// True when the id names no saved indexer, such as an unsaved indexer under test. Such a
    /// request takes only the global slot.
    /// </summary>
    public static bool IsUnattributed(int indexerId) => indexerId <= 0;

    /// <summary>
    /// Waits for the slots the request needs and returns a lease that gives them back when
    /// disposed. An unattributed id takes the global slot only. If the wait is cancelled, the
    /// task is cancelled with the caller's token and no slot is held.
    /// </summary>
    public async Task<Lease> AcquireAsync(int indexerId, SearchOrigin origin, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var interactive = origin == SearchOrigin.Interactive;
        if (IsUnattributed(indexerId))
        {
            await _globalSlots.WaitAsync(interactive, cancellationToken);
            return new Lease(_globalSlots, indexerSlots: null);
        }

        var indexerSlots = _indexerSlots.GetOrAdd(indexerId, _ => new SlotPool(_perIndexerCapacity));
        await indexerSlots.WaitAsync(interactive, cancellationToken);
        try
        {
            await _globalSlots.WaitAsync(interactive, cancellationToken);
        }
        catch
        {
            indexerSlots.Release();
            throw;
        }

        return new Lease(_globalSlots, indexerSlots);
    }

    /// <summary>
    /// The slots one request holds. Disposing gives back the global slot, then the indexer's.
    /// Disposing again does nothing.
    /// </summary>
    public sealed class Lease : IDisposable
    {
        private readonly SlotPool _globalSlots;
        private readonly SlotPool? _indexerSlots;
        private int _released;

        internal Lease(SlotPool globalSlots, SlotPool? indexerSlots)
        {
            _globalSlots = globalSlots;
            _indexerSlots = indexerSlots;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            _globalSlots.Release();
            _indexerSlots?.Release();
        }
    }

    /// <summary>
    /// A counted set of slots with two FIFO wait queues. A released slot passes straight to the
    /// next waiter, Interactive first, so a free slot and a waiter never exist together.
    /// Granting and cancelling both happen under the pool's lock, so a waiter is either handed a
    /// slot or removed from its queue, never both.
    /// </summary>
    internal sealed class SlotPool
    {
        private readonly object _sync = new();
        private readonly LinkedList<TaskCompletionSource> _interactiveWaiters = new();
        private readonly LinkedList<TaskCompletionSource> _unattendedWaiters = new();
        private int _available;

        public SlotPool(int capacity)
        {
            _available = capacity;
        }

        public async Task WaitAsync(bool interactive, CancellationToken cancellationToken)
        {
            LinkedListNode<TaskCompletionSource> node;
            lock (_sync)
            {
                if (_available > 0)
                {
                    _available--;
                    return;
                }

                var queue = interactive ? _interactiveWaiters : _unattendedWaiters;
                node = queue.AddLast(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            }

            using (cancellationToken.Register(() => CancelWaiter(node, cancellationToken)))
            {
                await node.Value.Task;
            }
        }

        public void Release()
        {
            TaskCompletionSource? next;
            lock (_sync)
            {
                next = TakeNextWaiter(_interactiveWaiters) ?? TakeNextWaiter(_unattendedWaiters);
                if (next is null)
                {
                    _available++;
                    return;
                }
            }

            next.SetResult();
        }

        private static TaskCompletionSource? TakeNextWaiter(LinkedList<TaskCompletionSource> queue)
        {
            var first = queue.First;
            if (first is null)
            {
                return null;
            }

            queue.RemoveFirst();
            return first.Value;
        }

        private void CancelWaiter(LinkedListNode<TaskCompletionSource> node, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                // Already granted: the waiter owns the slot, and its lease gives it back.
                if (node.List is null)
                {
                    return;
                }

                node.List.Remove(node);
            }

            node.Value.SetCanceled(cancellationToken);
        }
    }
}
