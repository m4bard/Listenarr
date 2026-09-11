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

namespace Listenarr.Application.Metadata.Refresh;

/// <summary>
/// The shipped defaults are deliberately timid: the provider publishes no limit, so 60 an hour
/// with a one-second floor still clears a couple of thousand books inside a 30-day staleness
/// target. The client does recognise a 429 and the run narrows itself when one arrives, but a
/// default nobody has to notice is better than one that relies on the provider complaining.
/// </summary>
/// <param name="RequestsPerHour">Bucket capacity and refill rate.</param>
/// <param name="MinimumSpacingMs">Floor between two grants, so a full bucket cannot burst.</param>
public sealed record MetadataRefreshBudgetOptions(
    int RequestsPerHour = 60,
    int MinimumSpacingMs = 1000);

/// <summary>
/// A token bucket over provider requests, shared by every refresh run in the process.
/// </summary>
/// <remarks>
/// One bucket, not one per run. A bucket built inside a run started full and granted its first
/// request immediately, so POST the trigger, DELETE the run, POST it again was a fresh allowance
/// every time, and a scheduled cycle plus one manual run inside an hour spent twice the
/// operator's ceiling between them. An hourly ceiling that resets on every trigger is not a
/// ceiling.
/// <para>
/// A run takes a view of this bucket through <see cref="BeginRun"/>. The view owns what is
/// genuinely per run, which is the window it may keep waiting in and what it has spent; the
/// tokens, the spacing floor and any pushback the provider asked for are the bucket's and
/// outlive it.
/// </para>
/// </remarks>
public sealed class MetadataRefreshBudget : IMetadataRefreshBudget
{
    private const double SecondsPerHour = 3600d;

    /// <summary>
    /// How long the bucket must go without pushback before it gives half of a narrowing back.
    /// An hour, because the bucket is priced in requests per hour and one quiet hour is the
    /// smallest interval that says the provider has stopped complaining. Something has to give
    /// it back: the bucket lives as long as the process, so a narrowing that never recovers
    /// leaves an instance that saw one 429 last month running at half rate today.
    /// </summary>
    internal static readonly TimeSpan PushbackRecoveryInterval = TimeSpan.FromHours(1);

    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MetadataRefreshBudget> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly Lock _gate = new();

    private TimeSpan _minimumSpacing;
    private double _capacity;
    private double _tokens;

    // What the operator's settings last said, so a reconfigure can tell a changed setting from
    // the same setting read again. Re-applying the same number would undo a halving, and a run
    // cancelled and restarted would clear the pushback the provider had just asked for.
    private int _configuredRequestsPerHour;

    private DateTimeOffset _lastRefill;
    private DateTimeOffset? _lastGrant;
    private DateTimeOffset? _notBefore;
    private int _spent;

    // When pushback last halved the capacity, or null when the capacity is the operator's.
    // Nothing else restores it: Reconfigure deliberately ignores a setting that has not
    // changed, so without this a single 429 held a long-lived process at half rate until it
    // was restarted, and two held it at a quarter.
    private DateTimeOffset? _narrowedAt;

    public MetadataRefreshBudget(
        TimeProvider timeProvider,
        MetadataRefreshBudgetOptions options,
        ILogger<MetadataRefreshBudget> logger,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);

        _timeProvider = timeProvider;
        _logger = logger;
        _delayAsync = delayAsync ?? ((delay, token) => Task.Delay(delay, token));
        _configuredRequestsPerHour = Math.Max(1, options.RequestsPerHour);
        _capacity = _configuredRequestsPerHour;
        _tokens = _capacity;
        _minimumSpacing = TimeSpan.FromMilliseconds(Math.Max(0, options.MinimumSpacingMs));
        _lastRefill = timeProvider.GetUtcNow();
    }

    /// <summary>Requests granted since the process started, across every run.</summary>
    public int RequestsSpent => Volatile.Read(ref _spent);

    /// <summary>Whole tokens available right now, for the throttling log line.</summary>
    public int RemainingThisHour
    {
        get
        {
            lock (_gate)
            {
                Refill(_timeProvider.GetUtcNow());
                return (int)Math.Floor(_tokens);
            }
        }
    }

    /// <summary>
    /// Applies the operator's current settings to the shared bucket, without crediting it. Called
    /// as each run is admitted, so a settings change takes effect on the next run rather than on
    /// the next restart.
    /// </summary>
    /// <remarks>
    /// A setting that has not changed is left alone rather than re-applied. Re-applying it would
    /// restore a capacity that pushback had halved, which would turn cancel-and-restart into a
    /// way of clearing a 429 the provider had just sent. A halving is given back by quiet time
    /// instead, in <see cref="PushbackRecoveryInterval"/> steps, which no caller can fake.
    /// </remarks>
    public void Reconfigure(int requestsPerHour, int minimumSpacingMs)
    {
        var wanted = Math.Max(1, requestsPerHour);
        lock (_gate)
        {
            _minimumSpacing = TimeSpan.FromMilliseconds(Math.Max(0, minimumSpacingMs));
            if (wanted == _configuredRequestsPerHour)
            {
                return;
            }

            // Credit what elapsed at the old rate before changing it, or a rate change would
            // silently re-price time the bucket had already earned.
            Refill(_timeProvider.GetUtcNow());
            _configuredRequestsPerHour = wanted;
            _capacity = wanted;
            _tokens = Math.Min(_tokens, _capacity);

            // The operator has just named the rate outright, so there is no narrowing left to
            // ramp back from.
            _narrowedAt = null;
        }
    }

    /// <summary>
    /// A view of this bucket for one run. <paramref name="runWindow"/> is how long that run may
    /// keep waiting for a slot; null waits until it is cancelled.
    /// </summary>
    public MetadataRefreshRunBudget BeginRun(TimeSpan? runWindow) =>
        new(this, runWindow.HasValue ? _timeProvider.GetUtcNow() + runWindow.Value : null);

    public Task<bool> ChargeAsync(CancellationToken cancellationToken) =>
        ChargeAsync(deadline: null, cancellationToken);

    /// <summary>
    /// Waits for the next slot and takes it. False means the next slot falls after
    /// <paramref name="deadline"/>, so the caller's run is out of time rather than out of tokens.
    /// </summary>
    internal async Task<bool> ChargeAsync(DateTimeOffset? deadline, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TimeSpan wait;
            int remaining;
            lock (_gate)
            {
                var now = _timeProvider.GetUtcNow();
                Refill(now);
                wait = WaitFrom(now);
                if (wait <= TimeSpan.Zero)
                {
                    _tokens -= 1d;
                    _lastGrant = now;
                    _notBefore = null;
                    Interlocked.Increment(ref _spent);
                    return true;
                }

                if (deadline.HasValue && now + wait > deadline.Value)
                {
                    return false;
                }

                remaining = (int)Math.Floor(_tokens);
            }

            _logger.LogDebug(
                "Metadata refresh throttled; {Remaining} request(s) of hourly budget remain, next slot in {Seconds}s",
                remaining,
                Math.Ceiling(wait.TotalSeconds));
            await _delayAsync(wait, cancellationToken);
        }
    }

    public void ApplyThrottleSignal(TimeSpan? retryAfter)
    {
        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow();
            Refill(now);
            _capacity = Math.Max(1d, _capacity / 2d);
            _tokens = Math.Min(_tokens, _capacity);
            _narrowedAt = now;
            if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero)
            {
                _notBefore = now + retryAfter.Value;
            }
        }
    }

    private void Refill(DateTimeOffset now)
    {
        var elapsed = now - _lastRefill;
        if (elapsed > TimeSpan.Zero)
        {
            _tokens = Math.Min(_capacity, _tokens + (elapsed.TotalSeconds * (_capacity / SecondsPerHour)));
            _lastRefill = now;
        }

        // After the credit, never before it: the quiet time is earned at the rate that was in
        // force while it elapsed, the same reasoning Reconfigure uses for a changed setting.
        RecoverFromPushback(now);
    }

    /// <summary>
    /// Gives the halving back one doubling per quiet interval, up to the operator's rate. A
    /// narrowing is meant to last the cycle it happened in, not the life of the process.
    /// </summary>
    private void RecoverFromPushback(DateTimeOffset now)
    {
        if (_narrowedAt is null)
        {
            return;
        }

        var quiet = now - _narrowedAt.Value;
        if (quiet < PushbackRecoveryInterval)
        {
            return;
        }

        // Capped so a clock that jumped years cannot overflow the doubling; the result is
        // clamped to the configured rate anyway.
        var intervals = Math.Min(32d, Math.Floor(quiet / PushbackRecoveryInterval));
        var widened = _capacity * Math.Pow(2d, intervals);
        if (widened >= _configuredRequestsPerHour)
        {
            _capacity = _configuredRequestsPerHour;
            _narrowedAt = null;
            return;
        }

        _capacity = widened;
        _narrowedAt = _narrowedAt.Value + (intervals * PushbackRecoveryInterval);
    }

    private TimeSpan WaitFrom(DateTimeOffset now)
    {
        var wait = TimeSpan.Zero;

        if (_lastGrant.HasValue)
        {
            var spacing = _lastGrant.Value + _minimumSpacing - now;
            if (spacing > wait)
            {
                wait = spacing;
            }
        }

        if (_notBefore.HasValue)
        {
            var pushback = _notBefore.Value - now;
            if (pushback > wait)
            {
                wait = pushback;
            }
        }

        if (_tokens < 1d)
        {
            var refill = TimeSpan.FromSeconds((1d - _tokens) * (SecondsPerHour / _capacity));
            if (refill > wait)
            {
                wait = refill;
            }
        }

        return wait;
    }
}

/// <summary>
/// One run's view of the shared bucket: the window it may wait in, and what it spent. Everything
/// that has to survive the run, tokens and pushback included, stays on the bucket.
/// </summary>
public sealed class MetadataRefreshRunBudget : IMetadataRefreshBudget
{
    private readonly MetadataRefreshBudget _budget;
    private readonly DateTimeOffset? _deadline;
    private int _spent;

    internal MetadataRefreshRunBudget(MetadataRefreshBudget budget, DateTimeOffset? deadline)
    {
        _budget = budget;
        _deadline = deadline;
    }

    /// <summary>Requests this run granted, which is not what the shared bucket has spent.</summary>
    public int RequestsSpent => Volatile.Read(ref _spent);

    /// <summary>
    /// True once a charge has been refused because the next slot fell outside the run window.
    /// The run is finished; the books it did not reach keep their unset timestamps.
    /// </summary>
    public bool WindowClosed { get; private set; }

    public async Task<bool> ChargeAsync(CancellationToken cancellationToken)
    {
        if (await _budget.ChargeAsync(_deadline, cancellationToken))
        {
            Interlocked.Increment(ref _spent);
            return true;
        }

        WindowClosed = true;
        return false;
    }

    public void ApplyThrottleSignal(TimeSpan? retryAfter) => _budget.ApplyThrottleSignal(retryAfter);
}
