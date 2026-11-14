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

using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.Indexers.Common;

[Trait("Name", "PassThroughIndexerRequestGateTests")]
[Trait("Category", "Application")]
public sealed class PassThroughIndexerRequestGateTests : BaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SendAsync_SpentAllowance_DefersWithoutDispatching()
    {
        // Given
        var gate = CreateGate();
        var dispatch = new CountingDispatch();
        var context = CreateContext(new CycleRequestAllowance(0));

        // When
        var result = await gate.SendAsync(context, dispatch.SendAsync, CancellationToken.None);

        // Then
        var deferred = Assert.IsType<IndexerRequestResult.Deferred>(result);
        Assert.Equal(new RequestDeferral(Now.UtcDateTime, RequestDeferralReason.CycleRequestBound), deferred.Deferral);
        Assert.Equal(DateTimeKind.Utc, deferred.Deferral.UntilUtc.Kind);
        Assert.Equal(0, dispatch.Calls);
    }

    [Fact]
    public async Task SendAsync_AllowanceWithOneUnit_SendsOnceThenDefers()
    {
        // Given
        var gate = CreateGate();
        var dispatch = new CountingDispatch();
        var allowance = new CycleRequestAllowance(1);
        var context = CreateContext(allowance);

        // When
        var first = await gate.SendAsync(context, dispatch.SendAsync, CancellationToken.None);
        var second = await gate.SendAsync(context, dispatch.SendAsync, CancellationToken.None);

        // Then
        Assert.IsType<IndexerRequestResult.Sent>(first);
        Assert.IsType<IndexerRequestResult.Deferred>(second);
        Assert.Equal(1, dispatch.Calls);
        Assert.Equal(1, allowance.Taken);
    }

    [Fact]
    public async Task SendAsync_NoAllowance_DispatchesOnceAndReturnsTheResponse()
    {
        // Given
        var gate = CreateGate();
        var dispatch = new CountingDispatch();
        using var cancellation = new CancellationTokenSource();

        // When
        var result = await gate.SendAsync(CreateContext(cycleAllowance: null), dispatch.SendAsync, cancellation.Token);

        // Then
        var sent = Assert.IsType<IndexerRequestResult.Sent>(result);
        Assert.Same(dispatch.LastResponse, sent.Response);
        Assert.Equal(1, dispatch.Calls);
        Assert.Equal(cancellation.Token, dispatch.LastToken);
    }

    [Fact]
    public async Task SendAsync_DispatchCancelled_Propagates()
    {
        // Given
        var gate = CreateGate();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // When
        var send = gate.SendAsync(
            CreateContext(cycleAllowance: null),
            token => Task.FromException<HttpResponseMessage>(new OperationCanceledException(token)),
            cancellation.Token);

        // Then
        await Assert.ThrowsAsync<OperationCanceledException>(() => send);
    }

    [Fact]
    public async Task GetDeferralAsync_ReturnsNull()
    {
        // Given
        var gate = CreateGate();

        // When
        var deferral = await gate.GetDeferralAsync(7, SearchOrigin.Sweep, CancellationToken.None);

        // Then
        Assert.Null(deferral);
    }

    private static PassThroughIndexerRequestGate CreateGate() => new(new FixedTimeProvider(Now));

    private static IndexerRequestContext CreateContext(CycleRequestAllowance? cycleAllowance)
        => new(7, SearchOrigin.Sweep, IndexerRequestKind.Search, cycleAllowance);

    private sealed class CountingDispatch
    {
        public int Calls { get; private set; }

        public HttpResponseMessage? LastResponse { get; private set; }

        public CancellationToken LastToken { get; private set; }

        public Task<HttpResponseMessage> SendAsync(CancellationToken cancellationToken)
        {
            Calls++;
            LastToken = cancellationToken;
            LastResponse = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            return Task.FromResult(LastResponse);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
