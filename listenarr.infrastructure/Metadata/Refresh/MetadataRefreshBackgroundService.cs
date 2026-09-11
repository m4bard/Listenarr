/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Metadata.Refresh;

/// <summary>The cycle body, kept separate so it can be tested without a host.</summary>
public interface IMetadataRefreshProcessor
{
    Task RunCycleAsync(CancellationToken cancellationToken);

    /// <summary>
    /// How long the last cycle took, or null before the first one has finished. The hosted
    /// service subtracts it from the interval, because the cycle runner sleeps the interval
    /// after the cycle rather than between starts.
    /// </summary>
    TimeSpan? LastCycleElapsed { get; }
}

/// <summary>
/// Walks the stalest books on an interval. Built on the monitoring pair: announce the interval,
/// keep the first cycle off the startup path, and let the cycle runner own the loop.
/// </summary>
public class MetadataRefreshBackgroundService(
    ILogger<MetadataRefreshBackgroundService> logger,
    IMetadataRefreshProcessor processor,
    IWorkerCycleRunner cycleRunner,
    MetadataRefreshOptionsHolder options,
    IServiceScopeFactory scopeFactory) : BackgroundService
{
    /// <summary>
    /// What is left of an interval a cycle has already eaten. A cycle that spends its whole
    /// window still yields something before the next one starts.
    /// </summary>
    private static readonly TimeSpan MinimumIdleBetweenCycles = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Before the announcement, not after it: the holder is at the record defaults until
        // something reads settings, and a start line naming 24 hours to an operator who set six
        // is worse than no line at all.
        using (var scope = scopeFactory.CreateScope())
        {
            await MetadataRefreshOptionsLoader.LoadAsync(
                scope.ServiceProvider,
                options,
                logger,
                stoppingToken);
        }

        logger.LogInformation(
            "MetadataRefreshBackgroundService started. Library metadata will be refreshed every {Hours} hours",
            options.Current.IntervalHours);

        await cycleRunner.RunPeriodicAsync(
            nameof(MetadataRefreshBackgroundService),
            initialDelay: TimeSpan.FromMinutes(10),
            intervalProvider: NextDelay,
            runCycle: processor.RunCycleAsync,
            stoppingToken);

        logger.LogInformation("MetadataRefreshBackgroundService stopped");
    }

    /// <summary>
    /// The cycle runner delays this after each cycle, not between starts, so a scheduled run
    /// that spent hours inside its window would otherwise push the next cycle out by the
    /// interval on top of that. Subtracting what the last one cost keeps the period the period.
    /// </summary>
    private TimeSpan NextDelay()
    {
        var interval = TimeSpan.FromHours(Math.Max(1, options.Current.IntervalHours));
        var remaining = interval - (processor.LastCycleElapsed ?? TimeSpan.Zero);
        return remaining > MinimumIdleBetweenCycles ? remaining : MinimumIdleBetweenCycles;
    }
}

public class MetadataRefreshProcessor(
    ILogger<MetadataRefreshProcessor> logger,
    IMetadataRefreshCoordinator coordinator,
    MetadataRefreshOptionsHolder options,
    IServiceScopeFactory scopeFactory) : IMetadataRefreshProcessor
{
    /// <summary>
    /// What the last cycle cost, wall clock, including the time the budget spent waiting. The
    /// hosted service subtracts it from the next delay.
    /// </summary>
    public TimeSpan? LastCycleElapsed { get; private set; }

    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            await RunCycleBodyAsync(cancellationToken);
        }
        finally
        {
            // Recorded even for a cycle that threw or was skipped: a cycle that spent an hour
            // before failing has still eaten that hour of the interval.
            LastCycleElapsed = Stopwatch.GetElapsedTime(startedAt);
        }
    }

    private async Task RunCycleBodyAsync(CancellationToken cancellationToken)
    {
        // Rewritten before every cycle, so an operator changing the budget or the interval does
        // not have to restart. The cycle runner re-reads the interval each pass for the same
        // reason.
        using (var scope = scopeFactory.CreateScope())
        {
            await MetadataRefreshOptionsLoader.LoadAsync(
                scope.ServiceProvider,
                options,
                logger,
                cancellationToken);
        }

        if (!options.Current.Enabled)
        {
            logger.LogDebug("MetadataRefreshBackgroundService cycle skipped; refresh is disabled in settings");
            return;
        }

        var run = await coordinator.RunToCompletionAsync(
            new MetadataRefreshScopeRequest(MetadataRefreshRunScope.Scheduled, null, Force: false),
            cancellationToken);
        if (run == null)
        {
            logger.LogDebug("MetadataRefreshBackgroundService cycle skipped; a refresh run is already in flight");
            return;
        }

        logger.LogInformation(
            "MetadataRefreshBackgroundService completed refresh cycle. Updated {Updated} of {Processed} audiobook(s), {Deferred} deferred, {Requests} provider request(s) spent",
            run.Updated,
            run.Processed,
            run.Deferred,
            run.RequestsSpent);
    }
}
