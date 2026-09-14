/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.Indexers;

/// <summary>
/// The polarity decisions, pinned one by one. Every pair here passes on an implementation that
/// treats all failures identically unless its opposite is also asserted, which is why they are
/// written as a table rather than as a handful of representative cases.
/// </summary>
[Trait("Area", "Search")]
[Trait("Name", "IndexerBackoffPolicyTests")]
[Trait("Category", "IndexerBackoffPolicy")]
public sealed class IndexerBackoffPolicyTests : BaseTests
{
    [Theory]
    [InlineData(IndexerQueryOutcome.Hit, IndexerQueryReason.None)]
    [InlineData(IndexerQueryOutcome.NoMatch, IndexerQueryReason.EmptyChannel)]
    [Trait("Method", "Classify")]
    [Trait("Scenario", "ReadableAnswer")]
    public void Classify_ReadableAnswer_IsSuccessEvenWhenItCarriedNothing(
        IndexerQueryOutcome outcome,
        IndexerQueryReason reason)
    {
        Assert.Equal(IndexerBackoffSignal.Success, IndexerBackoffPolicy.Classify(outcome, reason));
    }

    [Theory]
    [InlineData(IndexerQueryOutcome.Unavailable, IndexerQueryReason.Timeout)]
    [InlineData(IndexerQueryOutcome.Unavailable, IndexerQueryReason.HttpStatus)]
    [InlineData(IndexerQueryOutcome.Unavailable, IndexerQueryReason.AuthFailure)]
    [InlineData(IndexerQueryOutcome.Unreadable, IndexerQueryReason.MalformedXml)]
    [InlineData(IndexerQueryOutcome.Unreadable, IndexerQueryReason.MissingChannel)]
    [Trait("Method", "Classify")]
    [Trait("Scenario", "RemoteSideFailure")]
    public void Classify_FailureAttributableToTheIndexer_Escalates(
        IndexerQueryOutcome outcome,
        IndexerQueryReason reason)
    {
        Assert.Equal(IndexerBackoffSignal.Escalate, IndexerBackoffPolicy.Classify(outcome, reason));
    }

    [Fact]
    [Trait("Method", "Classify")]
    [Trait("Scenario", "TransportFailure")]
    public void Classify_NetworkError_HoldsRatherThanEscalating()
    {
        // The opposite direction from the rate-limit case below. One dropped VPN or one dead proxy
        // takes out every indexer behind it in the same instant; escalating all of them toward a
        // day-long ceiling for an outage on our own side is self-harm.
        Assert.Equal(
            IndexerBackoffSignal.Hold,
            IndexerBackoffPolicy.Classify(IndexerQueryOutcome.Unavailable, IndexerQueryReason.NetworkError));
    }

    [Fact]
    [Trait("Method", "Classify")]
    [Trait("Scenario", "RateLimited")]
    public void Classify_RateLimited_IsItsOwnSignal()
    {
        Assert.Equal(
            IndexerBackoffSignal.RateLimited,
            IndexerBackoffPolicy.Classify(IndexerQueryOutcome.Unavailable, IndexerQueryReason.RateLimited));
    }

    [Theory]
    [InlineData(IndexerQueryOutcome.NotConfigured, IndexerQueryReason.NoProviderForImplementation)]
    [InlineData(IndexerQueryOutcome.NotConfigured, IndexerQueryReason.None)]
    [InlineData(IndexerQueryOutcome.Unavailable, IndexerQueryReason.Cancelled)]
    [Trait("Method", "Classify")]
    [Trait("Scenario", "NothingObserved")]
    public void Classify_NoRequestReachedTheIndexer_ChangesNothing(
        IndexerQueryOutcome outcome,
        IndexerQueryReason reason)
    {
        // A missing credential and a caller-initiated cancel are both failures of ours. Neither is
        // evidence about whether the indexer is up, so neither may move its rung.
        Assert.Equal(IndexerBackoffSignal.Ignore, IndexerBackoffPolicy.Classify(outcome, reason));
    }

    [Fact]
    [Trait("Method", "Classify")]
    [Trait("Scenario", "ObservationOverload")]
    public void Classify_Observation_AgreesWithTheOutcomeAndReasonOverload()
    {
        var observation = IndexerQueryObservation.Unavailable(
            IndexerQueryReason.RateLimited,
            "Alice",
            "429",
            retryAfter: TimeSpan.FromMinutes(5));

        Assert.Equal(IndexerBackoffSignal.RateLimited, IndexerBackoffPolicy.Classify(observation));
    }
}
