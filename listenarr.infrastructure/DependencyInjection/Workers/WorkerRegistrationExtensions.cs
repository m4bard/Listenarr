/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Infrastructure.HostedServices;
using Listenarr.Infrastructure.HostedServices.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Listenarr.Infrastructure.DependencyInjection.Workers;

internal static class WorkerRegistrationExtensions
{
    public static IServiceCollection AddFeatureWorkers(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IScheduledTaskRegistry, ScheduledTaskRegistry>();
        services.AddSingleton<IWorkerCycleRunner, WorkerCycleRunner>();

        // MetadataRefreshOptionsHolder and IMetadataRefreshCoordinator are registered
        // unconditionally in AddMetadataServices instead of here: this method is skipped
        // wholesale when background hosted services are disabled, but the coordinator also
        // gates the foreground API trigger and must remain resolvable either way.

        services.AddSingleton<IScanQueueService, ScanQueueService>();
        services.AddSingleton<MoveScanHandoffRecoveryService>();
        AddProcessor<ScanJobProcessor, IScanJobProcessor>(services);
        services.AddHostedService<ScanBackgroundService>();

        services.AddSingleton<AudiobookContentMoveService>();
        AddProcessor<MoveJobProcessor, IMoveJobProcessor>(services);
        services.AddHostedService<MoveBackgroundService>();

        AddHostedProcessor<ImageCacheCleanupProcessor, IImageCacheCleanupProcessor, ImageCacheCleanupService>(services);
        AddHostedProcessor<DownloadMonitorProcessor, IDownloadMonitorProcessor, DownloadMonitorService>(services);
        AddHostedProcessor<DirectDownloadProcessor, IDirectDownloadProcessor, DirectDownloadService>(services);
        AddHostedProcessor<MovedDownloadCleanupProcessor, IMovedDownloadCleanupProcessor, MovedDownloadCleanupService>(services);

        AddProcessor<QueueMonitorProcessor, IQueueMonitorProcessor>(services);
        services.AddHostedService<QueueMonitorService>();

        AddHostedProcessor<AutomaticSearchProcessor, IAutomaticSearchProcessor, AutomaticSearchService>(services);
        AddHostedProcessor<AuthorMonitoringProcessor, IAuthorMonitoringProcessor, AuthorMonitoringBackgroundService>(services);
        AddHostedProcessor<SeriesMonitoringProcessor, ISeriesMonitoringProcessor, SeriesMonitoringBackgroundService>(services);
        AddHostedProcessor<FfmpegInstallProcessor, IFfmpegInstallProcessor, FfmpegInstallBackgroundService>(services);
        AddHostedProcessor<MetadataRescanProcessor, IMetadataRescanProcessor, MetadataRescanService>(services);
        AddHostedProcessor<MetadataRefreshProcessor, IMetadataRefreshProcessor, MetadataRefreshBackgroundService>(services);
        services.AddSingleton<DownloadProcessingJobProcessor>();
        services.AddSingleton<IDownloadImportProcessor>(provider =>
            provider.GetRequiredService<DownloadProcessingJobProcessor>());
        services.AddHostedService(provider =>
            provider.GetRequiredService<DownloadProcessingJobProcessor>());

        // Retention cleanup gets its own worker so importing files and pruning old
        // terminal processing-job rows remain separate durable responsibilities.
        AddHostedProcessor<
            DownloadProcessingJobCleanupProcessor,
            IDownloadProcessingJobCleanupProcessor,
            DownloadProcessingJobCleanupService>(services);

        AddHostedProcessor<UnmatchedScanProcessor, IUnmatchedScanProcessor, UnmatchedScanBackgroundService>(services);

        // Also its own worker: prunes action-history rows on the configured
        // HistoryRetentionDays setting, unrelated to any other cleanup service's table.
        AddHostedProcessor<
            HistoryRetentionCleanupProcessor,
            IHistoryRetentionCleanupProcessor,
            HistoryRetentionCleanupService>(services);

        AddHousekeeping(services);
        return services;
    }

    /// <summary>
    /// The daily retention sweep, and the list of tables it sweeps.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The housekeepers are listed here by name rather than found by an assembly scan. That is a
    /// deliberate divergence from the family, whose scan
    /// (src/NzbDrone.Common/Composition/Extensions.cs:27-29) leaves the execution order of its
    /// thirty-odd housekeepers undeclared, and it matches how every other worker in this
    /// container is registered. A table joins the sweep by being added to this list, where a
    /// reviewer can see the whole set at once.
    /// </para>
    /// <para>
    /// The sweep is deliberately absent from the manual-run allowlist. It deletes stored rows,
    /// and ScheduledTaskManualTrigger says on Allowed that such a cycle is not one to put there,
    /// so it takes the RunPeriodicAsync default of Denied and there is nothing to add here.
    /// </para>
    /// </remarks>
    private static void AddHousekeeping(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<HousekeepingOptionsHolder>();

        // The list. Order is execution order, cheapest and least consequential first, so a cycle
        // that is going to fail has already done the harmless work.
        services.AddSingleton<IHousekeepingTask, AuthorCacheHousekeeper>();
        services.AddSingleton<IHousekeepingTask, SeriesCacheHousekeeper>();
        services.AddSingleton<IHousekeepingTask, FileMutationJournalHousekeeper>();
        services.AddSingleton<IHousekeepingTask, MoveJobHousekeeper>();

        AddHostedProcessor<HousekeepingProcessor, IHousekeepingProcessor, HousekeepingService>(services);
    }

    private static void AddProcessor<TProcessor, TContract>(IServiceCollection services)
        where TProcessor : class, TContract
        where TContract : class
    {
        services.AddSingleton<TProcessor>();
        services.AddSingleton<TContract>(provider =>
            provider.GetRequiredService<TProcessor>());
    }

    private static void AddHostedProcessor<TProcessor, TContract, THostedService>(
        IServiceCollection services)
        where TProcessor : class, TContract
        where TContract : class
        where THostedService : class, IHostedService
    {
        AddProcessor<TProcessor, TContract>(services);
        services.AddSingleton<THostedService>();
        services.AddHostedService(provider =>
            provider.GetRequiredService<THostedService>());
    }
}
