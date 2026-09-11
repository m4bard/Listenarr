using System.Text.Json;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Metadata.Audible;

/// <summary>
/// A timeout and a genuine zero-match both used to produce an empty result set. These assert
/// the two can still be told apart, which is the whole point: a caller that reads an empty
/// list as "this book is not in the catalogue" is wrong half the time otherwise.
/// </summary>
/// <remarks>
/// The fault cases raise, and are asserted as raising. They were written against #910's
/// ProviderUnavailable flag, which the client's raise supersedes for every one of them: a
/// caller of the book, author or chapter lookup gets the same distinction now, where the flag
/// reached only this one workflow. The flag is still asserted where it still decides something,
/// which is a 200 carrying a body this client could not read; that is a property of the record
/// rather than of the provider, and the client keeps answering it with null.
/// </remarks>
[Trait("Name", "AudibleProviderUnavailableTests")]
[Trait("Category", "Application")]
public sealed class AudibleProviderUnavailableTests : BaseTests
{
    [Fact]
    public async Task SearchProductsDirectAsync_WhenAudibleDoesNotAnswer_RaisesRatherThanAnswerForIt()
    {
        var workflow = BuildWorkflow(new StallingHandler());

        var fault = await Record.ExceptionAsync(() => workflow.SearchProductsDirectAsync(
            query: "any", title: null, author: null, narrator: null, publisher: null,
            page: 1, limit: 10, region: "us", language: null, sortBy: "Relevance"));

        Assert.NotNull(fault);
        Assert.True(MetadataProviderFaults.IsProviderUnavailable(fault));
    }

    [Fact]
    public async Task SearchProductsDirectAsync_WhenAudibleAnswersWithNothing_IsAConfirmedZeroMatch()
    {
        // The control for the test above. If ProviderUnavailable were set unconditionally
        // on any empty result, this would fail, and the flag would mean nothing.
        var workflow = BuildWorkflow(new EmptyCatalogHandler());

        var result = await workflow.SearchProductsDirectAsync(
            query: "any", title: null, author: null, narrator: null, publisher: null,
            page: 1, limit: 10, region: "us", language: null, sortBy: "Relevance");

        Assert.False(result.ProviderUnavailable);
        Assert.Empty(result.Results);
    }

    [Fact]
    public async Task SearchProductsDirectAsync_WhenAudibleDoesNotAnswer_DoesNotSpendTheBudgetOnADiacriticsRetry()
    {
        // A failed call returns zero results, which used to look exactly like a miss worth
        // retrying without diacritics. That second request fails the same way and costs the
        // caller another full timeout.
        var handler = new StallingHandler();
        var workflow = BuildWorkflow(handler);

        var fault = await Record.ExceptionAsync(() => workflow.SearchProductsDirectAsync(
            query: null, title: "Les Mis\u00e9rables", author: null,
            narrator: null, publisher: null,
            page: 1, limit: 10, region: "us", language: null, sortBy: "Relevance"));

        Assert.NotNull(fault);
        Assert.Equal(1, handler.Requests);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(429)]
    public async Task SearchProductsDirectAsync_WhenAudibleRejectsTheCall_Raises(int statusCode)
    {
        // A timeout is only one of the ways the call fails. A 5xx and a rate-limit answer are
        // just as much "not known" as "not in the catalogue", so none of them may come back as
        // an answer about the catalogue. The predicate is asserted rather than a concrete type
        // because 429 raises pushback and the 5xx raise a request fault; what a caller has to
        // be able to tell is that neither is a verdict on the book.
        var workflow = BuildWorkflow(new StatusCodeHandler((System.Net.HttpStatusCode)statusCode));

        var fault = await Record.ExceptionAsync(() => workflow.SearchProductsDirectAsync(
            query: "any", title: null, author: null, narrator: null, publisher: null,
            page: 1, limit: 10, region: "us", language: null, sortBy: "Relevance"));

        Assert.NotNull(fault);
        Assert.True(MetadataProviderFaults.IsProviderUnavailable(fault));
    }

    [Fact]
    public async Task SearchProductsDirectAsync_WhenTheBodyWillNotParse_MarksTheResultUnavailable()
    {
        var workflow = BuildWorkflow(new MalformedBodyHandler());

        var result = await workflow.SearchProductsDirectAsync(
            query: "any", title: null, author: null, narrator: null, publisher: null,
            page: 1, limit: 10, region: "us", language: null, sortBy: "Relevance");

        Assert.True(result.ProviderUnavailable);
        Assert.Empty(result.Results);
    }

    [Fact]
    public async Task SearchProductsDirectAsync_WhenTheRequestNeverLeaves_Raises()
    {
        // Name resolution and connection refusal both surface as HttpRequestException.
        var workflow = BuildWorkflow(new TransportFailureHandler());

        var fault = await Record.ExceptionAsync(() => workflow.SearchProductsDirectAsync(
            query: "any", title: null, author: null, narrator: null, publisher: null,
            page: 1, limit: 10, region: "us", language: null, sortBy: "Relevance"));

        Assert.NotNull(fault);
        Assert.True(MetadataProviderFaults.IsProviderUnavailable(fault));
    }

    private static AudibleProductSearchWorkflow BuildWorkflow(HttpMessageHandler handler)
    {
        var client = new AudibleApiClient(new HttpClient(handler), NullLogger.Instance);
        return new AudibleProductSearchWorkflow(
            client,
            (_, _, _, _) => Task.FromResult<AudibleBookResponse?>(null),
            NullLogger.Instance);
    }

    /// <summary>Never answers inside the call's own timeout, which is what a real timeout looks like.</summary>
    private sealed class StallingHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
    }

    /// <summary>Answers promptly, and refuses.</summary>
    private sealed class StatusCodeHandler(System.Net.HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode));
    }

    /// <summary>Answers 200 with something that is not JSON.</summary>
    private sealed class MalformedBodyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"products\": [ truncated")
            });
        }
    }

    /// <summary>Never reaches Audible at all.</summary>
    private sealed class TransportFailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Name or service not known");
    }

    /// <summary>Answers promptly, with a catalogue that genuinely holds nothing.</summary>
    private sealed class EmptyCatalogHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { products = Array.Empty<object>() }))
            });
        }
    }
}
