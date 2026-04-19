using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Listenarr.Api.Services;
using Listenarr.Application.Repositories;

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
                var audiobookRepo = scope.ServiceProvider.GetRequiredService<IAudiobookRepository>();
                await audiobookRepo.NormalizeJsonColumnsAsync(cancellationToken);
                _logger.LogInformation("StartupDbNormalizer: normalization pass complete.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
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
