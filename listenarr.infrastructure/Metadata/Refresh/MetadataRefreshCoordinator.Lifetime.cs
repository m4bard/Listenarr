/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Metadata.Refresh;

/// <summary>
/// Shutdown. The run holds a scope factory it resolves a scope from per book, so a run still
/// walking while the root provider is torn down is a burst of ObjectDisposedException, one per
/// book, until the process goes. Stopping it is therefore part of disposing this.
/// </summary>
public sealed partial class MetadataRefreshCoordinator : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// How long a shutdown waits for the book in flight. One book is a provider call and a
    /// write, so ten seconds is generous; past that the run is abandoned rather than holding
    /// the host open, and the books it did not reach are unstamped and come back next start.
    /// </summary>
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(10);

    private bool _disposed;

    /// <summary>Test hook: waits for a background run started by StartAsync to settle.</summary>
    public Task WaitForIdleAsync(TimeSpan timeout)
    {
        Task inFlight;
        lock (_stateGate)
        {
            inFlight = _inFlight;
        }

        return inFlight.WaitAsync(timeout);
    }

    public async ValueTask DisposeAsync()
    {
        Task inFlight;
        CancellationTokenSource? cancellation;
        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            inFlight = _inFlight;
            cancellation = _cancellation;
        }

        Stop(cancellation);

        var drained = true;
        try
        {
            await inFlight.WaitAsync(ShutdownDrainTimeout);
        }
        catch (TimeoutException)
        {
            drained = false;
            _logger.LogWarning(
                "Metadata refresh run did not stop within {Seconds}s of shutdown; abandoning it",
                ShutdownDrainTimeout.TotalSeconds);
        }
        catch (Exception ex) when (WorkerExceptionClassifier.IsNonFatal(ex))
        {
            // How the run ended is the run loop's business and it has already logged it. All
            // this wait is for is knowing that nothing is still inside a book.
        }

        if (drained)
        {
            // Only once nothing is watching the token. Disposing a source out from under a run
            // turns its next Task.Delay into an ObjectDisposedException, which is the shape
            // this whole method exists to avoid.
            cancellation?.Dispose();
        }
    }

    /// <summary>
    /// The synchronous half, for the registrations that already hold this as IDisposable. It
    /// asks the run to stop and returns; nothing is awaited and nothing is disposed, because a
    /// run that is still inside a book needs its token to survive long enough to see the
    /// cancellation. A host that disposes asynchronously gets the waiting version above.
    /// </summary>
    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        lock (_stateGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            cancellation = _cancellation;
        }

        Stop(cancellation);
    }

    private static void Stop(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already gone, which means the run it belonged to is already over.
        }
    }
}
