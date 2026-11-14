/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Application.Search.Scheduling;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.Scheduling
{
    /// <summary>
    /// One test row per row of the book-outcome table: the per-indexer statuses, whether an
    /// acceptable release was found and what became of it decide the book's outcome. A deferred
    /// indexer never counts as a completed one, so it can never grow the no-result ladder.
    /// </summary>
    [Trait("Name", "BookSearchOutcomeDecisionTests")]
    [Trait("Category", "AutomaticSearchSchedule")]
    public class BookSearchOutcomeDecisionTests : BaseTests
    {
        private const IndexerSearchStatus C = IndexerSearchStatus.Completed;
        private const IndexerSearchStatus F = IndexerSearchStatus.Failed;
        private const IndexerSearchStatus D = IndexerSearchStatus.Deferred;
        private const IndexerSearchStatus X = IndexerSearchStatus.Cancelled;

        public static TheoryData<IndexerSearchStatus[], bool, GrabResult, BookSearchOutcome> Rows => new()
        {
            // Satisfied: an acceptable release was found and queued, whatever the other indexers did.
            { new[] { C }, true, GrabResult.Queued, BookSearchOutcome.Satisfied },
            { new[] { C, F }, true, GrabResult.Queued, BookSearchOutcome.Satisfied },
            { new[] { C, D }, true, GrabResult.Queued, BookSearchOutcome.Satisfied },
            { new[] { D }, true, GrabResult.Queued, BookSearchOutcome.Satisfied },
            { new[] { C, X }, true, GrabResult.Queued, BookSearchOutcome.Satisfied },
            // GrabNotCompleted: found, not queued.
            { new[] { C }, true, GrabResult.NotCompleted, BookSearchOutcome.GrabNotCompleted },
            { new[] { C, F }, true, GrabResult.NotCompleted, BookSearchOutcome.GrabNotCompleted },
            { new[] { D }, true, GrabResult.NotCompleted, BookSearchOutcome.GrabNotCompleted },
            { new[] { C }, true, GrabResult.NotAttempted, BookSearchOutcome.GrabNotCompleted },
            // NoAcceptableResults: every indexer Completed, nothing acceptable.
            { new[] { C }, false, GrabResult.NotAttempted, BookSearchOutcome.NoAcceptableResults },
            { new[] { C, C, C }, false, GrabResult.NotAttempted, BookSearchOutcome.NoAcceptableResults },
            // PartiallyCompleted: one Completed with nothing acceptable, one Failed or Deferred.
            { new[] { C, F }, false, GrabResult.NotAttempted, BookSearchOutcome.PartiallyCompleted },
            { new[] { C, D }, false, GrabResult.NotAttempted, BookSearchOutcome.PartiallyCompleted },
            { new[] { C, F, D }, false, GrabResult.NotAttempted, BookSearchOutcome.PartiallyCompleted },
            { new[] { D, C, C }, false, GrabResult.NotAttempted, BookSearchOutcome.PartiallyCompleted },
            // Failed: none Completed, at least one Failed, none Deferred.
            { new[] { F }, false, GrabResult.NotAttempted, BookSearchOutcome.Failed },
            { new[] { F, F }, false, GrabResult.NotAttempted, BookSearchOutcome.Failed },
            // Deferred: none Completed, at least one Deferred (M5: never NoAcceptableResults).
            { new[] { D }, false, GrabResult.NotAttempted, BookSearchOutcome.Deferred },
            { new[] { D, D }, false, GrabResult.NotAttempted, BookSearchOutcome.Deferred },
            { new[] { F, D }, false, GrabResult.NotAttempted, BookSearchOutcome.Deferred },
            // Cancelled: the cycle token was cancelled, and nothing was queued.
            { new[] { X }, false, GrabResult.NotAttempted, BookSearchOutcome.Cancelled },
            { new[] { C, X }, false, GrabResult.NotAttempted, BookSearchOutcome.Cancelled },
            { new[] { C, X }, true, GrabResult.NotCompleted, BookSearchOutcome.Cancelled },
            // NotNeeded: no pair was due, so no indexer was asked.
            { Array.Empty<IndexerSearchStatus>(), false, GrabResult.NotAttempted, BookSearchOutcome.NotNeeded },
        };

        [Theory]
        [MemberData(nameof(Rows))]
        public void Decide_FollowsTheOutcomeTable(
            IndexerSearchStatus[] statuses, bool acceptableFound, GrabResult grab, BookSearchOutcome expected)
        {
            var outcomes = statuses.Select((status, i) => Outcome(i + 1, status)).ToList();

            Assert.Equal(expected, BookSearchOutcomeDecision.Decide(outcomes, acceptableFound, grab));
        }

        [Fact]
        public void Decide_IgnoresAGrabResultWhenNothingAcceptableWasFound()
        {
            var outcomes = new[] { Outcome(1, C), Outcome(2, D) };

            Assert.Equal(
                BookSearchOutcome.PartiallyCompleted,
                BookSearchOutcomeDecision.Decide(outcomes, acceptableFound: false, GrabResult.Queued));
        }

        [Fact]
        public void Decide_NullOutcomes_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => BookSearchOutcomeDecision.Decide(null!, false, GrabResult.NotAttempted));
        }

        private static IndexerSearchOutcome Outcome(int indexerId, IndexerSearchStatus status) =>
            new(indexerId, status, Array.Empty<IndexerSearchResult>(), RequestsSent: 1);
    }
}
