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
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Persistence
{
    /// <summary>
    /// Runs once at startup to idempotently normalize legacy JSON-backed TEXT columns
    /// so that collection properties are stored as JSON arrays (not primitive roots).
    /// This is safe to run repeatedly and will not modify already-correct rows.
    /// </summary>
    internal sealed class StartupDbNormalizer(
        IServiceProvider provider,
        LibraryFilesystemReadiness filesystemReadiness,
        ILogger<StartupDbNormalizer> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            try
            {
                await filesystemReadiness.WaitUntilSettledAsync(stoppingToken);
                using var scope = provider.CreateScope();
                var audiobookRepository = scope.ServiceProvider.GetRequiredService<IAudiobookRepository>();
                await audiobookRepository.NormalizeJsonColumnsAsync(stoppingToken);

                // Here rather than in the migration that added the column, because migrations in
                // this repository stay direct EF scaffolds and data repair lives on the startup
                // path. It touches only rows with no refresh timestamp, so every start after the
                // first writes nothing, and it runs well before the refresh walk's first cycle,
                // which is ten minutes behind startup. That order matters: the rows it stamps are
                // the ones that would otherwise be due immediately, and stamping them now is what
                // buys an upgraded library its first staleness window.
                var backfilled = await audiobookRepository.BackfillMetadataRefreshTimestampsAsync(stoppingToken);
                if (backfilled > 0)
                {
                    logger.LogInformation(
                        "StartupDbNormalizer: gave {Count} audiobook(s) a starting metadata refresh timestamp",
                        backfilled);
                }

                logger.LogInformation("StartupDbNormalizer: normalization pass complete.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                Debug.WriteLine("StartupDbNormalizer canceled during host shutdown.");
            }
            catch (OperationCanceledException ex)
            {
                logger.LogWarning(ex, "StartupDbNormalizer: operation canceled/timed out; skipping normalization pass");
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogError(ex, "StartupDbNormalizer: unexpected error while running normalization");
            }
        }
    }
}
