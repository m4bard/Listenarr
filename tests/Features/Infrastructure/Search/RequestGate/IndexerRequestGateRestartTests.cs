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

using System.Net;
using System.Net.Http.Headers;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Common;
using Listenarr.Tests.Features.Infrastructure.Persistence;
using static Listenarr.Tests.Features.Infrastructure.Search.RequestGate.IndexerRequestGateHarness;

namespace Listenarr.Tests.Features.Infrastructure.Search.RequestGate;

[Trait("Name", "IndexerRequestGateRestartTests")]
[Trait("Category", "Infrastructure")]
public sealed class IndexerRequestGateRestartTests : BaseTests
{
    [Theory]
    [InlineData(RequestDeferralReason.RateLimited)]
    [InlineData(RequestDeferralReason.FailureLadder)]
    public async Task Cooldown_SurvivesRestart_NewGateDefersWithoutContactingIndexer(RequestDeferralReason reason)
    {
        // Given
        await using var database = await IndexerRequestStateTestDatabase.CreateAsync(indexerCount: 1);
        var indexerId = database.IndexerIds[0];
        var cooldownResponse = reason == RequestDeferralReason.RateLimited
            ? Sequence(() => RateLimited(new RetryConditionHeaderValue(TimeSpan.FromHours(1))))
            : Sequence(() => Status(HttpStatusCode.BadGateway));
        var requestsBeforeRestart = reason == RequestDeferralReason.RateLimited ? 1 : IndexerRequestPolicy.FailureThreshold;
        RequestDeferral? cooldownBeforeRestart;
        using (var beforeRestart = new IndexerRequestGateHarness(
            new EfIndexerRequestStateStore(database.Factory),
            cooldownResponse))
        {
            for (var i = 0; i < requestsBeforeRestart; i++)
            {
                await beforeRestart.SendAsync(indexerId);
            }

            cooldownBeforeRestart = await beforeRestart.Gate.GetDeferralAsync(indexerId, SearchOrigin.Sweep, CancellationToken.None);
        }

        // When: a new cache and gate over the same database file.
        using var afterRestart = new IndexerRequestGateHarness(
            new EfIndexerRequestStateStore(database.Factory),
            Sequence(() => Status(HttpStatusCode.OK)),
            clock: new ManualClock(Start.AddMinutes(1).AddSeconds(-1)));
        var result = await afterRestart.SendAsync(indexerId);
        var peeked = await afterRestart.Gate.GetDeferralAsync(indexerId, SearchOrigin.Interactive, CancellationToken.None);

        // Then
        var cooldown = Assert.IsType<RequestDeferral>(cooldownBeforeRestart);
        Assert.Equal(reason, cooldown.Reason);
        Assert.Equal(cooldown, Assert.IsType<IndexerRequestResult.Deferred>(result).Deferral);
        Assert.Equal(cooldown, peeked);
        Assert.Equal(DateTimeKind.Utc, Assert.IsType<RequestDeferral>(peeked).UntilUtc.Kind);
        Assert.Equal(0, afterRestart.Handler.Hits);
    }

    [Fact]
    public async Task Pacing_AfterRestart_IsSeededFromPersistedLastDispatch()
    {
        // Given: a failure persists the dispatch start of the request that drew it.
        await using var database = await IndexerRequestStateTestDatabase.CreateAsync(indexerCount: 1);
        var indexerId = database.IndexerIds[0];
        using (var beforeRestart = new IndexerRequestGateHarness(
            new EfIndexerRequestStateStore(database.Factory),
            Sequence(() => Status(HttpStatusCode.InternalServerError))))
        {
            using var failed = Assert.IsType<IndexerRequestResult.Sent>(await beforeRestart.SendAsync(indexerId)).Response;
        }

        // When: the process restarts half a second later.
        var elapsed = TimeSpan.FromMilliseconds(500);
        using var afterRestart = new IndexerRequestGateHarness(
            new EfIndexerRequestStateStore(database.Factory),
            Sequence(() => Status(HttpStatusCode.OK)),
            clock: new ManualClock(Start + elapsed));
        using var sent = Assert.IsType<IndexerRequestResult.Sent>(await afterRestart.SendAsync(indexerId)).Response;

        // Then
        Assert.Equal([Interval - elapsed], afterRestart.Clock.Delays);
        Assert.Equal(1, afterRestart.Handler.Hits);
    }
}
