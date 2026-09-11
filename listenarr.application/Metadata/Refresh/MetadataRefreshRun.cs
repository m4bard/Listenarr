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
    Cancelled
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
    public MetadataRefreshRun(MetadataRefreshRunScope scope, int totalBooks, DateTime startedAt)
    {
        RunId = Guid.NewGuid();
        Scope = scope;
        TotalBooks = totalBooks;
        StartedAt = startedAt;
    }

    public Guid RunId { get; }

    public MetadataRefreshRunScope Scope { get; }

    public int TotalBooks { get; }

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
