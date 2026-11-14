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
using System.Text;
using Listenarr.Infrastructure.DependencyInjection.Platform;
using Listenarr.Infrastructure.DependencyInjection.Search;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Configuration;

namespace Listenarr.Tests.Features.Infrastructure.Search.Providers.Torznab;

/// <summary>
/// Runs the Torznab provider as the application composes it, against a real loopback server, so
/// the real client handler decides whether a redirect is followed inside one send.
/// </summary>
[Trait("Name", "TorznabRedirectHopTests")]
[Trait("Category", "TorznabNewznabSearchProvider")]
public sealed class TorznabRedirectHopTests : BaseTests
{
    private const string EmptyFeed = "<rss><channel></channel></rss>";

    [Fact]
    public async Task RedirectHops_AreGateCalls()
    {
        // Given: an indexer whose API answers 302 to a second path, which answers 200
        await using var server = new LoopbackHttpServer(context =>
        {
            if (context.Request.Url!.AbsolutePath == "/api")
            {
                Redirect(context, "/moved");
                return;
            }

            WriteFeed(context);
        });
        var gate = new RecordingGate();

        // When
        var outcome = await SearchAsync(server, gate, new IndexerSearchContext(SearchOrigin.Interactive, CycleAllowance: null));

        // Then: each of the two wire requests is its own gate call, and the followed hop is a redirect
        Assert.Equal(new[] { "/api", "/moved" }, server.RequestPaths);
        Assert.Equal(new[] { IndexerRequestKind.Search, IndexerRequestKind.Redirect }, gate.Contexts.Select(c => c.Kind));
        Assert.Equal(IndexerSearchStatus.Completed, outcome.Status);
        Assert.Equal(2, outcome.RequestsSent);
    }

    [Fact]
    public async Task RedirectHop_FallbackQuery_KeepsQueryKindOnFirstSendOnly()
    {
        // Given: a fallback-query search whose API answers 302 once
        await using var server = new LoopbackHttpServer(context =>
        {
            if (context.Request.Url!.AbsolutePath == "/api")
            {
                Redirect(context, "/moved");
                return;
            }

            WriteFeed(context);
        });
        var gate = new RecordingGate();
        var allowance = new CycleRequestAllowance(5);

        // When
        await SearchAsync(server, gate, new IndexerSearchContext(SearchOrigin.Sweep, allowance, IndexerRequestKind.FallbackQuery));

        // Then: the query send carries the fallback kind; the hop is a redirect with the same origin and allowance
        Assert.Equal(
            new[]
            {
                new IndexerRequestContext(7, SearchOrigin.Sweep, IndexerRequestKind.FallbackQuery, allowance),
                new IndexerRequestContext(7, SearchOrigin.Sweep, IndexerRequestKind.Redirect, allowance)
            },
            gate.Contexts);
    }

    [Fact]
    public async Task RedirectHop_ToTargetWithEmbeddedCredentials_IsRefused()
    {
        // Given: an indexer whose API redirects to a target carrying credentials in the URL
        await using var server = new LoopbackHttpServer(context =>
        {
            if (context.Request.Url!.AbsolutePath == "/api")
            {
                var target = new UriBuilder(new Uri(context.Request.Url, "/moved")) { UserName = "user", Password = "secret" };
                Redirect(context, target.Uri.ToString());
                return;
            }

            WriteFeed(context);
        });
        var gate = new RecordingGate();

        // When
        var outcome = await SearchAsync(server, gate, new IndexerSearchContext(SearchOrigin.Interactive, CycleAllowance: null));

        // Then: the hop fails validation, so only the first request reached the wire
        Assert.Equal(new[] { "/api" }, server.RequestPaths);
        Assert.Single(gate.Contexts);
        Assert.Equal(IndexerSearchStatus.Failed, outcome.Status);
    }

    private static async Task<IndexerSearchOutcome> SearchAsync(
        LoopbackHttpServer server,
        IIndexerRequestGate gate,
        IndexerSearchContext context)
    {
        using var services = BuildServices(gate);
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider.GetServices<IIndexerSearchProvider>()
            .OfType<TorznabNewznabSearchProvider>()
            .Single();
        var indexer = new IndexerBuilder()
            .WithId(7)
            .WithName("Torznab")
            .WithImplementation("Torznab")
            .WithUrl(server.BaseUri.ToString())
            .Build();

        return await provider.SearchAsync(indexer, "query", null, null, context, CancellationToken.None);
    }

    // The production HTTP client and search registrations, with the gate replaced by a recorder.
    private static ServiceProvider BuildServices(IIndexerRequestGate gate)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton(Mock.Of<IIndexerRequestStateStore>());
        services.AddSingleton(Mock.Of<IConfigurationService>());
        services.AddSingleton(Mock.Of<IIndexerRepository>());
        services.AddPlatformHttpClients(new ConfigurationBuilder().Build());
        services.AddSearchServices();
        services.AddSingleton(gate);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static void Redirect(HttpListenerContext context, string location)
    {
        context.Response.StatusCode = (int)HttpStatusCode.Redirect;
        context.Response.RedirectLocation = location;
    }

    private static void WriteFeed(HttpListenerContext context)
    {
        var body = Encoding.UTF8.GetBytes(EmptyFeed);
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.ContentType = "application/rss+xml";
        context.Response.OutputStream.Write(body, 0, body.Length);
    }

    // Records every request context and then dispatches, as the pass-through gate does.
    private sealed class RecordingGate : IIndexerRequestGate
    {
        public List<IndexerRequestContext> Contexts { get; } = new();

        public async Task<IndexerRequestResult> SendAsync(
            IndexerRequestContext context,
            Func<CancellationToken, Task<HttpResponseMessage>> dispatch,
            CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            return new IndexerRequestResult.Sent(await dispatch(cancellationToken));
        }

        public Task<RequestDeferral?> GetDeferralAsync(int indexerId, SearchOrigin origin, CancellationToken cancellationToken)
        {
            return Task.FromResult<RequestDeferral?>(null);
        }
    }
}
