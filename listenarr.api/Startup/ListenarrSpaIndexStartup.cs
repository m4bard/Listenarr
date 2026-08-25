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

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.FileProviders;

namespace Listenarr.Api.Startup;

/// <summary>
/// Serves the single-page-app shell with a <c>&lt;base href&gt;</c> describing the path the
/// request arrived under.
/// </summary>
/// <remarks>
/// The frontend is built once and shipped in the image, so it cannot know at build time which
/// sub-path an operator will mount it on. Everything it asks for afterwards - assets, router
/// paths, API calls, hub connections - is resolved relative to the document base, so injecting
/// the base is enough to make the whole app sub-path aware without a rebuild.
/// </remarks>
public static class ListenarrSpaIndexStartup
{
    internal const string IndexFileName = "index.html";

    /// <summary>
    /// Builds the shell rewriter, or returns null when no <c>UrlBase</c> is configured so the
    /// site-root deployment keeps serving index.html straight off the static file middleware.
    /// </summary>
    internal static SpaIndexHtml? CreateListenarrSpaIndex(this WebApplication app)
    {
        var configuredUrlBase = app.Services
            .GetRequiredService<IStartupConfigService>()
            .GetConfig()?
            .UrlBase;

        if (ListenarrUrlBaseStartup.NormalizeUrlBase(configuredUrlBase) is null)
        {
            return null;
        }

        return new SpaIndexHtml(app.Environment.WebRootFileProvider);
    }

    /// <summary>
    /// Intercepts the shell requests that the static file middleware would otherwise answer
    /// verbatim, namely the directory root and an explicit index.html.
    /// </summary>
    internal static WebApplication UseListenarrSpaIndex(this WebApplication app, SpaIndexHtml? spaIndex)
    {
        if (spaIndex is null)
        {
            return app;
        }

        app.Use(async (context, next) =>
        {
            if (!IsShellRequest(context.Request))
            {
                await next(context);
                return;
            }

            await spaIndex.WriteAsync(context);
        });

        return app;
    }

    /// <summary>
    /// Maps the SPA fallback that answers deep links such as <c>/example/audiobooks</c>.
    /// </summary>
    internal static WebApplication MapListenarrSpaFallback(this WebApplication app, SpaIndexHtml? spaIndex)
    {
        if (spaIndex is null)
        {
            app.MapFallbackToFile(IndexFileName);
            return app;
        }

        app.MapFallback((RequestDelegate)spaIndex.WriteAsync);
        return app;
    }

    private static bool IsShellRequest(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            return false;
        }

        var path = request.Path.Value;
        return string.IsNullOrEmpty(path) ||
            path == "/" ||
            path.Equals($"/{IndexFileName}", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Holds the built index.html and hands out copies carrying a <c>&lt;base href&gt;</c> for the
/// request path base.
/// </summary>
internal sealed class SpaIndexHtml
{
    // A request path base only ever takes one of a handful of values: empty for a direct hit on
    // the container, the configured UrlBase, or a prefix forwarded by a trusted proxy. The cap
    // keeps a misbehaving proxy from growing the cache without bound.
    private const int MaxCachedBasePaths = 8;

    private readonly IFileProvider _fileProvider;
    private readonly ConcurrentDictionary<string, string> _byBasePath = new(StringComparer.Ordinal);
    private readonly Lazy<string?> _source;

    internal SpaIndexHtml(IFileProvider fileProvider)
    {
        _fileProvider = fileProvider;
        _source = new Lazy<string?>(ReadIndexHtml, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal async Task WriteAsync(HttpContext context)
    {
        if (!TryGetHtml(context.Request.PathBase, out var html))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";

        // The body now varies by the path base the request arrived under, so it is not safe for a
        // shared cache to hand the same shell to a client reaching the app a different way.
        context.Response.Headers.CacheControl = "no-cache";

        await context.Response.WriteAsync(html, Encoding.UTF8, context.RequestAborted);
    }

    internal bool TryGetHtml(PathString pathBase, out string html)
    {
        var source = _source.Value;
        if (source is null)
        {
            html = string.Empty;
            return false;
        }

        var basePath = NormalizeBasePath(pathBase);
        if (_byBasePath.TryGetValue(basePath, out var cached))
        {
            html = cached;
            return true;
        }

        html = InjectBaseHref(source, basePath);
        if (_byBasePath.Count < MaxCachedBasePaths)
        {
            _byBasePath.TryAdd(basePath, html);
        }

        return true;
    }

    /// <summary>
    /// Reduces a request path base to its URL form with no trailing slash. The empty string means
    /// the site root, which yields a <c>&lt;base href="/"&gt;</c>.
    /// </summary>
    internal static string NormalizeBasePath(PathString pathBase)
    {
        // ToUriComponent re-escapes what PathString holds decoded, so a base containing a space or
        // a non-ASCII character survives the trip into an href.
        var value = pathBase.HasValue ? pathBase.ToUriComponent() : string.Empty;
        return value.TrimEnd('/');
    }

    /// <summary>
    /// Inserts a <c>&lt;base&gt;</c> immediately after the opening head tag. HTML honours the
    /// first base element with an href, so this wins over any the document already carries.
    /// </summary>
    internal static string InjectBaseHref(string html, string basePath)
    {
        var tag = $"<base href=\"{WebUtility.HtmlEncode(basePath)}/\">";

        var headStart = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        if (headStart >= 0)
        {
            var headEnd = html.IndexOf('>', headStart);
            if (headEnd >= 0)
            {
                return html.Insert(headEnd + 1, tag);
            }
        }

        // No usable head tag. A base element before the html element is still hoisted into the
        // head the parser creates, so prepending keeps the document working.
        return tag + html;
    }

    private string? ReadIndexHtml()
    {
        var file = _fileProvider.GetFileInfo(ListenarrSpaIndexStartup.IndexFileName);
        if (!file.Exists || file.IsDirectory)
        {
            return null;
        }

        using var stream = file.CreateReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
