using Listenarr.Application.Common.Scheduling;
using Listenarr.Infrastructure.HostedServices;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Scheduling;

/// <summary>
/// A worker driven by the real <see cref="WorkerCycleRunner"/>, so a test exercises the
/// same path the shipped background services take rather than the registry alone.
/// </summary>
/// <remarks>
/// Holding a cycle on a semaphore is what makes the timing assertions deterministic. The
/// cycle is counted before the wait, and the whole prefix of <c>RunPeriodicAsync</c> up
/// to that wait runs synchronously inside this constructor, so once
/// <see cref="WaitForCycleAsync"/> has returned the handle is provably mid-cycle and
/// nothing has to be polled for.
/// </remarks>
internal sealed class PeriodicWorkerHarness : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _loop;
    private readonly List<TaskCompletionSource> _cycleWaiters = [];
    private readonly object _cycleLock = new();
    private readonly Func<Exception>? _failure;
    private readonly SemaphoreSlim? _holdCycleOn;
    private readonly int _holdFromCycle;
    private int _cycleCount;

    public PeriodicWorkerHarness(
        IScheduledTaskRegistry registry,
        string workerName,
        Func<Exception>? failure = null,
        SemaphoreSlim? holdCycleOn = null,
        int holdFromCycle = 1,
        TimeSpan? initialDelay = null,
        TimeSpan? interval = null,
        ScheduledTaskManualTrigger? manualTrigger = null,
        TimeProvider? timeProvider = null)
    {
        _failure = failure;
        _holdCycleOn = holdCycleOn;
        _holdFromCycle = holdFromCycle;
        Interval = interval ?? DefaultInterval;

        var runner = new WorkerCycleRunner(
            timeProvider ?? TimeProvider.System,
            Mock.Of<IAppMetricsService>(),
            registry,
            Mock.Of<ILogger<WorkerCycleRunner>>());

        // Null means the argument is genuinely omitted, so a worker that says nothing
        // about manual runs takes the runner's own default rather than one this
        // helper invented. Flipping that default has to break the tests that use it.
        _loop = manualTrigger is { } declared
            ? runner.RunPeriodicAsync(
                workerName,
                initialDelay,
                intervalProvider: () => Interval,
                runCycle: RunCycleAsync,
                _cancellation.Token,
                declared)
            : runner.RunPeriodicAsync(
                workerName,
                initialDelay,
                intervalProvider: () => Interval,
                runCycle: RunCycleAsync,
                _cancellation.Token);
    }

    public TimeSpan Interval { get; }

    /// <summary>How many times the cycle body has actually been entered.</summary>
    public int CycleCount
    {
        get
        {
            lock (_cycleLock)
            {
                return _cycleCount;
            }
        }
    }

    public Task WaitForCycleAsync(int cycleNumber)
    {
        lock (_cycleLock)
        {
            if (_cycleCount >= cycleNumber)
            {
                return Task.CompletedTask;
            }

            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _cycleWaiters.Add(waiter);
            return waiter.Task.WaitAsync(Patience);
        }
    }

    public async Task StopAsync()
    {
        await _cancellation.CancelAsync();
        await _loop.WaitAsync(Patience);
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var cycle = CountCycle();

        if (_holdCycleOn is { } hold && cycle >= _holdFromCycle)
        {
            await hold.WaitAsync(cancellationToken);
        }
        else
        {
            await Task.Yield();
        }

        if (_failure?.Invoke() is { } failure)
        {
            throw failure;
        }
    }

    private int CountCycle()
    {
        TaskCompletionSource[] waiters;
        int cycle;
        lock (_cycleLock)
        {
            cycle = ++_cycleCount;
            waiters = [.. _cycleWaiters];
            _cycleWaiters.Clear();
        }

        foreach (var waiter in waiters)
        {
            waiter.TrySetResult();
        }

        return cycle;
    }
}

/// <summary>
/// A clock that never moves, for the timestamps a test needs to predict exactly. Timers
/// are left to the base implementation, so <c>Task.Delay</c> against it still waits in
/// real time and still cancels; only <see cref="GetUtcNow"/> is pinned.
/// </summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
