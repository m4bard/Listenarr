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

/// <summary>
/// Reads the operator's settings into the live options holder.
/// </summary>
/// <remarks>
/// Shared, because the scheduled walk is not the only thing the budget has to govern. The
/// processor calls this at the top of every cycle; the coordinator calls it as a run is
/// admitted, inside the scope it already opens to resolve the run's scope. Without the second
/// call an operator who sets five requests an hour and then triggers a library refresh gets the
/// shipped sixty, because the first cycle is ten minutes after start and never runs at all when
/// hosted services are off.
/// </remarks>
public static class MetadataRefreshOptionsLoader
{
    // Lower bounds keep a zero or a negative from meaning "as fast as possible". Upper bounds
    // are here because nothing else validates these: a spacing of a day or an interval of a year
    // does not slow the walk down, it stops it, and it does so silently.
    //
    // Known constraint, left as is: these clamp at use, not at save, so GET /settings echoes
    // back whatever was stored and an operator who typed 100000 requests an hour is told 100000
    // while the walk runs at 3600. Clamping on the way in is the right fix and it does not
    // belong here: the bounds would have to move to the application layer beside
    // ApplicationSettings, because ConfigurationService cannot reference infrastructure. That is
    // a refactor of shared settings code rather than a change to this feature, so it is named
    // here rather than done quietly.
    private const int MinIntervalHours = 1;
    private const int MaxIntervalHours = 168;
    private const int MinStaleAfterDays = 0;
    private const int MaxStaleAfterDays = 3650;
    private const int MinRequestsPerHour = 1;
    private const int MaxRequestsPerHour = 3600;
    private const int MinMinimumSpacingMs = 0;
    private const int MaxMinimumSpacingMs = 60000;

    /// <summary>
    /// Rewrites <paramref name="holder"/> from application settings, clamped. A scope with no
    /// <see cref="IConfigurationService"/> in it, and a settings read that fails, both leave the
    /// values already in use alone: the run is better off with yesterday's budget than with
    /// none.
    /// </summary>
    public static async Task LoadAsync(
        IServiceProvider scopeProvider,
        MetadataRefreshOptionsHolder holder,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeProvider);
        ArgumentNullException.ThrowIfNull(holder);

        try
        {
            var configuration = scopeProvider.GetService<IConfigurationService>();
            if (configuration == null)
            {
                logger.LogDebug(
                    "No configuration service in this scope; keeping the metadata refresh options already in use");
                return;
            }

            var settings = await configuration.GetApplicationSettingsAsync();
            cancellationToken.ThrowIfCancellationRequested();
            holder.Current = new MetadataRefreshOptions(
                settings.MetadataRefreshEnabled,
                Math.Clamp(settings.MetadataRefreshIntervalHours, MinIntervalHours, MaxIntervalHours),
                Math.Clamp(settings.MetadataRefreshStaleAfterDays, MinStaleAfterDays, MaxStaleAfterDays),
                Math.Clamp(settings.MetadataRefreshRequestsPerHour, MinRequestsPerHour, MaxRequestsPerHour),
                Math.Clamp(settings.MetadataRefreshMinimumSpacingMs, MinMinimumSpacingMs, MaxMinimumSpacingMs));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (WorkerExceptionClassifier.IsNonFatal(ex))
        {
            logger.LogWarning(ex, "Failed to load metadata refresh settings; keeping the values already in use");
        }
    }
}
