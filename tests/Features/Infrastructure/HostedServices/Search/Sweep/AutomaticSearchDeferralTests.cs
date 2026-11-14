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

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search.Sweep
{
    // How a deferral reaches the schedule: a held-back .torrent or NZB fetch is a grab that did not
    // complete (not a book failure), and a failing indexer's cooldown floors its pair's next attempt.
    [Trait("Name", "AutomaticSearchDeferralTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class AutomaticSearchDeferralTests : AutomaticSearchSweepTestBase
    {
        [Fact]
        public async Task GrabFetchDeferred_IsGrabNotCompleted()
        {
            await ConfigureAsync();
            var indexer = await AddIndexerAsync("Only");
            var book = (await AddBooksAsync(1, StartUtc.AddMinutes(-5))).Single();
            Indexers.Script = (_, _) => Returns(AcceptableSource);
            var untilUtc = StartUtc.AddHours(3);
            Downloads
                .Setup(d => d.StartDownloadAsync(It.IsAny<SearchResult>(), It.IsAny<string>(), It.IsAny<int?>()))
                .ThrowsAsync(new IndexerRequestDeferredException(new RequestDeferral(untilUtc, RequestDeferralReason.FailureLadder)));

            await RunCycleAsync();

            var (dueUtc, failureCount, _, _) = await Database.ReadBookAsync(book.Id);
            Assert.Equal((untilUtc, 0), (dueUtc, failureCount));
            var pair = await Database.ReadPairAsync(book.Id, indexer);
            Assert.NotNull(pair);
            Assert.Equal(
                (nameof(PairScheduleResult.GrabNotCompleted), 0, 0, untilUtc),
                (pair.LastOutcome, pair.NoResultCount, pair.FailureCount, pair.NextAttemptUtc));
        }

        [Fact]
        public async Task FailingIndexerCooldown_FloorsThePairsNextAttempt()
        {
            await ConfigureAsync();
            var cooling = await AddIndexerAsync("Cooling");
            var book = (await AddBooksAsync(1, StartUtc.AddMinutes(-5))).Single();
            var untilUtc = StartUtc.AddHours(3);
            Gate.DeferUntil[cooling] = untilUtc;
            Gate.DeferReason = RequestDeferralReason.FailureLadder;

            await RunCycleAsync();

            var pair = await Database.ReadPairAsync(book.Id, cooling);
            Assert.NotNull(pair);
            Assert.Equal(
                (nameof(PairScheduleResult.Failed), 0, 1),
                (pair.LastOutcome, pair.NoResultCount, pair.FailureCount));
            Assert.True(pair.NextAttemptUtc >= untilUtc, $"next attempt {pair.NextAttemptUtc:O} is before the cooldown ends {untilUtc:O}");
        }
    }
}
