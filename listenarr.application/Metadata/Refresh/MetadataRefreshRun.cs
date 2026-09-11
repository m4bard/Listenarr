/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Listenarr.Application.Metadata.Refresh;

/// <summary>Which entry point asked for the run.</summary>
public enum MetadataRefreshRunScope
{
    Author,
    Library,
    Scheduled
}

/// <summary>Where a run got to.</summary>
public enum MetadataRefreshRunState
{
    Running,
    Completed,
    Failed,
    Cancelled,

    /// <summary>
    /// The run spent its window before it reached the end of its list. Every book it did reach
    /// settled; the rest keep their unset timestamps and sit at the head of the next cycle's
    /// queue. This is a cycle that finished, not one that failed, and the cycle line reports it
    /// like any other.
    /// </summary>
    Truncated
}

/// <summary>A run as the status endpoint reports it.</summary>
public sealed record MetadataRefreshRunSnapshot(
    Guid RunId,
    string Scope,
    string Status,
    int TotalBooks,
    int Processed,
    int Updated,
    int Skipped,
    int Deferred,
    int Failed,
    int RequestsSpent,
    DateTime StartedAt,
    DateTime? CompletedAt);

/// <summary>
/// The live counters for one run. Held in memory by the coordinator and mutated only from the
/// run loop, so a restart loses it; the per-book timestamps are what carry progress forward.
/// </summary>
public sealed class MetadataRefreshRun
{
    private bool _totalAssigned;

    public MetadataRefreshRun(MetadataRefreshRunScope scope, DateTime startedAt)
    {
        RunId = Guid.NewGuid();
        Scope = scope;
        StartedAt = startedAt;
    }

    public Guid RunId { get; }

    public MetadataRefreshRunScope Scope { get; }

    /// <summary>
    /// Zero until the scope has been resolved. The run is published before that query so a
    /// caller refused at the gate always has a run to be told about, which is the only window
    /// in which the count is not yet known.
    /// </summary>
    public int TotalBooks { get; private set; }

    public DateTime StartedAt { get; }

    public DateTime? CompletedAt { get; private set; }

    public MetadataRefreshRunState State { get; private set; } = MetadataRefreshRunState.Running;

    public int Processed { get; private set; }

    public int Updated { get; private set; }

    public int Skipped { get; private set; }

    public int Deferred { get; private set; }

    public int Failed { get; private set; }

    public int RequestsSpent { get; set; }

    /// <summary>
    /// Records one book. A conflict is counted with the deferrals: like a deferral it leaves the
    /// timestamp unset and comes back next cycle, which is not what skipped means here.
    /// </summary>
    /// <remarks>
    /// Called from the run loop without the state lock, while a status request reads the
    /// counters under it. That is deliberate and it is safe: each counter is an int, so a reader
    /// sees a value that was true at some point during the run rather than a torn one, and a
    /// progress number one book behind is what a progress number is. The terminal state is
    /// different and is written under the lock by <see cref="Finish"/>.
    /// </remarks>
    public void Record(MetadataRefreshOutcome outcome)
    {
        Processed++;
        switch (outcome)
        {
            case MetadataRefreshOutcome.Updated:
                Updated++;
                break;
            case MetadataRefreshOutcome.Skipped:
            case MetadataRefreshOutcome.NotFound:
                Skipped++;
                break;
            case MetadataRefreshOutcome.Conflict:
            case MetadataRefreshOutcome.Deferred:
                Deferred++;
                break;
            default:
                Failed++;
                break;
        }
    }

    /// <summary>
    /// Assigns the book count, once, while the run is still running. A setter would let a
    /// later caller rewrite a finished run's total out from under a snapshot someone is
    /// already holding.
    /// </summary>
    public void SetTotal(int totalBooks)
    {
        if (_totalAssigned || State != MetadataRefreshRunState.Running)
        {
            throw new InvalidOperationException(
                "A run's book count is assigned once, before the run finishes.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(totalBooks);
        _totalAssigned = true;
        TotalBooks = totalBooks;
    }

    public void Finish(MetadataRefreshRunState state, DateTime completedAt)
    {
        State = state;
        CompletedAt = completedAt;
    }

    public MetadataRefreshRunSnapshot ToSnapshot() => new(
        RunId,
        Scope.ToString(),
        State.ToString(),
        TotalBooks,
        Processed,
        Updated,
        Skipped,
        Deferred,
        Failed,
        RequestsSpent,
        StartedAt,
        CompletedAt);
}
