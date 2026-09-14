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

namespace Listenarr.Application.Search.Indexers.Common;

/// <summary>
/// The per-indexer failure backoff ladder. An indexer that stops answering is asked progressively
/// less often and eventually not at all until its cooldown expires; an indexer that answers walks
/// back down one rung per answer.
/// </summary>
public sealed class IndexerStatusService : IIndexerStatusService
{
    /// <summary>
    /// The cooldown at each rung. Readarr's ten rungs for indexers, unchanged: an operator moving
    /// between family tools gets behaviour they already know, and these are the numbers a reviewer
    /// will recognise. The day-long ceiling is deliberate and is Readarr's own choice for indexers
    /// specifically, harder than the hour it allows download clients, because an indexer that has
    /// been refusing for a day is usually one that is actively widening a ban.
    /// </summary>
    public static readonly TimeSpan[] Periods =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(60),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(3),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(12),
        TimeSpan.FromHours(24)
    ];

    /// <summary>Highest rung on the ladder.</summary>
    public static readonly int MaxLevel = Periods.Length - 1;

    /// <summary>Rung the startup window caps a block to.</summary>
    public const int StartupGraceLevel = 2;

    /// <summary>
    /// Added to every cooldown. The fan-out is parallel, so several indexers that failed in the
    /// same request would otherwise all come back in the same instant and fail together again.
    /// One-sided on purpose: a negative jitter can make a block shorter than the rung, which for a
    /// cooldown derived from a stated Retry-After means returning before the remote said to.
    /// </summary>
    public static readonly TimeSpan MaxJitter = TimeSpan.FromSeconds(5);

    private readonly IIndexerRepository _indexerRepository;
    private readonly IndexerBackoffStartupWindow _startupWindow;
    private readonly TimeProvider _timeProvider;
    private readonly IHubBroadcaster? _hubBroadcaster;
    private readonly ILogger<IndexerStatusService> _logger;

    public IndexerStatusService(
        IIndexerRepository indexerRepository,
        IndexerBackoffStartupWindow startupWindow,
        TimeProvider timeProvider,
        ILogger<IndexerStatusService> logger,
        IHubBroadcaster? hubBroadcaster = null)
    {
        _indexerRepository = indexerRepository ?? throw new ArgumentNullException(nameof(indexerRepository));
        _startupWindow = startupWindow ?? throw new ArgumentNullException(nameof(startupWindow));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hubBroadcaster = hubBroadcaster;
    }

    public async Task<IReadOnlySet<int>> GetBlockedIndexerIdsAsync(CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var indexers = await _indexerRepository.GetAllAsync(ct);

        var blocked = new HashSet<int>();
        foreach (var indexer in indexers)
        {
            if (IsBlocked(indexer, now))
            {
                blocked.Add(indexer.Id);
            }
        }

        return blocked;
    }

    public async Task<bool> AnyEnabledIndexerBlockedAsync(bool isAutomaticSearch, CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var indexers = await _indexerRepository.GetAllAsync(ct);

        // Only the indexers this kind of search would have asked. An interactive-only indexer in
        // cooldown says nothing about whether an automatic sweep was complete.
        return indexers.Any(i =>
            i.IsEnabled
            && (isAutomaticSearch ? i.EnableAutomaticSearch : i.EnableInteractiveSearch)
            && IsBlocked(i, now));
    }

    private static bool IsBlocked(Indexer indexer, DateTime now) =>
        indexer.DisabledTill is { } till && till > now;

    public async Task<IndexerBackoffState> RecordAsync(
        Indexer indexer,
        IndexerQueryObservation observation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(indexer);
        ArgumentNullException.ThrowIfNull(observation);

        var current = IndexerBackoffState.From(indexer);
        var signal = IndexerBackoffPolicy.Classify(observation);
        var now = _timeProvider.GetUtcNow();

        var next = Advance(current, signal, observation.Reason, observation.RetryAfter, now);
        if (next == current)
        {
            // The steady state: a healthy indexer answering. No write, which is what keeps the
            // recording affordable on a sweep that searches every monitored book in turn.
            return current;
        }

        await _indexerRepository.UpdateBackoffStateAsync(indexer.Id, next, ct);
        LogTransition(indexer, current, next);
        await BroadcastTransitionAsync(ct);
        return next;
    }

    /// <summary>
    /// Nudges any open settings view to re-read the indexer list, so a block appears and clears on
    /// the card without a manual refresh. A mechanism that silently mutes indexers would reproduce
    /// the defect that let the original incident run for a day unnoticed.
    /// </summary>
    /// <remarks>
    /// Deliberately carries counts only and no indexer array: the existing IndexersUpdated handler
    /// treats a payload carrying indexers as a Prowlarr import and raises an "imported N indexers"
    /// toast, which this is not. With created at zero it takes the quiet refresh path instead.
    /// </remarks>
    private async Task BroadcastTransitionAsync(CancellationToken ct)
    {
        if (_hubBroadcaster == null)
        {
            return;
        }

        try
        {
            await _hubBroadcaster.BroadcastAsync(
                RealtimeHubTarget.Settings,
                "IndexersUpdated",
                new { created = 0, skipped = 0 },
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            _logger.LogDebug(ex, "Failed to broadcast an indexer failure backoff transition");
        }
    }

    /// <summary>
    /// The arithmetic on its own, with no persistence and no clock of its own, so the ladder can be
    /// asserted rung by rung.
    /// </summary>
    public IndexerBackoffState Advance(
        IndexerBackoffState current,
        IndexerBackoffSignal signal,
        IndexerQueryReason reason,
        TimeSpan? retryAfter,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(current);

        switch (signal)
        {
            case IndexerBackoffSignal.Ignore:
                return current;

            case IndexerBackoffSignal.Success:
                return Decrement(current);

            default:
                return Escalate(current, signal, reason, retryAfter, now);
        }
    }

    private static IndexerBackoffState Decrement(IndexerBackoffState current)
    {
        if (current.EscalationLevel == 0 && current.DisabledTill == null)
        {
            return current;
        }

        // Decrement rather than reset, which is Readarr's choice and the opposite of the in-house
        // download-client ladder. An indexer that climbed to the six-hour rung and then answers one
        // request is not yet healthy, and a flapping indexer that alternates fail/succeed would
        // never accumulate under a reset, so the feature would do nothing in the case it exists for.
        var level = Math.Max(0, current.EscalationLevel - 1);

        return level == 0
            ? IndexerBackoffState.Healthy with { MostRecentFailure = current.MostRecentFailure }
            : current with { EscalationLevel = level, DisabledTill = null };
    }

    private IndexerBackoffState Escalate(
        IndexerBackoffState current,
        IndexerBackoffSignal signal,
        IndexerQueryReason reason,
        TimeSpan? retryAfter,
        DateTimeOffset now)
    {
        var utcNow = now.UtcDateTime;

        int level;
        if (current.EscalationLevel == 0)
        {
            // One strike puts the indexer on rung 1, a sixty-second block. It does not also climb:
            // a first failure landing on rung 2 is the off-by-one this is written to avoid.
            level = 1;
        }
        else if (signal == IndexerBackoffSignal.Hold)
        {
            level = current.EscalationLevel;
        }
        else
        {
            level = Math.Min(MaxLevel, current.EscalationLevel + 1);
        }

        var honouringStatedDelay = false;
        if (signal == IndexerBackoffSignal.RateLimited && retryAfter is { } wanted && wanted > TimeSpan.Zero)
        {
            // The remote named a number. Climb until the rung is at least that long rather than
            // taking one rung at a time and coming back inside the window it just asked for.
            while (level < MaxLevel && Periods[level] < wanted)
            {
                level++;
            }

            honouringStatedDelay = true;
        }

        var period = Periods[level];

        // A container that comes up before its network does fails every indexer it has. Capping the
        // block during the startup window stops one unlucky restart burying the whole install for
        // hours. An explicit Retry-After is exempt: the remote stated that number, and coming back
        // inside it because we happened to have restarted is the behaviour it asked us not to have.
        if (!honouringStatedDelay && _startupWindow.Contains(now) && period > Periods[StartupGraceLevel])
        {
            period = Periods[StartupGraceLevel];
        }

        return new IndexerBackoffState(
            // Set once, at the start of the run, and preserved from then on. "Failing for more than
            // six hours" is a different and louder condition than "failing right now", and it is
            // uncomputable if this field tracks the latest failure instead of the first.
            InitialFailure: current.EscalationLevel == 0 ? utcNow : current.InitialFailure ?? utcNow,
            MostRecentFailure: utcNow,
            EscalationLevel: level,
            DisabledTill: utcNow + period + Jitter(),
            LastFailureReason: reason.ToString());
    }

    private static TimeSpan Jitter() =>
        TimeSpan.FromMilliseconds(Random.Shared.Next(0, (int)MaxJitter.TotalMilliseconds + 1));

    private void LogTransition(Indexer indexer, IndexerBackoffState current, IndexerBackoffState next)
    {
        if (next.EscalationLevel == 0)
        {
            _logger.LogInformation(
                "Indexer {Name} left failure backoff (was rung {PreviousLevel})",
                indexer.Name,
                current.EscalationLevel);
            return;
        }

        if (next.DisabledTill == null)
        {
            _logger.LogInformation(
                "Indexer {Name} walked down to failure backoff rung {Level}",
                indexer.Name,
                next.EscalationLevel);
            return;
        }

        _logger.LogWarning(
            "Indexer {Name} blocked until {DisabledTill:o} at failure backoff rung {Level} ({Reason})",
            indexer.Name,
            next.DisabledTill,
            next.EscalationLevel,
            next.LastFailureReason);
    }
}
