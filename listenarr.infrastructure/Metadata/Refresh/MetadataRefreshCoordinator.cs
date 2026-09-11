/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Application.Common.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Metadata.Refresh;

/// <summary>
/// Single-run gate, in-memory run registry, and the per-book scope loop. A singleton, so every
/// scoped dependency is resolved per book from a scope this class creates.
/// </summary>
public sealed class MetadataRefreshCoordinator : IMetadataRefreshCoordinator, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MetadataRefreshCoordinator> _logger;
    private readonly MetadataRefreshOptionsHolder _optionsHolder;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _stateGate = new();

    private MetadataRefreshRun? _active;
    private MetadataRefreshRun? _last;
    private CancellationTokenSource? _cancellation;
    private Task _inFlight = Task.CompletedTask;
    private List<MetadataRefreshCandidate> _pending = [];

    // Test seam only: production leaves it null and the budget uses Task.Delay. A fake clock
    // that never advances would otherwise make an integration test wait in real time.
    private readonly Func<TimeSpan, CancellationToken, Task>? _delayAsync;

    public MetadataRefreshCoordinator(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        ILogger<MetadataRefreshCoordinator> logger,
        MetadataRefreshOptionsHolder optionsHolder,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = logger;
        _optionsHolder = optionsHolder ?? throw new ArgumentNullException(nameof(optionsHolder));
        _delayAsync = delayAsync;
    }

    private MetadataRefreshOptions Options => _optionsHolder.Current;

    public async Task<MetadataRefreshStartResult> StartAsync(
        MetadataRefreshScopeRequest request,
        CancellationToken cancellationToken)
    {
        var admission = await AdmitAsync(request, cancellationToken);
        if (!admission.Started)
        {
            return admission;
        }

        var run = _active!;
        var token = _cancellation!.Token;
        _inFlight = Task.Run(() => ExecuteAsync(run, request, token), CancellationToken.None);
        return admission;
    }

    public async Task<MetadataRefreshRunSnapshot?> RunToCompletionAsync(
        MetadataRefreshScopeRequest request,
        CancellationToken cancellationToken)
    {
        var admission = await AdmitAsync(request, cancellationToken);
        if (!admission.Started)
        {
            _logger.LogDebug(
                "Metadata refresh cycle skipped; run {RunId} is already in flight",
                admission.Run.RunId);
            return null;
        }

        var run = _active!;
        await ExecuteAsync(run, request, _cancellation!.Token);
        return run.ToSnapshot();
    }

    public MetadataRefreshRunSnapshot? Find(Guid runId)
    {
        lock (_stateGate)
        {
            if (_active?.RunId == runId)
            {
                return _active.ToSnapshot();
            }

            return _last?.RunId == runId ? _last.ToSnapshot() : null;
        }
    }

    public MetadataRefreshRunSnapshot? Current()
    {
        lock (_stateGate)
        {
            return (_active ?? _last)?.ToSnapshot();
        }
    }

    public bool Cancel(Guid runId)
    {
        lock (_stateGate)
        {
            if (_active?.RunId != runId || _cancellation == null)
            {
                return false;
            }

            _cancellation.Cancel();
            return true;
        }
    }

    /// <summary>Test hook: waits for a background run started by StartAsync to settle.</summary>
    public Task WaitForIdleAsync(TimeSpan timeout) => _inFlight.WaitAsync(timeout);

    public void Dispose()
    {
        _cancellation?.Dispose();
        _gate.Dispose();
    }

    private async Task<MetadataRefreshStartResult> AdmitAsync(
        MetadataRefreshScopeRequest request,
        CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            lock (_stateGate)
            {
                return new MetadataRefreshStartResult(false, (_active ?? _last)!.ToSnapshot());
            }
        }

        try
        {
            var candidates = await ResolveAsync(request, cancellationToken);
            var run = new MetadataRefreshRun(
                request.Scope,
                candidates.Count,
                _timeProvider.GetUtcNow().UtcDateTime);
            lock (_stateGate)
            {
                _active = run;
                _cancellation?.Dispose();
                _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _pending = candidates;
            }

            return new MetadataRefreshStartResult(true, run.ToSnapshot());
        }
        catch
        {
            _gate.Release();
            throw;
        }
    }

    private async Task<List<MetadataRefreshCandidate>> ResolveAsync(
        MetadataRefreshScopeRequest request,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAudiobookRepository>();
        var staleBefore = request.Force
            ? DateTime.MaxValue
            : _timeProvider.GetUtcNow().UtcDateTime.AddDays(-Options.StaleAfterDays);

        if (request.Scope != MetadataRefreshRunScope.Author)
        {
            var due = await repository.GetAudiobooksDueForMetadataRefreshAsync(
                staleBefore,
                int.MaxValue,
                cancellationToken);
            return GroupByAuthor(due);
        }

        var authors = scope.ServiceProvider.GetRequiredService<IMonitoredAuthorRepository>();
        var author = request.MonitoredAuthorId.HasValue
            ? await authors.GetByIdAsync(request.MonitoredAuthorId.Value, cancellationToken)
            : null;
        if (author == null)
        {
            return [];
        }

        var ids = await repository.GetAudiobookIdsByAuthorNameAsync(author.AuthorName, cancellationToken);
        if (!request.Force)
        {
            var due = await repository.GetAudiobooksDueForMetadataRefreshAsync(
                staleBefore,
                int.MaxValue,
                cancellationToken);
            var dueIds = due.Select(candidate => candidate.AudiobookId).ToHashSet();
            ids = ids.Where(dueIds.Contains).ToList();
        }

        return ids
            .Select(id => new MetadataRefreshCandidate(id, author.AuthorName, null))
            .ToList();
    }

    /// <summary>
    /// An author's due books are taken together so their membership repairs land in one pass.
    /// First appearance decides an author's place, so the oldest-first ordering is preserved.
    /// </summary>
    private static List<MetadataRefreshCandidate> GroupByAuthor(
        IReadOnlyList<MetadataRefreshCandidate> candidates) =>
        candidates
            .GroupBy(candidate => candidate.PrimaryAuthor ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group)
            .ToList();

    private async Task ExecuteAsync(
        MetadataRefreshRun run,
        MetadataRefreshScopeRequest request,
        CancellationToken cancellationToken)
    {
        var budget = new MetadataRefreshBudget(
            _timeProvider,
            new MetadataRefreshBudgetOptions(
                Options.RequestsPerHour,
                Options.MinimumSpacingMs,
                request.Scope == MetadataRefreshRunScope.Scheduled
                    ? TimeSpan.FromHours(Options.IntervalHours)
                    : null),
            _loggerFactory.CreateLogger<MetadataRefreshBudget>(),
            _delayAsync);
        var state = MetadataRefreshRunState.Completed;

        try
        {
            foreach (var candidate in _pending)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IMetadataRefreshService>();
                MetadataRefreshResult? result = null;
                try
                {
                    result = await service.RefreshAsync(candidate.AudiobookId, budget, cancellationToken);
                }
                catch (ApplicationConflictException ex)
                {
                    // A refused filesystem mutation is this book's problem, not the run's: it
                    // keeps its unset timestamp and comes back next cycle.
                    _logger.LogDebug(
                        "Metadata refresh run {RunId} deferred audiobook {AudiobookId}: {Code}",
                        run.RunId,
                        candidate.AudiobookId,
                        ex.Code);
                }

                run.Record(result?.Outcome ?? MetadataRefreshOutcome.Conflict);
                run.RequestsSpent = budget.RequestsSpent;

                if (result?.Outcome is MetadataRefreshOutcome.Updated
                    or MetadataRefreshOutcome.Skipped
                    or MetadataRefreshOutcome.NotFound)
                {
                    var repository = scope.ServiceProvider.GetRequiredService<IAudiobookRepository>();
                    await repository.StampMetadataRefreshAsync(
                        candidate.AudiobookId,
                        _timeProvider.GetUtcNow().UtcDateTime,
                        cancellationToken);
                }

                if (budget.WindowClosed)
                {
                    // The rest keep their unset timestamps and stay at the head of the queue.
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            state = MetadataRefreshRunState.Cancelled;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            state = MetadataRefreshRunState.Failed;
            _logger.LogError(ex, "Metadata refresh run {RunId} failed", run.RunId);
        }
        finally
        {
            run.RequestsSpent = budget.RequestsSpent;
            run.Finish(state, _timeProvider.GetUtcNow().UtcDateTime);
            lock (_stateGate)
            {
                _last = run;
                _active = null;
                _pending = [];
            }

            _gate.Release();
        }
    }
}
