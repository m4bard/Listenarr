/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Application.Metadata.Refresh;

namespace Listenarr.Application.Metadata.Contracts;

/// <summary>What a caller is asking to refresh.</summary>
/// <param name="Scope">Which entry point asked, which also decides whether a run window applies.</param>
/// <param name="MonitoredAuthorId">
/// A row in MonitoredAuthors. Null is the whole library, which is also how books by authors
/// nobody monitors are reached.
/// </param>
/// <param name="Force">Ignore the staleness age and take every book in scope.</param>
public sealed record MetadataRefreshScopeRequest(
    MetadataRefreshRunScope Scope,
    int? MonitoredAuthorId,
    bool Force);

/// <summary>
/// Started is false when a run was already in flight; Run is then the run that holds the gate,
/// which the API turns into a 409 carrying its id.
/// </summary>
public sealed record MetadataRefreshStartResult(bool Started, MetadataRefreshRunSnapshot Run);

/// <summary>Operator-set knobs, read from application settings once per run.</summary>
public sealed record MetadataRefreshOptions(
    bool Enabled = true,
    int IntervalHours = 24,
    int StaleAfterDays = 30,
    int RequestsPerHour = 60,
    int MinimumSpacingMs = 1000);

/// <summary>
/// The live options, rewritten from application settings at the top of every cycle. A holder
/// rather than the record itself, so a settings change takes effect on the next cycle without
/// a restart, the way WorkerCycleRunner already re-reads its interval every pass.
/// </summary>
public sealed class MetadataRefreshOptionsHolder
{
    public MetadataRefreshOptions Current { get; set; } = new();
}

/// <summary>
/// The single gate every metadata refresh goes through, so the request budget has one consumer.
/// </summary>
public interface IMetadataRefreshCoordinator
{
    /// <summary>
    /// Starts a run in the background and returns immediately. The token bounds admission
    /// only, so a caller may pass the one it already has: it can cancel the queue lookup, but
    /// the run that comes out of it outlives the caller and is stopped through
    /// <see cref="Cancel"/>. A web request's token would otherwise end the run at the moment
    /// the response is written.
    /// <para>
    /// It does not outlive the process, though. The run's own source is linked to the host's
    /// ApplicationStopping token, so a shutdown stops it at the next book boundary instead of
    /// leaving it asking a provider it can no longer resolve for the rest of the library.
    /// </para>
    /// </summary>
    Task<MetadataRefreshStartResult> StartAsync(
        MetadataRefreshScopeRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs to completion in the caller's context, for the scheduled walk. Null means the gate
    /// was held by another run and this cycle did nothing.
    /// </summary>
    Task<MetadataRefreshRunSnapshot?> RunToCompletionAsync(
        MetadataRefreshScopeRequest request,
        CancellationToken cancellationToken);

    MetadataRefreshRunSnapshot? Find(Guid runId);

    /// <summary>The active run, or the most recent one this process ran.</summary>
    MetadataRefreshRunSnapshot? Current();

    bool Cancel(Guid runId);

    /// <summary>
    /// A view of the shared provider budget for a caller that is not a refresh run, bounded by
    /// how long it may keep waiting for a slot.
    /// </summary>
    /// <remarks>
    /// The bucket is per process and already shared across every refresh entry point, and the
    /// reason for that is not refresh-specific: it is one provider, and two schedulers pointed at
    /// it are two schedulers' worth of requests however carefully each one paces itself. Anything
    /// else that walks the library asking the provider questions spends out of this, rather than
    /// bringing its own limiter and quietly doubling the rate.
    ///
    /// It leases budget and nothing else. The one-run gate is deliberately not taken here: a
    /// caller holding it would make every scheduled refresh cycle in its window do nothing, and
    /// these passes are not mutually exclusive, only jointly rate limited. Sharing the bucket is
    /// what keeps them honest with each other.
    ///
    /// The settings the bucket is configured from are the operator's current ones, so a caller
    /// that has just loaded them gets the rate they asked for rather than the shipped default.
    /// </remarks>
    IMetadataRefreshBudget LeaseBudget(TimeSpan window);
}
