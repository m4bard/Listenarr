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
public sealed partial class MetadataRefreshCoordinator : IMetadataRefreshCoordinator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MetadataRefreshCoordinator> _logger;
    private readonly MetadataRefreshOptionsHolder _optionsHolder;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly Lock _stateGate = new();

    private MetadataRefreshRun? _active;
    private MetadataRefreshRun? _last;
    private CancellationTokenSource? _cancellation;
    private Task _inFlight = Task.CompletedTask;

    /// <summary>
    /// The most books one operator-triggered run will take on. See <see cref="ScopeLimit"/>.
    /// </summary>
    private const int OperatorScopeCandidateCeiling = 5000;

    // One bucket for the process, not one per run. Built on the first run rather than in the
    // constructor, because the operator's settings are not loaded until a run is admitted.
    private MetadataRefreshBudget? _budget;

    // The gate itself. A bool under _stateGate rather than a semaphore: every read and write of
    // it already happens inside that lock, and the semaphore added a second disposable whose
    // Release could land in a run's finally after disposal had already taken it away.
    private bool _running;

    // Test seam only: production leaves it null and the budget uses Task.Delay. A fake clock
    // that never advances would otherwise make an integration test wait in real time.
    private readonly Func<TimeSpan, CancellationToken, Task>? _delayAsync;

    public MetadataRefreshCoordinator(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        ILogger<MetadataRefreshCoordinator> logger,
        MetadataRefreshOptionsHolder optionsHolder,
        IHostApplicationLifetime lifetime,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = logger;
        _optionsHolder = optionsHolder ?? throw new ArgumentNullException(nameof(optionsHolder));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _delayAsync = delayAsync;
    }

    private MetadataRefreshOptions Options => _optionsHolder.Current;

    public async Task<MetadataRefreshStartResult> StartAsync(
        MetadataRefreshScopeRequest request,
        CancellationToken cancellationToken)
    {
        // The run deliberately does not inherit the caller's token. A web request's token is
        // cancelled as soon as the response is written, which would kill the run this call
        // exists to start; the caller stops it through Cancel instead. It is linked to the
        // host's stopping token all the same, because a run nobody is waiting on still has to
        // stop when the process does.
        var admission = await AdmitAsync(request, linkToCaller: false, cancellationToken);
        if (admission.Run == null)
        {
            return admission.Result;
        }

        var run = admission.Run;
        var token = admission.Token;
        var candidates = admission.Candidates;
        lock (_stateGate)
        {
            // Checked again here, not only at admission. The scope query in between is awaited,
            // so a DisposeAsync can run across it, capture _inFlight as already completed and
            // wait on nothing while this line schedules a run behind it. Harmless today because
            // the token is cancelled by then and Stop swallows what it finds, but the drain is
            // supposed to mean that nothing is still inside a book.
            ObjectDisposedException.ThrowIf(_disposed, this);
            _inFlight = Task.Run(
                () => ExecuteAsync(run, candidates, token),
                CancellationToken.None);
        }

        return admission.Result;
    }

    public async Task<MetadataRefreshRunSnapshot?> RunToCompletionAsync(
        MetadataRefreshScopeRequest request,
        CancellationToken cancellationToken)
    {
        // The scheduled walk runs in its caller's context and should stop when the host does,
        // so this one does take the caller's token.
        var admission = await AdmitAsync(request, linkToCaller: true, cancellationToken);
        if (admission.Run == null)
        {
            // Information, not Debug. This is the line that says the scheduled walk did nothing
            // this cycle, and an operator who triggered a library refresh and then wondered why
            // the queue stopped moving for a day had no way of seeing it at the default level.
            _logger.LogInformation(
                "Metadata refresh cycle skipped; run {RunId} is already in flight",
                admission.Result.Run.RunId);
            return null;
        }

        // Published for the drain, like the background half above. Without this a shutdown
        // during the scheduled cycle captures _inFlight as whatever the last StartAsync left
        // there, usually a completed task, waits on nothing and tears the root provider down
        // while this run is still inside a book.
        //
        // A completion source rather than the running task itself, because the task cannot be
        // started under _stateGate: ExecuteAsync runs synchronously up to its first await and
        // takes the same lock on the way out.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _inFlight = completion.Task;
        }

        try
        {
            await ExecuteAsync(admission.Run, admission.Candidates, admission.Token);
        }
        finally
        {
            completion.SetResult();
        }

        return admission.Run.ToSnapshot();
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
        CancellationTokenSource source;
        lock (_stateGate)
        {
            if (_active?.RunId != runId || _cancellation == null)
            {
                return false;
            }

            source = _cancellation;
        }

        // Cancelled outside the lock on purpose. A continuation waiting on the run token can
        // complete inline on the cancelling thread, which would run the rest of the loop, its
        // finally and all, on an API request thread that is still holding _stateGate.
        //
        // Which is also why the source can be gone by the time it is used: the run may finish
        // and the host may dispose it in the gap. That is not an error for the caller to see as
        // a 500. The run it asked to cancel is over, which is a false, the same answer it gets
        // for a run id that never matched.
        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// The outcome of asking for the gate. A null Run means the gate was held and Result
    /// carries the run holding it; otherwise Result is the admitted run, Candidates is the list
    /// of books it covers and Token is the one its loop must watch.
    /// </summary>
    private readonly record struct Admission(
        MetadataRefreshStartResult Result,
        MetadataRefreshRun? Run,
        List<MetadataRefreshCandidate> Candidates,
        CancellationToken Token);

    private async Task<Admission> AdmitAsync(
        MetadataRefreshScopeRequest request,
        bool linkToCaller,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        MetadataRefreshRun run;
        CancellationToken token;

        // Taking the gate and publishing the run happen under one lock. Publishing after the
        // scope query instead would leave a window, as wide as that query, in which the gate is
        // held and no run is visible: on a cold process a second caller refused in that window
        // would have found neither an active nor a previous run to report.
        lock (_stateGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_running)
            {
                return new Admission(
                    new MetadataRefreshStartResult(false, (_active ?? _last)!.ToSnapshot()),
                    null,
                    [],
                    CancellationToken.None);
            }

            _running = true;
            run = new MetadataRefreshRun(request.Scope, _timeProvider.GetUtcNow().UtcDateTime);
            _active = run;
            _cancellation?.Dispose();
            _cancellation = linkToCaller
                ? CancellationTokenSource.CreateLinkedTokenSource(
                    _lifetime.ApplicationStopping,
                    cancellationToken)
                : CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ApplicationStopping);
            token = _cancellation.Token;
        }

        try
        {
            var candidates = await ResolveAsync(request, cancellationToken);
            run.SetTotal(candidates.Count);

            return new Admission(
                new MetadataRefreshStartResult(true, run.ToSnapshot()),
                run,
                candidates,
                token);
        }
        catch
        {
            // The run never got as far as a book, so it is withdrawn rather than finished.
            lock (_stateGate)
            {
                _active = null;
                _running = false;
            }

            throw;
        }
    }

    private async Task<List<MetadataRefreshCandidate>> ResolveAsync(
        MetadataRefreshScopeRequest request,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        // Read here, before staleBefore is computed and before the budget is built from the same
        // holder. An API-triggered run would otherwise walk on the record defaults until the
        // scheduled cycle wrote them, ten minutes away at best and never at all with hosted
        // services off.
        await MetadataRefreshOptionsLoader.LoadAsync(
            scope.ServiceProvider,
            _optionsHolder,
            _logger,
            cancellationToken);

        var repository = scope.ServiceProvider.GetRequiredService<IAudiobookRepository>();
        var staleBefore = request.Force
            ? DateTime.MaxValue
            : _timeProvider.GetUtcNow().UtcDateTime.AddDays(-Options.StaleAfterDays);

        if (request.Scope != MetadataRefreshRunScope.Author)
        {
            var due = await repository.GetAudiobooksDueForMetadataRefreshAsync(
                staleBefore,
                ScopeLimit(request.Scope),
                cancellationToken);
            return GroupByAuthor(due);
        }

        var authors = scope.ServiceProvider.GetRequiredService<IMonitoredAuthorRepository>();
        var author = request.MonitoredAuthorId.HasValue
            ? await authors.GetByIdAsync(request.MonitoredAuthorId.Value, cancellationToken)
            : null;
        if (author == null)
        {
            // An id that names nobody is not an empty run. Accepting it handed the caller a run
            // id, a total of zero and a Completed status, which reads as "that author's books
            // are all up to date" rather than "there is no such author".
            throw new ApplicationNotFoundException(
                "monitored_author_not_found",
                "No monitored author with that id.");
        }

        var ids = await repository.GetAudiobookIdsByAuthorNameAsync(author.AuthorName, cancellationToken);
        if (!request.Force)
        {
            ids = await repository.FilterAudiobookIdsDueForMetadataRefreshAsync(
                ids,
                staleBefore,
                cancellationToken);
        }

        return ids
            .Select(id => new MetadataRefreshCandidate(id, author.AuthorName, null))
            .ToList();
    }

    /// <summary>
    /// The most books a run will take on. A scheduled cycle cannot reach more than its window
    /// can pay for, so taking the whole due set would report a total the cycle never intended to
    /// finish and make every cycle over a large library look like a partial one.
    /// </summary>
    /// <remarks>
    /// An operator-triggered library run is bounded too, just far more generously. It is
    /// windowed like every other scope, so past some point it is materialising rows it will
    /// never reach; and an unbounded limit means a library-wide trigger loads the whole library
    /// into a list and holds it for the run. The ceiling costs the operator nothing they can
    /// see: the due set is ordered stalest first, a run that stops short ends as Truncated, and
    /// the books it did not reach keep their unset timestamps and stay at the head of the queue
    /// for the next trigger or the next cycle.
    /// <para>
    /// The author scope is not limited here at all. One author's catalogue is bounded by the
    /// author, and an operator who asked about one author is owed all of them.
    /// </para>
    /// </remarks>
    private int ScopeLimit(MetadataRefreshRunScope scope) =>
        scope == MetadataRefreshRunScope.Scheduled
            ? Math.Max(1, Options.RequestsPerHour * Options.IntervalHours)
            : OperatorScopeCandidateCeiling;

    /// <summary>
    /// The shared bucket, reconfigured from the settings this run was admitted with, and a view
    /// of it bounded by the window this run may wait in.
    /// </summary>
    /// <remarks>
    /// Every scope is windowed, including the ones an operator triggers. A library-wide run over
    /// ten thousand books walks for the better part of a week at the shipped budget, and it holds
    /// the single gate the whole time: every scheduled cycle in that week found the gate taken
    /// and did nothing. A windowed run ends as Truncated with its unreached books still unstamped
    /// and still at the head of the queue, which is where the scheduled walk will find them.
    /// </remarks>
    private MetadataRefreshRunBudget AcquireBudget()
    {
        var window = TimeSpan.FromHours(Math.Max(1, Options.IntervalHours));
        lock (_stateGate)
        {
            _budget ??= new MetadataRefreshBudget(
                _timeProvider,
                new MetadataRefreshBudgetOptions(Options.RequestsPerHour, Options.MinimumSpacingMs),
                _loggerFactory.CreateLogger<MetadataRefreshBudget>(),
                _delayAsync);
            _budget.Reconfigure(Options.RequestsPerHour, Options.MinimumSpacingMs);
            return _budget.BeginRun(window);
        }
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
}
