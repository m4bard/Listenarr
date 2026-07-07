namespace Listenarr.Application.Common;

public sealed class FilesystemMutationCoordinator : IFilesystemMutationCoordinator, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task ExecuteExclusiveAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await operation(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<T> ExecuteExclusiveAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await operation(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
