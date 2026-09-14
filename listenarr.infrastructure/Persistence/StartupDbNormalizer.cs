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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Persistence
{
    /// <summary>
    /// Runs once at startup to idempotently normalize legacy JSON-backed TEXT columns
    /// so that collection properties are stored as JSON arrays (not primitive roots), and to
    /// re-derive the normalized author-name keys that rows written by an earlier normalizer
    /// still carry.
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

                // Must precede any join on AuthorNameNormalized: rows written before the author
                // normalizer was unified hold keys the current reader never produces, so a join
                // on that column would silently skip exactly the drifted rows it is looking for.
                var rederived = await audiobookRepository.RederiveAuthorNameKeysAsync(stoppingToken);
                if (rederived.AuthorCacheEntriesCorrected > 0
                    || rederived.MonitoredAuthorsCorrected > 0
                    || rederived.Skipped > 0)
                {
                    logger.LogInformation(
                        "StartupDbNormalizer: re-derived author name keys "
                        + "({CacheEntries} cached authors, {MonitoredAuthors} monitored authors, "
                        + "{Skipped} left alone to avoid a duplicate key).",
                        rederived.AuthorCacheEntriesCorrected,
                        rederived.MonitoredAuthorsCorrected,
                        rederived.Skipped);
                }

                // Runs after the re-derivation above and not before it: this join is on
                // AuthorNameNormalized, so a stale key silently excludes exactly the drifted rows
                // the pass exists to correct.
                var canonicalized = await audiobookRepository.CanonicalizeStoredAuthorNamesAsync(
                    stoppingToken);
                if (canonicalized > 0)
                {
                    logger.LogInformation(
                        "StartupDbNormalizer: adopted the cached author spelling on {Count} books.",
                        canonicalized);
                }

                logger.LogInformation("StartupDbNormalizer: normalization pass complete.");
            }
            catch (OperationCanceledException exception) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogDebug(exception, "StartupDbNormalizer canceled during host shutdown");
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
