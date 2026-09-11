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

namespace Listenarr.Tests.Features.Application.Metadata.Audible;

/// <summary>
/// What the Audible client does with an answer that is not an answer. These are the tests that
/// separate "the provider has never heard of this book" from "the provider did not answer".
/// Only the first of those is a fact about the book, and only the first may become null.
/// </summary>
[Trait("Area", "Metadata")]
[Trait("Name", "AudibleProviderFaultTests")]
[Trait("Category", "Application")]
public class AudibleProviderFaultTests : BaseTests
{
    private static AudibleService Service(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        out HttpClient client)
    {
        client = new HttpClient(new DelegatingHandlerMock(respond)
        {
            InnerHandler = new HttpClientHandler()
        });
        return new AudibleService(client, Mock.Of<ILogger<AudibleService>>());
    }

    private static AudibleService Answering(HttpStatusCode status, out HttpClient client) =>
        Service((_, _) => Task.FromResult(new HttpResponseMessage(status)), out client);

    [Fact]
    [Trait("Scenario", "PushbackIsRaisedWithTheRetryAfter")]
    public async Task GetBookMetadataAsync_RaisesThrottled_WhenTheProviderAnswers429()
    {
        var service = Service(
            (_, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
                return Task.FromResult(response);
            },
            out var client);
        using var _ = client;

        var thrown = await Assert.ThrowsAsync<MetadataProviderThrottledException>(
            () => service.GetBookMetadataAsync("B0THROTTLD", "us", useCache: false));

        // Logging the status and returning null made a throttled sweep look like a library of
        // books the provider had never heard of.
        Assert.Equal(TimeSpan.FromSeconds(90), thrown.RetryAfter);
    }

    [Fact]
    [Trait("Scenario", "AnHttpDateRetryAfterIsUnderstood")]
    public async Task GetBookMetadataAsync_ReadsTheRetryAfter_WhenItIsAnHttpDate()
    {
        var service = Service(
            (_, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter =
                    new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(5));
                return Task.FromResult(response);
            },
            out var client);
        using var _ = client;

        var thrown = await Assert.ThrowsAsync<MetadataProviderThrottledException>(
            () => service.GetBookMetadataAsync("B0RETRYDAT", "us", useCache: false));

        // RFC 9110 allows both shapes and Audible has used both. Reading only the delta form
        // would drop the wait silently and go straight back at the provider.
        Assert.NotNull(thrown.RetryAfter);
        Assert.InRange(thrown.RetryAfter!.Value, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(6));
    }

    [Fact]
    [Trait("Scenario", "ARetryAfterAlreadyInThePastIsNotAWait")]
    public async Task GetBookMetadataAsync_ReportsNoRetryAfter_WhenTheHttpDateHasAlreadyPassed()
    {
        var service = Service(
            (_, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter =
                    new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(-5));
                return Task.FromResult(response);
            },
            out var client);
        using var _ = client;

        var thrown = await Assert.ThrowsAsync<MetadataProviderThrottledException>(
            () => service.GetBookMetadataAsync("B0PASTDATE", "us", useCache: false));

        // A date in the past arrives from a provider whose clock disagrees with ours, and it is
        // the shape a sloppy proxy emits too. Subtracting it gives a negative wait, and a
        // negative wait handed to the caller reads as no pushback at all, or worse arms a timer
        // in the past. Null says the provider named no usable wait, which is what it did.
        Assert.Null(thrown.RetryAfter);
    }

    [Fact]
    [Trait("Scenario", "ForbiddenIsPushbackToo")]
    public async Task GetBookMetadataAsync_RaisesThrottled_WhenTheProviderAnswers403()
    {
        var service = Answering(HttpStatusCode.Forbidden, out var client);
        using var _ = client;

        // The catalog endpoints take no credentials, so there is no authorization here to fail.
        // A forbidden catalog read is how Audible shuts out a caller that asked too much, and
        // it is the shape seen far more often than a 429.
        var thrown = await Assert.ThrowsAsync<MetadataProviderThrottledException>(
            () => service.GetBookMetadataAsync("B0FORBIDDN", "us", useCache: false));
        Assert.Null(thrown.RetryAfter);
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

        // The gap the throttle work left open. Pushback was raised and everything else was
        // still logged and flattened to null, so a provider having a bad hour read as a
        // provider that had never heard of any of the books asked about.
        var thrown = await Assert.ThrowsAsync<HttpRequestException>(
            () => service.GetBookMetadataAsync("B0SERVERER", "us", useCache: false));
        Assert.Equal(status, thrown.StatusCode);
    }

    [Fact]
    [Trait("Scenario", "ATransportFaultPropagates")]
    public async Task GetBookMetadataAsync_RaisesTheTransportFault_RatherThanReturningNull()
    {
        var service = Service(
            (_, _) => throw new HttpRequestException("connection refused"),
            out var client);
        using var _ = client;

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.GetBookMetadataAsync("B0REFUSEDX", "us", useCache: false));
    }

    [Fact]
    [Trait("Scenario", "TheRequestUrlStaysOutOfTheMessage")]
    public async Task GetBookMetadataAsync_KeepsTheRequestUrl_OutOfEveryRaisedMessage()
    {
        var throttled = Answering(HttpStatusCode.TooManyRequests, out var throttledClient);
        using var _ = throttledClient;

        var pushback = await Assert.ThrowsAsync<MetadataProviderThrottledException>(
            () => throttled.GetBookMetadataAsync("B0THROTTLD", "us", useCache: false));

        var timedOut = Service(
            (_, _) => throw new TaskCanceledException("the request was canceled due to timeout"),
            out var timedOutClient);
        using var __ = timedOutClient;

        var transport = await Assert.ThrowsAsync<HttpRequestException>(
            () => timedOut.GetBookMetadataAsync("B0TIMEDOUT", "us", useCache: false));

        var serverFault = Answering(HttpStatusCode.BadGateway, out var serverFaultClient);
        using var ___ = serverFaultClient;

        var faulted = await Assert.ThrowsAsync<HttpRequestException>(
            () => serverFault.GetBookMetadataAsync("B0BADGATEW", "us", useCache: false));

        // Several API endpoints answer a failed lookup by reporting what the exception said.
        // A client that raises composes that message from the request it made, and the query
        // string holds the ASIN or the search terms. The URL belongs in the log, which is
        // ours; the message can be read by anyone who can call the API.
        foreach (var message in new[] { pushback.Message, transport.Message, faulted.Message })
        {
            Assert.DoesNotContain("http", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("audible.com", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("catalog/products", message, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("B0THROTTLD", pushback.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("B0TIMEDOUT", transport.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("B0BADGATEW", faulted.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [Trait("Scenario", "AGenuineMissIsStillNull")]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task GetBookMetadataAsync_ReturnsNull_WhenTheProviderSaysThereIsNoSuchRecord(HttpStatusCode status)
    {
        var service = Answering(status, out var client);
        using var _ = client;

        // The control for everything above. A provider that answers and has no such book must
        // still come back as null, or no caller can ever settle anything.
        Assert.Null(await service.GetBookMetadataAsync("B0MISSINGX", "us", useCache: false));
    }
}
