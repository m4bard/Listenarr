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
/// The shipped defaults are deliberately timid: the provider publishes no limit and the client
/// does not recognise a 429, so 60 an hour with a one-second floor still clears a couple of
/// thousand books inside a 30-day staleness target.
/// </summary>
/// <param name="RequestsPerHour">Bucket capacity and refill rate.</param>
/// <param name="MinimumSpacingMs">Floor between two grants, so a full bucket cannot burst.</param>
/// <param name="RunWindow">How long this run may keep waiting. Null waits until cancelled.</param>
public sealed record MetadataRefreshBudgetOptions(
    int RequestsPerHour = 60,
    int MinimumSpacingMs = 1000,
    TimeSpan? RunWindow = null);

/// <summary>
/// A token bucket over provider requests, shared by every book in one run.
/// </summary>
public sealed class MetadataRefreshBudget : IMetadataRefreshBudget
{
    private const double SecondsPerHour = 3600d;

    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MetadataRefreshBudget> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly Lock _gate = new();
    private readonly DateTimeOffset _startedAt;
    private readonly TimeSpan? _runWindow;
    private readonly TimeSpan _minimumSpacing;

    private double _capacity;
    private double _tokens;
    private DateTimeOffset _lastRefill;
    private DateTimeOffset? _lastGrant;
    private DateTimeOffset? _notBefore;
    private int _spent;

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
        _capacity = Math.Max(1d, options.RequestsPerHour);
        _tokens = _capacity;
        _minimumSpacing = TimeSpan.FromMilliseconds(Math.Max(0, options.MinimumSpacingMs));
        _runWindow = options.RunWindow;
        _startedAt = timeProvider.GetUtcNow();
        _lastRefill = _startedAt;
    }

    public int RequestsSpent => Volatile.Read(ref _spent);

    /// <summary>
    /// True once a charge has been refused because the next slot fell outside the run window.
    /// The run is finished; the books it did not reach keep their unset timestamps.
    /// </summary>
    public bool WindowClosed { get; private set; }

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

    public async Task<bool> ChargeAsync(CancellationToken cancellationToken)
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

                if (_runWindow.HasValue && now + wait > _startedAt + _runWindow.Value)
                {
                    WindowClosed = true;
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
            if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero)
            {
                _notBefore = now + retryAfter.Value;
            }
        }
    }

    private void Refill(DateTimeOffset now)
    {
        var elapsed = now - _lastRefill;
        if (elapsed <= TimeSpan.Zero)
        {
            return;
        }

        _tokens = Math.Min(_capacity, _tokens + (elapsed.TotalSeconds * (_capacity / SecondsPerHour)));
        _lastRefill = now;
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
