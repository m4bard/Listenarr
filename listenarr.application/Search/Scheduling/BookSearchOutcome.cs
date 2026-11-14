/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using Listenarr.Application.Search.Contracts;

namespace Listenarr.Application.Search.Scheduling;

/// <summary>
/// How one book's automatic search ended. Decides the scheduling response per book.
/// </summary>
public enum BookSearchOutcome
{
    Satisfied,
    GrabNotCompleted,
    NoAcceptableResults,
    PartiallyCompleted,
    Failed,
    Deferred,
    BookFailed,
    Cancelled,
    NotNeeded
}

/// <summary>
/// What happened to the acceptable release the search found.
/// </summary>
public enum GrabResult
{
    NotAttempted,
    Queued,
    NotCompleted
}

public static class BookSearchOutcomeDecision
{
    /// <summary>
    /// The outcome the per-indexer outcomes, the scoring result and the grab decide between them.
    /// BookFailed is not decided here: it comes from an exception outside the indexer calls.
    /// A queued download wins over any other indexer's status; otherwise a cancelled indexer makes
    /// the book Cancelled. A Deferred indexer never counts as a Completed one (it cannot add to the
    /// no-result ladder), and <paramref name="grabResult"/> is read only when
    /// <paramref name="acceptableFound"/> is true.
    /// </summary>
    public static BookSearchOutcome Decide(
        IReadOnlyList<IndexerSearchOutcome> outcomes, bool acceptableFound, GrabResult grabResult)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        if (acceptableFound && grabResult == GrabResult.Queued)
        {
            return BookSearchOutcome.Satisfied;
        }

        if (outcomes.Any(outcome => outcome.Status == IndexerSearchStatus.Cancelled))
        {
            return BookSearchOutcome.Cancelled;
        }

        if (acceptableFound)
        {
            return BookSearchOutcome.GrabNotCompleted;
        }

        if (outcomes.Count == 0)
        {
            return BookSearchOutcome.NotNeeded;
        }

        var completed = outcomes.Count(outcome => outcome.Status == IndexerSearchStatus.Completed);
        if (completed == outcomes.Count)
        {
            return BookSearchOutcome.NoAcceptableResults;
        }

        if (completed > 0)
        {
            return BookSearchOutcome.PartiallyCompleted;
        }

        return outcomes.Any(outcome => outcome.Status == IndexerSearchStatus.Deferred)
            ? BookSearchOutcome.Deferred
            : BookSearchOutcome.Failed;
    }
}
