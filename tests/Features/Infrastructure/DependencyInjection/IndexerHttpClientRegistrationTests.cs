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
using Listenarr.Infrastructure.DependencyInjection.Platform;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Configuration;

namespace Listenarr.Tests.Features.Infrastructure.DependencyInjection;

[Trait("Name", "IndexerHttpClientRegistrationTests")]
[Trait("Category", "Infrastructure")]
public sealed class IndexerHttpClientRegistrationTests : BaseTests
{
    [Fact]
    public async Task IndexerClient_ReturnsTheRedirect_WhereDefaultClientFollowsIt()
    {
        // Given: a server whose first path answers 302 to a second path
        await using var server = new LoopbackHttpServer(context =>
        {
            if (context.Request.Url!.AbsolutePath == "/first")
            {
                context.Response.StatusCode = (int)HttpStatusCode.Redirect;
                context.Response.RedirectLocation = "/second";
            }
        });
        using var services = BuildServices(configure: null);
        var factory = services.GetRequiredService<IHttpClientFactory>();
        var target = new Uri(server.BaseUri, "/first");

        // When
        using var defaultResponse = await factory.CreateClient("default").GetAsync(target);
        using var indexerResponse = await factory.CreateClient(IndexerHttpDispatch.ClientName).GetAsync(target);

        // Then: the default client hides the hop inside one send; the indexer client returns it
        Assert.Equal(HttpStatusCode.OK, defaultResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, indexerResponse.StatusCode);
        Assert.Equal(new[] { "/first", "/second", "/first" }, server.RequestPaths);
    }

    [Fact]
    public void IndexerClient_TimeoutComesFromExternalRequestOptions()
    {
        using var services = BuildServices(options => options.TimeoutSeconds = 17);

        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(IndexerHttpDispatch.ClientName);

        Assert.Equal(TimeSpan.FromSeconds(17), client.Timeout);
    }

    private static ServiceProvider BuildServices(Action<ExternalRequestOptions>? configure)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        if (configure != null)
        {
            services.Configure(configure);
        }

        services.AddPlatformHttpClients(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }
}
