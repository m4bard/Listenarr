using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Listenarr.Application.Repositories;
using Listenarr.Domain.Models;

namespace Listenarr.Api.Services
{
    public interface IProcessExecutionStore
    {
        Task SaveAsync(ProcessResult result, string? source = null, ProcessStartInfo? startInfo = null, CancellationToken cancellationToken = default);
    }

    public class ProcessExecutionStore : IProcessExecutionStore
    {
        private readonly IProcessExecutionLogRepository _logs;

        public ProcessExecutionStore(IProcessExecutionLogRepository logs)
        {
            _logs = logs;
        }

        public async Task SaveAsync(ProcessResult result, string? source = null, ProcessStartInfo? startInfo = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var entity = new ProcessExecutionLog
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Source = source,
                    FileName = startInfo?.FileName,
                    Arguments = startInfo?.Arguments,
                    ExitCode = result.ExitCode,
                    TimedOut = result.TimedOut,
                    Stdout = result.Stdout,
                    Stderr = result.Stderr,
                    DurationMs = null
                };

                await _logs.AddAsync(entity, cancellationToken);
            }
            catch (Exception caughtEx_1) when (caughtEx_1 is not OperationCanceledException && caughtEx_1 is not OutOfMemoryException && caughtEx_1 is not StackOverflowException) {
                // Swallow errors here - persistence is best-effort to avoid disrupting process flows.
                            System.Diagnostics.Debug.WriteLine("Suppressed non-fatal exception in catch block.");
            }
        }
    }
}
