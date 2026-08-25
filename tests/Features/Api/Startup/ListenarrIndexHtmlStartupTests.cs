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

using System.Text;
using System.Text.Json;
using Listenarr.Api.Startup;
using Listenarr.Tests.Common;
using Microsoft.Extensions.FileProviders;

namespace Listenarr.Tests.Features.Api.Startup;

[Trait("Name", "ListenarrIndexHtmlStartupTests")]
[Trait("Category", "Api")]
public sealed class ListenarrIndexHtmlStartupTests : BaseTests
{
    /// <summary>
    /// A trimmed copy of the document Vite emits with <c>base: './'</c>: every asset reference is
    /// document-relative, the entry module is the first script, and nothing else is.
    /// </summary>
    private const string EmittedIndexHtml =
        """
        <!DOCTYPE html>
        <html lang="en">
          <head>
            <link rel="icon" type="image/x-icon" href="./favicon.ico">
            <link rel="manifest" href="./site.webmanifest">
            <title>Listenarr - Audiobook Management</title>
            <script type="module" crossorigin src="./assets/index-D69pwXdM.js"></script>
            <link rel="modulepreload" crossorigin href="./assets/pinia-8_mxSixj.js">
            <link rel="stylesheet" crossorigin href="./assets/index-D8-1G8bZ.css">
          </head>
          <body>
            <div id="app"></div>
          </body>
        </html>
        """;

    [Fact]
    public void ApplyUrlBase_PrefixesEveryDocumentRelativeAssetReference()
    {
        var rewritten = ListenarrIndexHtmlStartup.ApplyUrlBase(EmittedIndexHtml, "/example");

        Assert.DoesNotContain("\"./", rewritten, StringComparison.Ordinal);
        Assert.Contains("href=\"/example/favicon.ico\"", rewritten, StringComparison.Ordinal);
        Assert.Contains("href=\"/example/site.webmanifest\"", rewritten, StringComparison.Ordinal);
        Assert.Contains("src=\"/example/assets/index-D69pwXdM.js\"", rewritten, StringComparison.Ordinal);
        Assert.Contains("href=\"/example/assets/pinia-8_mxSixj.js\"", rewritten, StringComparison.Ordinal);
        Assert.Contains("href=\"/example/assets/index-D8-1G8bZ.css\"", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyUrlBase_WithoutAUrlBase_ProducesTheAbsoluteReferencesTheDocumentUsedToCarry()
    {
        var rewritten = ListenarrIndexHtmlStartup.ApplyUrlBase(EmittedIndexHtml, null);

        Assert.DoesNotContain("\"./", rewritten, StringComparison.Ordinal);
        Assert.Contains("href=\"/favicon.ico\"", rewritten, StringComparison.Ordinal);
        Assert.Contains("src=\"/assets/index-D69pwXdM.js\"", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("__listenarrUrlBase", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyUrlBase_InjectsTheRuntimeGlobalAheadOfTheEntryModule()
    {
        var rewritten = ListenarrIndexHtmlStartup.ApplyUrlBase(EmittedIndexHtml, "/example");

        var globalIndex = rewritten.IndexOf(
            "window.__listenarrUrlBase = \"/example\";",
            StringComparison.Ordinal);
        var entryScriptIndex = rewritten.IndexOf(
            "<script type=\"module\"",
            StringComparison.Ordinal);

        Assert.True(globalIndex >= 0, "The runtime global was not injected.");
        Assert.True(entryScriptIndex >= 0, "The entry module script disappeared from the document.");
        Assert.True(
            globalIndex < entryScriptIndex,
            "The runtime global must be set before the entry module runs.");
    }

    [Fact]
    public void ApplyUrlBase_DoesNotEmitABaseHrefTag()
    {
        var rewritten = ListenarrIndexHtmlStartup.ApplyUrlBase(EmittedIndexHtml, "/example");

        // The whole point of this approach is that URL resolution for the rest of the document is
        // left alone. A <base href> would silently move every other relative URL as well.
        Assert.DoesNotContain("<base ", rewritten, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<a data-href=\"./keep-me\">x</a>")]
    [InlineData("<script>const m = import(\"./chunk.js\");</script>")]
    [InlineData("<p>Paths look like ./this in prose.</p>")]
    public void ApplyUrlBase_LeavesEverythingThatIsNotAnAssetAttributeAlone(string fragment)
    {
        var html = $"<html><head><title>t</title></head><body>{fragment}</body></html>";

        Assert.Equal(html, ListenarrIndexHtmlStartup.ApplyUrlBase(html, null));
    }

    [Fact]
    public void ApplyUrlBase_KeepsAConfiguredBaseInsideTheScriptElement()
    {
        // NormalizeUrlBase does not reject angle brackets, so the injected literal has to be
        // encoded rather than trusted. Anything else is stored XSS driven by config.
        const string hostile = "/a\"</script><script>alert(1)</script>";

        var rewritten = ListenarrIndexHtmlStartup.ApplyUrlBase(EmittedIndexHtml, hostile);

        Assert.DoesNotContain("</script><script>alert(1)", rewritten, StringComparison.Ordinal);
        Assert.Contains(JsonSerializer.Serialize(hostile), rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyUrlBase_FallsBackToTheHeadWhenThereIsNoEntryModule()
    {
        const string html = "<html><head><title>t</title></head><body></body></html>";

        var rewritten = ListenarrIndexHtmlStartup.ApplyUrlBase(html, "/example");

        var globalIndex = rewritten.IndexOf("__listenarrUrlBase", StringComparison.Ordinal);
        Assert.True(globalIndex >= 0, "The runtime global was not injected.");
        Assert.True(
            globalIndex < rewritten.IndexOf("</head>", StringComparison.Ordinal),
            "The runtime global must still land inside the head.");
    }

    [Fact]
    public void DocumentCache_TransformsOnceAndReReadsWhenTheFrontendIsRebuilt()
    {
        var provider = new MutableIndexHtmlFileProvider(EmittedIndexHtml);
        var cache = new ListenarrIndexHtmlStartup.IndexHtmlDocumentCache(provider, "/example");

        var first = cache.GetCurrent();
        var second = cache.GetCurrent();

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, provider.ReadCount);

        provider.Replace(EmittedIndexHtml.Replace("index-D69pwXdM", "index-REBUILT", StringComparison.Ordinal));
        var third = cache.GetCurrent();

        Assert.NotNull(third);
        Assert.NotSame(first, third);
        Assert.Equal(2, provider.ReadCount);
        Assert.Contains(
            "src=\"/example/assets/index-REBUILT.js\"",
            Encoding.UTF8.GetString(third!.Content),
            StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentCache_ReturnsNothingWhenTheFrontendHasNotBeenBuilt()
    {
        var cache = new ListenarrIndexHtmlStartup.IndexHtmlDocumentCache(
            new NullFileProvider(),
            "/example");

        Assert.Null(cache.GetCurrent());
    }

    [Fact]
    public void DocumentCache_GivesDocumentsWithDifferentContentDifferentETags()
    {
        var atRoot = new ListenarrIndexHtmlStartup.IndexHtmlDocumentCache(
            new MutableIndexHtmlFileProvider(EmittedIndexHtml),
            null).GetCurrent();
        var underSubPath = new ListenarrIndexHtmlStartup.IndexHtmlDocumentCache(
            new MutableIndexHtmlFileProvider(EmittedIndexHtml),
            "/example").GetCurrent();

        Assert.NotNull(atRoot);
        Assert.NotNull(underSubPath);
        Assert.NotEqual(atRoot!.ETag, underSubPath!.ETag);
    }

    private sealed class MutableIndexHtmlFileProvider(string html) : IFileProvider
    {
        private byte[] _content = Encoding.UTF8.GetBytes(html);
        private DateTimeOffset _lastModified = DateTimeOffset.UnixEpoch;

        internal int ReadCount { get; private set; }

        internal void Replace(string updatedHtml)
        {
            _content = Encoding.UTF8.GetBytes(updatedHtml);
            _lastModified = _lastModified.AddSeconds(1);
        }

        public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

        public IFileInfo GetFileInfo(string subpath) =>
            string.Equals(subpath, "index.html", StringComparison.OrdinalIgnoreCase)
                ? new InMemoryFileInfo(this)
                : new NotFoundFileInfo(subpath);

        public Microsoft.Extensions.Primitives.IChangeToken Watch(string filter) =>
            new NullFileProvider().Watch(filter);

        private sealed class InMemoryFileInfo(MutableIndexHtmlFileProvider owner) : IFileInfo
        {
            public bool Exists => true;
            public bool IsDirectory => false;
            public DateTimeOffset LastModified => owner._lastModified;
            public long Length => owner._content.Length;
            public string Name => "index.html";
            public string? PhysicalPath => null;

            public Stream CreateReadStream()
            {
                owner.ReadCount++;
                return new MemoryStream(owner._content, writable: false);
            }
        }
    }
}
