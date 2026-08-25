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
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Listenarr.Tests.Features.Api.Startup;

/// <summary>
/// Drives the real request pipeline against a web root holding the document the frontend build
/// emits, so the asset prefix is checked end to end rather than only through the transform.
/// </summary>
[Trait("Name", "ListenarrIndexHtmlPipelineTests")]
[Trait("Category", "Api")]
public sealed class ListenarrIndexHtmlPipelineTests : BaseTests
{
    private const string EmittedIndexHtml =
        """
        <!DOCTYPE html>
        <html lang="en">
          <head>
            <link rel="icon" type="image/x-icon" href="./favicon.ico">
            <link rel="manifest" href="./site.webmanifest">
            <script type="module" crossorigin src="./assets/index-D69pwXdM.js"></script>
            <link rel="stylesheet" crossorigin href="./assets/index-D8-1G8bZ.css">
          </head>
          <body><div id="app"></div></body>
        </html>
        """;

    [Theory]
    [InlineData("/example/")]
    [InlineData("/example/index.html")]
    [InlineData("/example/audiobooks/12")]
    public async Task UnderAUrlBase_TheDocumentCarriesThePrefixAndTheRuntimeGlobal(string requestPath)
    {
        using var host = CreateHost("/example");
        using var client = CreateClient(host);

        var response = await client.GetAsync(requestPath);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("src=\"/example/assets/index-D69pwXdM.js\"", body, StringComparison.Ordinal);
        Assert.Contains("href=\"/example/site.webmanifest\"", body, StringComparison.Ordinal);
        Assert.Contains("window.__listenarrUrlBase = \"/example\";", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"./", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/audiobooks/12")]
    public async Task WithoutAUrlBase_TheDocumentIsTheAbsoluteOneItAlwaysWas(string requestPath)
    {
        using var host = CreateHost("/");
        using var client = CreateClient(host);

        var response = await client.GetAsync(requestPath);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("src=\"/assets/index-D69pwXdM.js\"", body, StringComparison.Ordinal);
        Assert.Contains("href=\"/favicon.ico\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("__listenarrUrlBase", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"./", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDocumentRevalidatesWithAnETagInsteadOfBeingRefetched()
    {
        using var host = CreateHost("/example");
        using var client = CreateClient(host);

        var first = await client.GetAsync("/example/audiobooks/12");
        var etag = first.Headers.ETag;
        Assert.NotNull(etag);

        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/example/audiobooks/12");
        conditional.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag!.Tag));
        var second = await client.SendAsync(conditional);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task StaticAssetsUnderTheUrlBaseAreStillServedByTheStaticFileMiddleware()
    {
        using var host = CreateHost("/example");
        using var client = CreateClient(host);

        var response = await client.GetAsync("/example/assets/index-D8-1G8bZ.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(".listenarr{color:#000}", await response.Content.ReadAsStringAsync());
    }

    private readonly ListenarrWebApplicationFactory _factory = new();

    public override async Task DisposeAsync()
    {
        _factory.Dispose();
        await base.DisposeAsync();
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    private WebApplicationFactory<Program> CreateHost(string urlBase)
    {
        var webRoot = Path.Join(FileService.GetTempPath(), $"webroot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Join(webRoot, "assets"));
        File.WriteAllText(Path.Join(webRoot, "index.html"), EmittedIndexHtml);
        File.WriteAllText(Path.Join(webRoot, "assets", "index-D8-1G8bZ.css"), ".listenarr{color:#000}");

        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseWebRoot(webRoot);
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IStartupConfigService>(_ =>
                    new StartupConfigServiceMock(new StartupConfig
                    {
                        AuthenticationRequired = "false",
                        UrlBase = urlBase,
                    }));
            });
        });
    }
}
