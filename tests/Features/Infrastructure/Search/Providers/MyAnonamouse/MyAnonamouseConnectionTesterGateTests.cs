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
using Microsoft.Extensions.Logging.Abstractions;
using static Listenarr.Tests.Features.Api.Features.Indexers.IndexerGateTestSupport;

namespace Listenarr.Tests.Features.Infrastructure.Search.Providers.MyAnonamouse;

[Trait("Name", "MyAnonamouseConnectionTesterGateTests")]
[Trait("Category", "MyAnonamouseConnectionTester")]
public sealed class MyAnonamouseConnectionTesterGateTests : BaseTests
{
    private const string SearchPath = "/tor/js/loadSearchJSONbasic.php";

    [Fact]
    public async Task ConnectionTest_InCooldown_SendsNothingAndReportsTheDeferral()
    {
        // Given: an indexer whose gate defers every request
        await using var server = RedirectingServer("/none", "{\"data\":[]}");
        var tester = CreateTester(new DeferringGate());

        // When
        var result = await tester.TestAsync(CreateIndexer(server), "secret");

        // Then
        Assert.False(result.Succeeded);
        Assert.Equal(Cooldown, result.Deferral);
        Assert.Equal(Cooldown.Describe(), result.Message);
        Assert.Empty(server.RequestPaths);
    }

    [Fact]
    public async Task ConnectionTestRedirectHopsAreGateCalls()
    {
        // Given: a search endpoint that answers 302 to a second path, which answers 200
        await using var server = RedirectingServer(SearchPath, "{\"data\":[]}");
        var gate = new RecordingGate();

        // When
        var result = await CreateTester(gate).TestAsync(CreateIndexer(server), "secret");

        // Then: both wire requests are gate calls: an interactive connection test and its redirect hop
        Assert.True(result.Succeeded);
        Assert.Equal(new[] { SearchPath, "/moved" }, server.RequestPaths);
        Assert.Equal(
            new[]
            {
                new IndexerRequestContext(7, SearchOrigin.Interactive, IndexerRequestKind.ConnectionTest),
                new IndexerRequestContext(7, SearchOrigin.Interactive, IndexerRequestKind.Redirect)
            },
            gate.Contexts);
    }

    private static MyAnonamouseConnectionTester CreateTester(IIndexerRequestGate gate)
        => new(new StubHttpClientFactory(new HttpClientHandler { AllowAutoRedirect = false }), gate, NullLogger<MyAnonamouseConnectionTester>.Instance);

    private static Indexer CreateIndexer(LoopbackHttpServer server)
        => new() { Id = 7, Name = "MAM", Url = server.BaseUri.ToString(), Implementation = "MyAnonamouse", Type = "Torrent" };
}
