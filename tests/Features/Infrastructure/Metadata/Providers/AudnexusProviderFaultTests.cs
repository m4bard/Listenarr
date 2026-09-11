/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using System.Net;
using System.Net.Http.Headers;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Metadata.Providers;

/// <summary>
/// The second source the multi-source walk asks, held to the same division as the first. A walk
/// that gets null from both sources reports the book as absent, so a null either client hands
/// back has to mean the provider answered and had nothing.
/// </summary>
[Trait("Area", "Metadata")]
[Trait("Name", "AudnexusProviderFaultTests")]
[Trait("Category", "Infrastructure")]
public sealed class AudnexusProviderFaultTests : BaseTests
{
    private static AudnexusService Service(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        out HttpClient client)
    {
        client = new HttpClient(new DelegatingHandlerMock(respond)
        {
            InnerHandler = new HttpClientHandler()
        });
        return new AudnexusService(client, Mock.Of<ILogger<AudnexusService>>());
    }

    private static AudnexusService Answering(HttpStatusCode status, out HttpClient client) =>
        Service((_, _) => Task.FromResult(new HttpResponseMessage(status)), out client);

    [Fact]
    [Trait("Scenario", "PushbackIsRaisedWithTheRetryAfter")]
    public async Task GetBookMetadataAsync_RaisesThrottled_WhenTheProviderAnswers429()
    {
        var service = Service(
            (_, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(45));
                return Task.FromResult(response);
            },
            out var client);
        using var _ = client;

        var thrown = await Assert.ThrowsAsync<MetadataProviderThrottledException>(
            () => service.GetBookMetadataAsync("B0THROTTLD"));
        Assert.Equal(TimeSpan.FromSeconds(45), thrown.RetryAfter);
    }

    [Fact]
    [Trait("Scenario", "ForbiddenIsPushbackToo")]
    public async Task GetBookMetadataAsync_RaisesThrottled_WhenTheProviderAnswers403()
    {
        var service = Answering(HttpStatusCode.Forbidden, out var client);
        using var _ = client;

        await Assert.ThrowsAsync<MetadataProviderThrottledException>(
            () => service.GetBookMetadataAsync("B0FORBIDDN"));
    }

    [Theory]
    [Trait("Scenario", "AServerFaultIsNotAMissingBook")]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task GetBookMetadataAsync_RaisesTransport_WhenTheProviderAnswers5xx(HttpStatusCode status)
    {
        var service = Answering(status, out var client);
        using var _ = client;

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(
            () => service.GetBookMetadataAsync("B0SERVERER"));
        Assert.Equal(status, thrown.StatusCode);
    }

    [Fact]
    [Trait("Scenario", "ATransportFaultPropagates")]
    public async Task GetBookMetadataAsync_RaisesTheTransportFault_RatherThanReturningNull()
    {
        var service = Service((_, _) => throw new HttpRequestException("connection refused"), out var client);
        using var _ = client;

        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetBookMetadataAsync("B0REFUSEDX"));
    }

    [Fact]
    [Trait("Scenario", "ATimeoutIsNotACancellation")]
    public async Task GetBookMetadataAsync_RewrapsATimeout_SoItIsNotReadAsAStopRequest()
    {
        var service = Service(
            (_, _) => throw new TaskCanceledException("the request was canceled due to timeout"),
            out var client);
        using var _ = client;

        // A TaskCanceledException is an OperationCanceledException, which callers above read as
        // "you were asked to stop". A request that never arrived is a different event.
        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetBookMetadataAsync("B0TIMEDOUT"));
    }

    [Fact]
    [Trait("Scenario", "AnUnusablePayloadIsStillNull")]
    public async Task GetBookMetadataAsync_ReturnsNull_WhenTheProviderAnsweredWithSomethingUnreadable()
    {
        var service = Service(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ this is not json")
            }),
            out var client);
        using var _ = client;

        // The provider answered. What it said cannot be parsed, which is a property of the
        // record and will be the same on the next attempt, so it must not be reported as an
        // outage that never clears.
        Assert.Null(await service.GetBookMetadataAsync("B0BADJSONX"));
    }

    [Theory]
    [Trait("Scenario", "AGenuineMissIsStillNull")]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task GetBookMetadataAsync_ReturnsNull_WhenTheProviderSaysThereIsNoSuchRecord(HttpStatusCode status)
    {
        var service = Answering(status, out var client);
        using var _ = client;

        // The control for everything above.
        Assert.Null(await service.GetBookMetadataAsync("B0MISSINGX"));
    }
}
