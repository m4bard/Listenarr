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
using Listenarr.Application.Security.Outbound;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Security.Outbound;

[Trait("Name", "OutboundRequestSecurityGatedRedirectTests")]
[Trait("Category", "OutboundRequestSecurity")]
public sealed class OutboundRequestSecurityGatedRedirectTests : BaseTests
{
    // Public IP literals, so the address checks run without a DNS lookup.
    private static readonly Uri StartUri = new("https://203.0.113.10/start");

    [Fact]
    public async Task GatedOverload_RedirectToLoopbackAddress_IsBlockedBeforeTheHopIsSent()
    {
        // Given: the first hop redirects to a loopback address
        var sender = new RecordingSender(hop => hop == 0 ? Redirect("https://127.0.0.1/next") : Ok());

        // When
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(sender));

        // Then: the redirect target is rejected and never sent
        Assert.StartsWith("Blocked redirect target", exception.Message);
        Assert.Equal([StartUri], sender.SentUris);
    }

    [Fact]
    public async Task GatedOverload_LocalInitialHost_IsBlockedBeforeAnySend()
    {
        var sender = new RecordingSender(_ => Ok());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => OutboundRequestSecurity.SendWithValidatedRedirectsAsync(
                uri => new HttpRequestMessage(HttpMethod.Get, uri),
                new Uri("https://localhost/start"),
                sender.SendAsync,
                NullLogger.Instance,
                allowPrivateTargets: false));

        Assert.Empty(sender.SentUris);
    }

    [Fact]
    public async Task GatedOverload_CallsTheSenderOncePerHop_WithTheHopIndex()
    {
        // Given: two redirects, one absolute and one relative, then a final answer
        var sender = new RecordingSender(hop => hop switch
        {
            0 => Redirect("https://203.0.113.11/one"),
            1 => Redirect("/two"),
            _ => Ok()
        });

        // When
        var (response, finalUri) = await SendAsync(sender);

        // Then: one sender call per hop, in order, each with its own hop index
        using (response)
        {
            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal([0, 1, 2], sender.HopIndexes);
        Assert.Equal(
            [StartUri, new Uri("https://203.0.113.11/one"), new Uri("https://203.0.113.11/two")],
            sender.SentUris);
        Assert.Equal(new Uri("https://203.0.113.11/two"), finalUri);
    }

    [Fact]
    public async Task GatedOverload_DeclinedFirstHop_ReturnsNoResponseAndTheInitialUri()
    {
        var sender = new RecordingSender(_ => null);

        var (response, finalUri) = await SendAsync(sender);

        Assert.Null(response);
        Assert.Equal(StartUri, finalUri);
        Assert.Equal([0], sender.HopIndexes);
    }

    [Fact]
    public async Task GatedOverload_DeclinedLaterHop_EndsTheLoopWithNoResponse()
    {
        var sender = new RecordingSender(hop => hop == 0 ? Redirect("https://203.0.113.11/one") : null);

        var (response, finalUri) = await SendAsync(sender);

        Assert.Null(response);
        Assert.Equal(new Uri("https://203.0.113.11/one"), finalUri);
        Assert.Equal([0, 1], sender.HopIndexes);
    }

    [Fact]
    public async Task GatedOverload_TooManyRedirects_ThrowsAfterTheLimit()
    {
        var sender = new RecordingSender(hop => Redirect($"/hop/{hop + 1}"));

        await Assert.ThrowsAsync<HttpRequestException>(() => SendAsync(sender, maxRedirects: 2));

        Assert.Equal([0, 1, 2], sender.HopIndexes);
    }

    private static Task<(HttpResponseMessage? Response, Uri FinalUri)> SendAsync(RecordingSender sender, int maxRedirects = 5)
    {
        return OutboundRequestSecurity.SendWithValidatedRedirectsAsync(
            uri => new HttpRequestMessage(HttpMethod.Get, uri),
            StartUri,
            sender.SendAsync,
            NullLogger.Instance,
            allowPrivateTargets: false,
            maxRedirects: maxRedirects);
    }

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private static HttpResponseMessage Ok()
    {
        return new HttpResponseMessage(HttpStatusCode.OK);
    }

    // Answers each hop from a script and records what it was asked to send. A null answer declines the hop.
    private sealed class RecordingSender
    {
        private readonly Func<int, HttpResponseMessage?> _answer;

        public RecordingSender(Func<int, HttpResponseMessage?> answer)
        {
            _answer = answer;
        }

        public List<int> HopIndexes { get; } = new();

        public List<Uri> SentUris { get; } = new();

        public Task<HttpResponseMessage?> SendAsync(HttpRequestMessage request, int hopIndex, CancellationToken cancellationToken)
        {
            HopIndexes.Add(hopIndex);
            SentUris.Add(request.RequestUri!);
            return Task.FromResult(_answer(hopIndex));
        }
    }
}
