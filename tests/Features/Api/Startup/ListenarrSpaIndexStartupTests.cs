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
using System.Text;
using Listenarr.Api.Startup;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.FileProviders;

namespace Listenarr.Tests.Features.Api.Startup;

[Trait("Name", "ListenarrSpaIndexStartupTests")]
[Trait("Category", "Api")]
public sealed class ListenarrSpaIndexStartupTests : BaseTests
{
    private const string ShellHtml =
        "<!DOCTYPE html>\n<html lang=\"en\">\n  <head>\n    <title>Listenarr</title>\n" +
        "    <script type=\"module\" src=\"./assets/index.js\"></script>\n  </head>\n" +
        "  <body><div id=\"app\"></div></body>\n</html>\n";

    [Fact]
    public void InjectBaseHref_PutsTheTagImmediatelyAfterTheOpeningHeadTag()
    {
        var injected = SpaIndexHtml.InjectBaseHref(ShellHtml, "/example");

        Assert.Contains("<head><base href=\"/example/\">", injected, StringComparison.Ordinal);
        Assert.Single(SplitOn(injected, "<base "));

        // Everything the build emitted has to survive untouched.
        Assert.Contains("<script type=\"module\" src=\"./assets/index.js\">", injected, StringComparison.Ordinal);
        Assert.Equal(ShellHtml.Length + "<base href=\"/example/\">".Length, injected.Length);
    }

    [Fact]
    public void InjectBaseHref_WritesASiteRootBase_ForAnEmptyBasePath()
    {
        Assert.Contains("<base href=\"/\">", SpaIndexHtml.InjectBaseHref(ShellHtml, string.Empty), StringComparison.Ordinal);
    }

    [Fact]
    public void InjectBaseHref_KeepsTheTagInTheDocument_WhenThereIsNoHeadElement()
    {
        var injected = SpaIndexHtml.InjectBaseHref("<div id=\"app\"></div>", "/example");

        Assert.StartsWith("<base href=\"/example/\">", injected, StringComparison.Ordinal);
    }

    [Fact]
    public void InjectBaseHref_EncodesTheBase_SoAConfiguredValueCannotBreakOutOfTheAttribute()
    {
        var injected = SpaIndexHtml.InjectBaseHref(ShellHtml, "/a\"><script>alert(1)</script>");

        Assert.DoesNotContain("<script>alert(1)</script>", injected, StringComparison.Ordinal);
        Assert.Contains("&quot;&gt;&lt;script&gt;", injected, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("/", "")]
    [InlineData("/example", "/example")]
    [InlineData("/example/", "/example")]
    [InlineData("/example/audiobooks", "/example/audiobooks")]
    public void NormalizeBasePath_DropsTheTrailingSlash(string pathBase, string expected)
    {
        Assert.Equal(expected, SpaIndexHtml.NormalizeBasePath(new PathString(pathBase)));
    }

    [Fact]
    public void NormalizeBasePath_EscapesABaseThatIsNotUrlSafe()
    {
        Assert.Equal("/my%20books", SpaIndexHtml.NormalizeBasePath(new PathString("/my books")));
    }

    [Fact]
    public async Task WriteAsync_SendsTheShellWithTheBaseOfTheRequestPathBase()
    {
        var webRoot = CreateWebRootWithShell();
        var spaIndex = new SpaIndexHtml(new PhysicalFileProvider(webRoot));

        var atSubPath = await WriteShellAsync(spaIndex, "/example");
        var atSiteRoot = await WriteShellAsync(spaIndex, string.Empty);

        Assert.Contains("<base href=\"/example/\">", atSubPath.Body, StringComparison.Ordinal);
        Assert.Contains("<base href=\"/\">", atSiteRoot.Body, StringComparison.Ordinal);
        Assert.Equal(StatusCodes.Status200OK, atSubPath.StatusCode);
        Assert.Equal("text/html; charset=utf-8", atSubPath.ContentType);
        Assert.Equal("no-cache", atSubPath.CacheControl);
    }

    [Fact]
    public async Task WriteAsync_ReadsTheShellOnce()
    {
        var webRoot = CreateWebRootWithShell();
        var spaIndex = new SpaIndexHtml(new PhysicalFileProvider(webRoot));

        var first = await WriteShellAsync(spaIndex, "/example");
        await File.WriteAllTextAsync(Path.Join(webRoot, "index.html"), "<html><head></head><body>replaced</body></html>");
        var second = await WriteShellAsync(spaIndex, "/example");

        Assert.Equal(first.Body, second.Body);
        Assert.DoesNotContain("replaced", second.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_Returns404_WhenTheFrontendHasNotBeenBuiltIntoTheWebRoot()
    {
        var spaIndex = new SpaIndexHtml(new PhysicalFileProvider(FileService.GetTempDirectory("empty-web-root")));

        var response = await WriteShellAsync(spaIndex, "/example");

        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        Assert.Equal(string.Empty, response.Body);
    }

    [Fact]
    public async Task ConfiguredUrlBase_ServesTheShellWithABaseHref_AtTheRootAndAtADeepLink()
    {
        var webRoot = CreateWebRootWithShell();
        using var factory = new ListenarrWebApplicationFactory();
        using var withUrlBase = CreateFactory(factory, "/example", webRoot);
        using var client = CreateClient(withUrlBase);

        var atRoot = await client.GetStringAsync("/example/");
        var atIndex = await client.GetStringAsync("/example/index.html");
        var atDeepLink = await client.GetStringAsync("/example/audiobooks");

        Assert.Contains("<head><base href=\"/example/\">", atRoot, StringComparison.Ordinal);
        Assert.Contains("<head><base href=\"/example/\">", atIndex, StringComparison.Ordinal);
        Assert.Contains("<head><base href=\"/example/\">", atDeepLink, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfiguredUrlBase_ServesASiteRootBase_ForARequestThatSkipsTheProxy()
    {
        var webRoot = CreateWebRootWithShell();
        using var factory = new ListenarrWebApplicationFactory();
        using var withUrlBase = CreateFactory(factory, "/example", webRoot);
        using var client = CreateClient(withUrlBase);

        var atRoot = await client.GetStringAsync("/");

        Assert.Contains("<head><base href=\"/\">", atRoot, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoUrlBase_LeavesTheShellExactlyAsTheFrontendBuiltIt()
    {
        var webRoot = CreateWebRootWithShell();
        using var factory = new ListenarrWebApplicationFactory();
        using var withoutUrlBase = CreateFactory(factory, "/", webRoot);
        using var client = CreateClient(withoutUrlBase);

        var atRoot = await client.GetAsync("/");
        var atDeepLink = await client.GetAsync("/audiobooks");

        Assert.Equal(HttpStatusCode.OK, atRoot.StatusCode);
        Assert.Equal(ShellHtml, await atRoot.Content.ReadAsStringAsync());
        Assert.Equal(ShellHtml, await atDeepLink.Content.ReadAsStringAsync());
        Assert.DoesNotContain("<base ", await atRoot.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private string CreateWebRootWithShell()
    {
        var webRoot = FileService.GetTempDirectory($"web-root-{Guid.NewGuid():N}");
        File.WriteAllText(Path.Join(webRoot, "index.html"), ShellHtml, Encoding.UTF8);
        return webRoot;
    }

    private static async Task<(int StatusCode, string? ContentType, string? CacheControl, string Body)> WriteShellAsync(
        SpaIndexHtml spaIndex,
        string pathBase)
    {
        var context = new DefaultHttpContext();
        context.Request.PathBase = new PathString(pathBase);
        using var body = new MemoryStream();
        context.Response.Body = body;

        await spaIndex.WriteAsync(context);

        return (
            context.Response.StatusCode,
            context.Response.ContentType,
            context.Response.Headers.CacheControl.ToString() is { Length: > 0 } cacheControl ? cacheControl : null,
            Encoding.UTF8.GetString(body.ToArray()));
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
    }

    private static WebApplicationFactory<Program> CreateFactory(
        ListenarrWebApplicationFactory factory,
        string urlBase,
        string webRoot)
    {
        return factory.WithWebHostBuilder(builder =>
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

    private static string[] SplitOn(string value, string separator)
    {
        return value.Split(separator, StringSplitOptions.None)[1..];
    }
}
