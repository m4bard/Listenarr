using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Listenarr.Infrastructure.Models;

namespace Listenarr.Api.Services
{
    /// <summary>
    /// Runs once at startup to idempotently normalize legacy JSON-backed TEXT columns
    /// so that collection properties are stored as JSON arrays (not primitive roots).
    /// This is safe to run repeatedly and will not modify already-correct rows.
    /// </summary>
    public class StartupDbNormalizer : IHostedService
    {
        private readonly IServiceProvider _provider;
        private readonly ILogger<StartupDbNormalizer> _logger;

        public StartupDbNormalizer(IServiceProvider provider, ILogger<StartupDbNormalizer> logger)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _provider.CreateScope();
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
                await using var ctx = await factory.CreateDbContextAsync(cancellationToken);

                // IMPORTANT: column names are interpolated directly into raw SQL.
                // This whitelist MUST be kept up-to-date if new columns are added.
                // Never replace this with a caller-supplied or user-supplied list.
                var columns = new[] { "Authors", "Genres", "Tags", "Narrators", "AuthorAsins", "Isbn" };
                var allowedColumns = new HashSet<string>(columns, StringComparer.Ordinal);

                foreach (var col in columns)
                {
                    // Defensive check: reject any column name that was not explicitly whitelisted
                    // or that contains characters invalid for an unquoted SQL identifier.
                    if (!allowedColumns.Contains(col) || !System.Text.RegularExpressions.Regex.IsMatch(col, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                    {
                        _logger.LogError("StartupDbNormalizer: refusing to process unexpected column name '{Column}'", col);
                        continue;
                    }

                    try
                    {
                        var update = $@"UPDATE Audiobooks SET {col} = json_array(json_extract({col}, '$')) WHERE {col} IS NOT NULL AND json_valid({col})=1 AND json_type({col}) NOT IN ('array','object')";
                        var changed = await ctx.Database.ExecuteSqlRawAsync(update, cancellationToken);
                        _logger.LogInformation("StartupDbNormalizer: normalized column {Column}, rows changed: {Changes}", col, changed);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException) {
                        _logger.LogWarning(ex, "StartupDbNormalizer: failed to normalize column {Column}", col);
                    }
                }

                _logger.LogInformation("StartupDbNormalizer: normalization pass complete.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // shutdown requested - ignore
                            System.Diagnostics.Debug.WriteLine("Suppressed non-fatal exception in catch block.");
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "StartupDbNormalizer: operation canceled/timed out; skipping normalization pass");
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException) {
                _logger.LogError(ex, "StartupDbNormalizer: unexpected error while running normalization");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

