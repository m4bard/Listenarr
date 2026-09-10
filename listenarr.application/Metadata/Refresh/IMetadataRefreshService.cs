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

namespace Listenarr.Application.Metadata.Refresh;

/// <summary>What one book's refresh did. The caller counts these and decides whether to stamp.</summary>
public enum MetadataRefreshOutcome
{
    /// <summary>Provider metadata was applied.</summary>
    Updated,

    /// <summary>Nothing to ask about: no ASIN and no ISBN. Stamped so it leaves the queue head.</summary>
    Skipped,

    /// <summary>Identifiers and regions exhausted, nothing returned. Stamped; local metadata untouched.</summary>
    NotFound,

    /// <summary>The book changed while the provider was answering. Not stamped; retried next cycle.</summary>
    Conflict,

    /// <summary>Transient provider failure past the retry ceiling, or no budget. Not stamped.</summary>
    Deferred,

    /// <summary>The book vanished, or the write was rejected.</summary>
    Failed
}

/// <summary>
/// The result of refreshing one book, including what it cost. <c>RequestsSpent</c> is the
/// provider requests this book alone consumed, not the run's running total.
/// </summary>
public sealed record MetadataRefreshResult(
    MetadataRefreshOutcome Outcome,
    int RequestsSpent,
    string? Source = null,
    string? Asin = null,
    string? Region = null);

/// <summary>
/// The run's provider-request allowance. Charged once per provider request, inside the region
/// loop rather than around it, because one book is one to thirteen requests.
/// </summary>
public interface IMetadataRefreshBudget
{
    /// <summary>Total requests granted so far in this run.</summary>
    int RequestsSpent { get; }

    /// <summary>
    /// Waits for the next slot and takes it. False means the run's allowance is finished and the
    /// caller should defer the book rather than fail it.
    /// </summary>
    Task<bool> ChargeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The provider pushed back. Halves what remains for the rest of the run, and waits out
    /// <paramref name="retryAfter"/> before the next grant when the provider named one.
    /// </summary>
    void ApplyThrottleSignal(TimeSpan? retryAfter);
}

/// <summary>
/// Refreshes one book's metadata from the provider. No HttpContext, no per-actor quota and no
/// IActionResult: those belong to the API adapter that also calls this.
/// </summary>
/// <remarks>
/// One failure is not an outcome and is thrown instead. A filesystem mutation the move queue
/// refuses raises <see cref="ApplicationConflictException"/>, which carries a code and a safe
/// detail naming what has to be resolved first. Those two strings have no home on
/// <see cref="MetadataRefreshResult"/>, and the API adapter reports them verbatim, so the
/// exception is left to propagate rather than flattened into an outcome.
/// </remarks>
public interface IMetadataRefreshService
{
    /// <summary>Refreshes one book, charging <paramref name="budget"/> per provider request.</summary>
    /// <exception cref="ApplicationConflictException">
    /// The book cannot be written right now, typically because a move is unresolved. A caller
    /// working through a list must catch this per book and treat it as
    /// <see cref="MetadataRefreshOutcome.Conflict"/>, leaving the book unstamped for the next
    /// cycle, rather than letting one blocked book end the run.
    /// </exception>
    Task<MetadataRefreshResult> RefreshAsync(
        int audiobookId,
        IMetadataRefreshBudget budget,
        CancellationToken cancellationToken);
}
