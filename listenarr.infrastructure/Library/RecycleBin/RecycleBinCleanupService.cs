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

using Listenarr.Application.Library.RecycleBin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.RecycleBin
{
    /// <summary>
    /// Ages recycled files out of the bin once a day.
    ///
    /// Readarr does this with a scheduled command, which Listenarr has no equivalent of at
    /// this commit. Rather than wait for one, this reuses the periodic worker shape that
    /// ImageCacheCleanupService already uses: IWorkerCycleRunner.RunPeriodicAsync, first
    /// run delayed to midnight, then every 24 hours.
    /// </summary>
    public class RecycleBinCleanupService(
        ILogger<RecycleBinCleanupService> logger,
        IRecycleBinCleanupProcessor processor,
        IWorkerCycleRunner cycleRunner,
        TimeProvider timeProvider) : BackgroundService
    {
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Recycle Bin Cleanup Service is starting");
            await cycleRunner.RunPeriodicAsync(
                nameof(RecycleBinCleanupService),
                initialDelay: GetDelayUntilMidnight(),
                intervalProvider: () => CleanupInterval,
                runCycle: processor.RunCycleAsync,
                stoppingToken);
        }

        private TimeSpan GetDelayUntilMidnight()
        {
            var now = timeProvider.GetLocalNow().DateTime;
            var delay = now.Date.AddDays(1) - now;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Recycle Bin Cleanup Service is stopping");
            await base.StopAsync(cancellationToken);
        }
    }

    public class RecycleBinCleanupProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<RecycleBinCleanupProcessor> logger) : IRecycleBinCleanupProcessor
    {
        public async Task RunCycleAsync(CancellationToken cancellationToken)
        {
            using var scope = scopeFactory.CreateScope();
            var recycleBinService = scope.ServiceProvider
                .GetRequiredService<IRecycleBinService>();
            var result = await recycleBinService.CleanupAsync(cancellationToken);
            logger.LogDebug(
                "Daily recycle bin cleanup removed {FileCount} files",
                result.FilesRemoved);
        }
    }
}
