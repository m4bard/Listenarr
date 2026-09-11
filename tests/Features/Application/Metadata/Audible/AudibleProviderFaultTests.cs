/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using System.Net;
using System.Net.Http.Headers;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Metadata.Audible;

/// <summary>
/// What the Audible client does with an answer that is not an answer. These are the tests that
/// separate "the provider has never heard of this book" from "the provider did not answer", a
/// distinction the scheduled refresh turns into a 30-day stamp or a retry next cycle.
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
        // books the provider had never heard of, and every one of them was stamped as checked.
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
        // would have dropped the wait silently and gone straight back at the provider.
        Assert.NotNull(thrown.RetryAfter);
        Assert.InRange(thrown.RetryAfter!.Value, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(6));
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
        var throttled = Service(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)),
            out var throttledClient);
        using var _ = throttledClient;

        var pushback = await Assert.ThrowsAsync<MetadataProviderThrottledException>(
            () => throttled.GetBookMetadataAsync("B0THROTTLD", "us", useCache: false));

        var timedOut = Service(
            (_, _) => throw new TaskCanceledException("the request was canceled due to timeout"),
            out var timedOutClient);
        using var __ = timedOutClient;

        var transport = await Assert.ThrowsAsync<HttpRequestException>(
            () => timedOut.GetBookMetadataAsync("B0TIMEDOUT", "us", useCache: false));

        // Several API endpoints answer a failed lookup by echoing ex.Message. Once the client
        // began raising rather than returning null, the message it composed was the full
        // request URL, and the query string holds the ASIN or the search terms. The URL belongs
        // in the log, which is ours; the message can be read by anyone who can call the API.
        foreach (var message in new[] { pushback.Message, transport.Message })
        {
            Assert.DoesNotContain("http", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("audible.com", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("catalog/products", message, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("B0THROTTLD", pushback.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("B0TIMEDOUT", transport.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Scenario", "AGenuineMissIsStillNull")]
    public async Task GetBookMetadataAsync_ReturnsNull_WhenTheProviderAnswers404()
    {
        var service = Service(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)),
            out var client);
        using var _ = client;

        // The control for the three above. A provider that answers and has no such book must
        // still come back as null, or the refresh walk can never settle anything.
        Assert.Null(await service.GetBookMetadataAsync("B0MISSINGX", "us", useCache: false));
    }
}
