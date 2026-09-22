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

using Listenarr.Domain.Common;
using Listenarr.Infrastructure.Downloads.Blocklist;
using Listenarr.Infrastructure.Persistence;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Infrastructure.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;

namespace Listenarr.Infrastructure.DependencyInjection;

public static class InfrastructureStartupCompositionExtensions
{
    public static IServiceCollection AddListenarrInfrastructureComposition(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddListenarrHttpClients(configuration);

        var sqliteDbPath = ResolveSqliteDbPath(configuration, environment);
        Log.Logger.Information("[Startup] Resolved SQLite DB path: {SqliteDbPath}", sqliteDbPath);

        services.AddListenarrAdapters(configuration);
        services.AddListenarrInfrastructure(options =>
            options.UseSqlite($"Data Source={sqliteDbPath}", sqliteOptions =>
                sqliteOptions.MigrationsAssembly(typeof(QualityProfileRepository).Assembly.GetName().Name)),
            environment.ContentRootPath);
        services.AddListenarrAppServices(configuration);
        services.AddListenarrHostedWorkers(configuration);
        services.AddListenarrExternalRequests(configuration);
        services.AddScoped<IDownloadHistoryService, DownloadHistoryService>();
        services.AddScoped<IBlocklistService, BlocklistService>();

        return services;
    }

    public static void ApplyListenarrDatabaseMigrations(this IServiceProvider serviceProvider)
    {
        try
        {
            Log.Logger.Information("[Startup] Applying EF Core migrations at startup");
            using var migrateScope = serviceProvider.CreateScope();
            var factory = migrateScope.ServiceProvider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
            using var ctx = factory.CreateDbContext();
            // First, before anything on this path writes. RepairLegacyData below is not a read:
            // it clears the IsDefault flag on every root folder but the lowest-numbered one, on
            // exactly the old populated databases this backup exists for. Taken after it, the
            // archive would hold the repaired state and could not restore what the repair chose.
            //
            // A failure here is deliberately not caught: it falls into the handler at the bottom
            // and refuses the start, because migrating without the copy is the outcome this
            // prevents. Readarr does the same, in that a throw out of Backup(BackupType.Update) is
            // not among the exceptions InstallUpdateService.Execute handles, so the update it was
            // protecting does not proceed.
            PreMigrationBackup
                .ProtectAsync(
                    ctx.Database.GetPendingMigrations().ToList(),
                    ctx,
                    PreMigrationBackup.IsEnabled(migrateScope.ServiceProvider.GetService<IConfiguration>()),
                    new Lazy<IBackupService>(migrateScope.ServiceProvider.GetRequiredService<IBackupService>))
                .GetAwaiter()
                .GetResult();

            var repairedLegacyData =
                ListenarrDatabaseMigrationPreflight.RepairLegacyData(ctx);
            if (repairedLegacyData.DefaultRootsNormalized > 0)
            {
                Log.Logger.Warning(
                    "[Startup] Normalized {Count} duplicate default root folder row(s) before applying the single-default constraint",
                    repairedLegacyData.DefaultRootsNormalized);
            }

            ctx.Database.Migrate();
            var repairedPostMigrationData =
                ListenarrDatabaseMigrationPreflight.RepairPostMigrationData(ctx);
            if (repairedPostMigrationData.MoveJobsRepaired > 0)
            {
                Log.Logger.Warning(
                    "[Startup] Normalized {Count} legacy move job row(s) after applying durable move migrations",
                    repairedPostMigrationData.MoveJobsRepaired);
            }
            if (repairedPostMigrationData.MoveJobTerminalTimestampsBackfilled > 0)
            {
                Log.Logger.Information(
                    "[Startup] Stamped a terminal timestamp onto {Count} finished move job row(s) that predate the column, so housekeeping retention can see them",
                    repairedPostMigrationData.MoveJobTerminalTimestampsBackfilled);
            }

            if (repairedPostMigrationData.QualityProfileUpgradeFlagsRepaired > 0)
            {
                Log.Logger.Information(
                    "[Startup] Turned quality upgrades off on {Count} profile(s) that recorded it with a blank cutoff",
                    repairedPostMigrationData.QualityProfileUpgradeFlagsRepaired);
            }

            var repairedAuthorAsins =
                ListenarrDatabaseMigrationPreflight.RepairAmbiguousAuthorAsins(ctx);
            if (repairedAuthorAsins.MonitoredAuthorsRepaired > 0
                || repairedAuthorAsins.CachedAuthorsRepaired > 0)
            {
                Log.Logger.Warning(
                    "[Startup] Cleared ambiguous author ASINs from {MonitoredCount} monitored author row(s) and {CachedCount} cached author row(s); each name will be resolved again on its next sync",
                    repairedAuthorAsins.MonitoredAuthorsRepaired,
                    repairedAuthorAsins.CachedAuthorsRepaired);
            }

            Log.Logger.Information("[Startup] EF Core migrations applied successfully");

            // Swept only now the schema is current, because retention reads application settings
            // and that read is not safe until the columns this build expects exist. Housekeeping,
            // so a failure is logged rather than allowed to stop a start that is otherwise fine.
            SweepExpiredBackups(migrateScope.ServiceProvider);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            Log.Logger.Error(ex, "[Startup] Failed to apply EF Core migrations at startup. Listenarr cannot start safely with an unknown database schema.");
            throw;
        }
    }

    private static void SweepExpiredBackups(IServiceProvider scopedServiceProvider)
    {
        try
        {
            // Optional, unlike the pre-migration backup. A provider that has no backup service has
            // no backups to sweep, and housekeeping is not worth failing a start over.
            scopedServiceProvider
                .GetService<IBackupService>()
                ?.ApplyRetentionAsync()
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            Log.Logger.Warning(ex, "[Startup] Backup retention sweep failed; existing backups are untouched");
        }
    }

    private static IServiceCollection AddListenarrHostedWorkers(this IServiceCollection services, IConfiguration configuration)
    {
        var disableHostedServices =
            configuration.GetValue<bool>("Listenarr:DisableHostedServices") ||
            string.Equals(Environment.GetEnvironmentVariable("LISTENARR_DISABLE_HOSTED_SERVICES"), "true", StringComparison.OrdinalIgnoreCase);

        if (disableHostedServices)
        {
            Log.Logger.Warning("[Startup] Hosted/background services are disabled by configuration override");
        }
        else
        {
            Log.Logger.Information("[Startup] Hosted/background services are enabled");
        }

        services.AddSingleton<IUnmatchedScanQueueService, UnmatchedScanQueueService>();
        services.AddHostedService<LibraryFilesystemStartupReconciliationService>();

        if (!disableHostedServices)
        {
            services.AddListenarrHostedServices(configuration);
        }

        services.AddHostedService<StartupDbNormalizer>();
        return services;
    }

    private static IServiceCollection AddListenarrExternalRequests(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ExternalRequestOptions>()
            .Bind(configuration.GetSection("ExternalRequests"))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ExternalRequestOptions>, ExternalRequestOptionsValidator>();

        return services;
    }

    private static string ResolveSqliteDbPath(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var sqliteDbPathOverride = configuration["Listenarr:SqliteDbPath"];
        var sqliteDbPath = string.IsNullOrWhiteSpace(sqliteDbPathOverride)
            ? Path.Join(environment.ContentRootPath, "config", "database", "listenarr.db")
            : (Path.IsPathRooted(sqliteDbPathOverride)
                ? sqliteDbPathOverride
                : Path.Join(environment.ContentRootPath, sqliteDbPathOverride));

        if (environment.IsEnvironment("Test"))
        {
            var repoDbPath = Path.GetFullPath(Path.Join(environment.ContentRootPath, "config", "database", "listenarr.db"));
            var resolvedSqlitePath = Path.GetFullPath(sqliteDbPath);
            if (FileUtils.AreFilesystemPathsEquivalentForCurrentOs(resolvedSqlitePath, repoDbPath))
            {
                sqliteDbPath = Path.Join(Path.GetTempPath(), "listenarr-tests", "program-main", $"listenarr-{Guid.NewGuid():N}.db");
                Log.Logger.Warning("[Startup] Test environment attempted to use repo sqlite path; forcing isolated test DB path: {SqliteDbPath}", sqliteDbPath);
            }
        }

        var sqliteDbDir = Path.GetDirectoryName(sqliteDbPath);
        if (!string.IsNullOrEmpty(sqliteDbDir) && !Directory.Exists(sqliteDbDir))
        {
            Directory.CreateDirectory(sqliteDbDir);
        }

        return sqliteDbPath;
    }
}
