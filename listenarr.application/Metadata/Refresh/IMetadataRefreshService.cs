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

    /// <summary>
    /// Nothing was asked: no ASIN and no ISBN, or none of them usable and no resolver for the
    /// ones that were. Stamped so it leaves the queue head. No provider had any part in it.
    /// </summary>
    Skipped,

    /// <summary>
    /// Identifiers and regions exhausted and every one of them answered, all of them with
    /// nothing. Stamped; local metadata untouched. A walk that ended the same way but had a
    /// transient failure somewhere in it, or that got no answer at all, is
    /// <see cref="Deferred"/>, because a provider that would not answer is not evidence that the
    /// book is gone.
    /// </summary>
    NotFound,

    /// <summary>The book changed while the provider was answering. Not stamped; retried next cycle.</summary>
    Conflict,

    /// <summary>
    /// The provider pushed back, or a transient failure left some part of the walk unanswered,
    /// or the run's allowance ran out. Not stamped, so the next cycle asks again.
    /// </summary>
    Deferred,

    /// <summary>The book vanished, or the write was rejected.</summary>
    Failed
}

/// <summary>
/// The result of refreshing one book, including what it cost. <c>RequestsSpent</c> is the
/// provider requests this book alone consumed, not the run's running total.
/// </summary>
/// <remarks>
/// <c>ProviderAnswers</c> is how many of this book's provider requests came back with a verdict
/// rather than an exception. A caller may only stamp
/// <see cref="MetadataRefreshOutcome.NotFound"/> when it is above zero: the provider answers
/// null for a book it has never heard of and raises when it could not be asked, so an outcome
/// reached with no answers is silence rather than absence, and stamping silence hides the book
/// for the whole staleness window.
/// </remarks>
public sealed record MetadataRefreshResult(
    MetadataRefreshOutcome Outcome,
    int RequestsSpent,
    string? Source = null,
    string? Asin = null,
    string? Region = null,
    int ProviderAnswers = 0);

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
    /// Raised for <see cref="MetadataProviderThrottledException"/>, and for an
    /// <see cref="HttpRequestException"/> carrying <c>TooManyRequests</c>, and for nothing else:
    /// an ordinary transient fault is retried without narrowing the run, because halving on
    /// every timeout empties the allowance in a couple of books.
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
/// <para>
/// The retry ceiling is per book and per run, not per region: a book is retried in place at
/// most twice, and the attempt that exceeds that moves the walk on to the next region or
/// identifier rather than ending it, so every region still gets asked at least once. Pushback
/// is the exception, and stops the book where it stands, because it is the provider asking for
/// less rather than one region failing.
/// </para>
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
