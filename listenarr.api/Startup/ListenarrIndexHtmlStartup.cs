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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileProviders;

namespace Listenarr.Api.Startup;

/// <summary>
/// Serves the single-page-app entry document with its asset prefix resolved against the
/// configured URL base, and hands the same value to the app as <c>window.__listenarrUrlBase</c>.
/// </summary>
/// <remarks>
/// The frontend is built with Vite's <c>base: './'</c>, so every asset reference in the emitted
/// <c>index.html</c> is document-relative. Document-relative resolution is wrong the moment the
/// document is a deep link such as <c>/books/12</c>, so the prefix is resolved here rather than
/// left to the browser. There is deliberately no <c>&lt;base href&gt;</c> tag: that tag would
/// change resolution for every relative URL in the document, not just the asset references.
/// </remarks>
public static class ListenarrIndexHtmlStartup
{
    private const string IndexFileName = "index.html";
    private const string CachePropertyKey = "Listenarr.IndexHtmlDocumentCache";

    /// <summary>
    /// Matches an <c>href</c> or <c>src</c> attribute whose value is document-relative. Anchored
    /// on the whitespace that separates attributes so that neither a <c>data-href</c> style
    /// attribute nor a bare <c>./</c> elsewhere in the document is rewritten.
    /// </summary>
    private static readonly Regex DocumentRelativeAttributeRegex = new(
        """(?<lead>\s(?:href|src)\s*=\s*)(?<quote>["'])\./(?<path>[^"'<>]*)\k<quote>""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex EntryScriptRegex = new(
        """<script\b[^>]*\btype\s*=\s*["']module["']""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Serves <c>/</c> and <c>/index.html</c> from the rewritten document. Must be registered
    /// ahead of the static file middleware, which would otherwise serve the untransformed file.
    /// </summary>
    public static WebApplication UseListenarrIndexHtml(this WebApplication app)
    {
        var cache = GetOrCreateCache(app);

        app.Use(async (context, next) =>
        {
            if (!IsEntryDocumentPath(context.Request.Path) ||
                !await TryServeAsync(context, cache).ConfigureAwait(false))
            {
                await next(context).ConfigureAwait(false);
            }
        });

        return app;
    }

    /// <summary>
    /// Serves the rewritten document for client-side routes, replacing
    /// <c>MapFallbackToFile("index.html")</c>.
    /// </summary>
    public static WebApplication MapListenarrIndexHtmlFallback(this WebApplication app)
    {
        var cache = GetOrCreateCache(app);

        app.MapFallback(async context =>
        {
            if (!await TryServeAsync(context, cache).ConfigureAwait(false))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
            }
        });

        return app;
    }

    /// <summary>
    /// Rewrites the document-relative asset prefix to <paramref name="urlBase"/> and, when a URL
    /// base is configured, injects the runtime global ahead of the entry module so it is set
    /// before any application code runs.
    /// </summary>
    /// <param name="html">The emitted <c>index.html</c>.</param>
    /// <param name="urlBase">
    /// A normalized URL base such as <c>/example</c>, or null when serving from the site root. A
    /// null base still rewrites <c>./asset</c> to <c>/asset</c>, which is what the document would
    /// have contained before the frontend moved to a relative build base.
    /// </param>
    internal static string ApplyUrlBase(string html, string? urlBase)
    {
        var assetPrefix = WebUtility.HtmlEncode(urlBase ?? string.Empty);
        var rewritten = DocumentRelativeAttributeRegex.Replace(
            html,
            match =>
                match.Groups["lead"].Value
                + match.Groups["quote"].Value
                + assetPrefix
                + "/"
                + match.Groups["path"].Value
                + match.Groups["quote"].Value);

        return urlBase is null ? rewritten : InjectRuntimeUrlBase(rewritten, urlBase);
    }

    private static string InjectRuntimeUrlBase(string html, string urlBase)
    {
        // System.Text.Json's default encoder escapes '<', '>' and '&', so a configured base can
        // not close the script element even though it reaches this point as free text.
        var script = $"<script>window.__listenarrUrlBase = {JsonSerializer.Serialize(urlBase)};</script>";
        var insertAt = FindRuntimeGlobalInsertionPoint(html);

        return html.Insert(insertAt, script + "\n" + LeadingWhitespaceOfLine(html, insertAt));
    }

    private static int FindRuntimeGlobalInsertionPoint(string html)
    {
        var entryScript = EntryScriptRegex.Match(html);
        if (entryScript.Success)
        {
            return entryScript.Index;
        }

        var headEnd = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return headEnd >= 0 ? headEnd : 0;
    }

    private static string LeadingWhitespaceOfLine(string html, int index)
    {
        var lineStart = html.LastIndexOf('\n', Math.Max(index - 1, 0)) + 1;
        var whitespaceEnd = lineStart;
        while (whitespaceEnd < index && char.IsWhiteSpace(html[whitespaceEnd]))
        {
            whitespaceEnd++;
        }

        return html[lineStart..whitespaceEnd];
    }

    private static bool IsEntryDocumentPath(PathString path)
    {
        var value = path.Value;
        return string.IsNullOrEmpty(value)
            || value == "/"
            || string.Equals(value, $"/{IndexFileName}", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> TryServeAsync(HttpContext context, IndexHtmlDocumentCache cache)
    {
        var request = context.Request;
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            return false;
        }

        var document = cache.GetCurrent();
        if (document is null)
        {
            return false;
        }

        var response = context.Response;
        response.ContentType = "text/html; charset=utf-8";
        response.Headers.CacheControl = "no-cache";
        response.Headers.ETag = document.ETag;

        if (RequestMatchesETag(request, document.ETag))
        {
            response.StatusCode = StatusCodes.Status304NotModified;
            return true;
        }

        response.ContentLength = document.Content.Length;
        if (HttpMethods.IsHead(request.Method))
        {
            return true;
        }

        await response.Body.WriteAsync(document.Content, context.RequestAborted).ConfigureAwait(false);
        return true;
    }

    private static bool RequestMatchesETag(HttpRequest request, string etag)
    {
        foreach (var header in request.Headers.IfNoneMatch)
        {
            if (header is null)
            {
                continue;
            }

            foreach (var candidate in header.Split(','))
            {
                var trimmed = candidate.Trim();
                if (trimmed == "*" || string.Equals(trimmed, etag, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IndexHtmlDocumentCache GetOrCreateCache(WebApplication app)
    {
        // Both registration points share one cache, so the document is read and transformed once
        // no matter which of them serves a given request.
        var properties = ((IApplicationBuilder)app).Properties;
        if (properties.TryGetValue(CachePropertyKey, out var existing) &&
            existing is IndexHtmlDocumentCache cache)
        {
            return cache;
        }

        var configuredUrlBase = app.Services
            .GetRequiredService<IStartupConfigService>()
            .GetConfig()?
            .UrlBase;

        var created = new IndexHtmlDocumentCache(
            app.Environment.WebRootFileProvider,
            ListenarrUrlBaseStartup.NormalizeUrlBase(configuredUrlBase));

        properties[CachePropertyKey] = created;
        return created;
    }

    /// <summary>
    /// Holds the rewritten document so the transform runs once per build of the frontend rather
    /// than once per request. Keyed on the file's length and modification time so a rebuilt
    /// <c>index.html</c> is picked up without a restart.
    /// </summary>
    internal sealed class IndexHtmlDocumentCache(IFileProvider fileProvider, string? urlBase)
    {
        private readonly Lock _sync = new();
        private string? _cachedSourceKey;
        private IndexHtmlDocument? _cachedDocument;

        internal string? UrlBase { get; } = urlBase;

        internal IndexHtmlDocument? GetCurrent()
        {
            var file = fileProvider.GetFileInfo(IndexFileName);
            if (!file.Exists || file.IsDirectory)
            {
                return null;
            }

            var sourceKey = $"{file.Length}:{file.LastModified.UtcTicks}";

            lock (_sync)
            {
                if (_cachedDocument is not null && _cachedSourceKey == sourceKey)
                {
                    return _cachedDocument;
                }
            }

            string source;
            using (var stream = file.CreateReadStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                source = reader.ReadToEnd();
            }

            var document = IndexHtmlDocument.Create(ApplyUrlBase(source, UrlBase));

            lock (_sync)
            {
                _cachedSourceKey = sourceKey;
                _cachedDocument = document;
                return document;
            }
        }
    }

    internal sealed record IndexHtmlDocument(byte[] Content, string ETag)
    {
        internal static IndexHtmlDocument Create(string html)
        {
            var content = Encoding.UTF8.GetBytes(html);
            var etag = $"\"{Convert.ToHexString(SHA256.HashData(content))}\"";
            return new IndexHtmlDocument(content, etag);
        }
    }
}
