/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Metadata.Refresh;

/// <summary>The cycle body, kept separate so it can be tested without a host.</summary>
public interface IMetadataRefreshProcessor
{
    Task RunCycleAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Walks the stalest books on an interval. Built on the monitoring pair: announce the interval,
/// keep the first cycle off the startup path, and let the cycle runner own the loop.
/// </summary>
public class MetadataRefreshBackgroundService(
    ILogger<MetadataRefreshBackgroundService> logger,
    IMetadataRefreshProcessor processor,
    IWorkerCycleRunner cycleRunner,
    MetadataRefreshOptionsHolder options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "MetadataRefreshBackgroundService started. Library metadata will be refreshed every {Hours} hours",
            options.Current.IntervalHours);

        await cycleRunner.RunPeriodicAsync(
            nameof(MetadataRefreshBackgroundService),
            initialDelay: TimeSpan.FromMinutes(10),
            intervalProvider: () => TimeSpan.FromHours(Math.Max(1, options.Current.IntervalHours)),
            runCycle: processor.RunCycleAsync,
            stoppingToken);

        logger.LogInformation("MetadataRefreshBackgroundService stopped");
    }
}

public class MetadataRefreshProcessor(
    ILogger<MetadataRefreshProcessor> logger,
    IMetadataRefreshCoordinator coordinator,
    MetadataRefreshOptionsHolder options,
    IServiceScopeFactory scopeFactory) : IMetadataRefreshProcessor
{
    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        await RefreshOptionsAsync(cancellationToken);

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

    /// <summary>
    /// Rewrites the holder before every cycle, so an operator changing the budget or the interval
    /// does not have to restart. The cycle runner re-reads the interval each pass for the same reason.
    /// </summary>
    private async Task RefreshOptionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            var settings = await configuration.GetApplicationSettingsAsync();
            options.Current = new MetadataRefreshOptions(
                settings.MetadataRefreshEnabled,
                Math.Max(1, settings.MetadataRefreshIntervalHours),
                Math.Max(0, settings.MetadataRefreshStaleAfterDays),
                Math.Max(1, settings.MetadataRefreshRequestsPerHour),
                Math.Max(0, settings.MetadataRefreshMinimumSpacingMs));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
            when (ex is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
        {
            logger.LogWarning(ex, "Failed to load metadata refresh settings; keeping the values already in use");
        }
    }
}
