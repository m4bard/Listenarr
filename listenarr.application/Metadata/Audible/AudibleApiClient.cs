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
using System.Text.Json;
using Listenarr.Application.Metadata.Refresh;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Metadata.Audible
{
    internal sealed class AudibleApiClient
    {
        private const string BrowserAcceptHeader = "application/json, text/plain, */*";
        private const string BrowserAcceptLanguageHeader = "en-US,en;q=0.9";
        private const string BrowserUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";
        private const string AudibleApiAcceptHeader = "application/json";
        private const string AudibleApiUserAgent =
            "Dalvik/2.1.0 (Linux; U; Android 15); com.audible.application";
        private const string AudibleApiVerboseUserAgent =
            "Dalvik/2.1.0 (Linux; U; Android 15; good_phone Build/AAAA.240000.005); com.audible.application";

        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;

        public AudibleApiClient(HttpClient httpClient, ILogger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            ConfigureBrowserHeaders(_httpClient);
        }

        public Task<JsonDocument?> GetProductDocumentAsync(string asin, string region, string responseGroups)
        {
            var safeRegion = AudibleRequestHelper.NormalizeRegion(region);
            var url =
                $"{AudibleRequestHelper.BuildApiBaseUrl(safeRegion)}/1.0/catalog/products/{Uri.EscapeDataString(asin)}?" +
                $"{AudibleRequestHelper.BuildQueryString(new Dictionary<string, string?>
                {
                    ["response_groups"] = responseGroups,
                    ["image_sizes"] = "500,1000,2400,3200"
                })}";

            return GetJsonDocumentAsync(url, safeRegion, includeLocaleHeaders: false, timeoutSeconds: 10);
        }

        public async Task<JsonDocument?> GetJsonDocumentAsync(
            string url,
            string region,
            bool includeLocaleHeaders,
            int timeoutSeconds)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", includeLocaleHeaders ? AudibleApiVerboseUserAgent : AudibleApiUserAgent);
                request.Headers.TryAddWithoutValidation("Accept", AudibleApiAcceptHeader);
                request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip");
                request.Headers.TryAddWithoutValidation("Accept-Charset", "utf-8");
                if (includeLocaleHeaders)
                {
                    var locale = AudibleRequestHelper.GetLocale(region);
                    request.Headers.TryAddWithoutValidation("ACCEPTED-LANGUAGE", locale);
                    request.Headers.TryAddWithoutValidation("accept-language", locale);
                    request.Headers.TryAddWithoutValidation("X-ADP-SW", Random.Shared.Next(10_000_000, 99_999_999).ToString());
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                var response = await _httpClient.SendAsync(request, cts.Token);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    // Raised rather than logged and flattened to null. A null here is read two
                    // layers up as the provider saying it has never heard of the book, so a
                    // throttled sweep used to stamp everything it touched as checked.
                    var retryAfter = ReadRetryAfter(response);
                    _logger.LogWarning(
                        "Audible API asked for less traffic (429) for URL {Url}; retry after {RetryAfter}",
                        url,
                        retryAfter?.ToString() ?? "unspecified");
                    // The URL is logged, not carried. This message reaches API clients through
                    // more than one catch that echoes ex.Message, and the query string holds
                    // the ASIN or the search terms the request was built from.
                    throw new MetadataProviderThrottledException(
                        "The Audible API asked for less traffic",
                        retryAfter);
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Audible API returned status code {StatusCode} for URL {Url}", response.StatusCode, url);
                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
                return await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
            }
            catch (TaskCanceledException ex)
            {
                // Rewrapped, not rethrown. TaskCanceledException is an OperationCanceledException,
                // and every caller between here and the refresh run treats one of those as "the
                // run was asked to stop" rather than "this request did not arrive".
                _logger.LogWarning(ex, "Audible API request timed out for URL: {Url}", url);
                throw new HttpRequestException("The Audible API request timed out", ex);
            }
            catch (HttpRequestException ex)
            {
                // A transport fault propagates: a refused connection or a name that will not
                // resolve is not evidence about the book, and the caller has to be able to tell
                // the two apart.
                _logger.LogWarning(ex, "Audible API request failed for URL: {Url}", url);
                throw;
            }
            catch (MetadataProviderThrottledException)
            {
                // Raised a few lines up, inside this try. Without this the catch-all below would
                // swallow the pushback the throw exists to report.
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "Error performing Audible API request for URL: {Url}", url);
                return null;
            }
        }

        /// <summary>
        /// The wait the provider named, in either of the two shapes RFC 9110 allows. Null when it
        /// named none, or named one already in the past.
        /// </summary>
        private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
        {
            var header = response.Headers.RetryAfter;
            if (header == null)
            {
                return null;
            }

            if (header.Delta.HasValue)
            {
                return header.Delta.Value > TimeSpan.Zero ? header.Delta.Value : null;
            }

            if (header.Date.HasValue)
            {
                var wait = header.Date.Value - DateTimeOffset.UtcNow;
                return wait > TimeSpan.Zero ? wait : null;
            }

            return null;
        }

        public async Task<HttpResponseMessage?> GetWithTimeoutAsync(string url, int timeoutSeconds = 5)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                var resp = await _httpClient.GetAsync(url, cts.Token);
                return resp;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "Audible request timed out for URL: {Url}", url);
                return null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "Error performing Audible HTTP request for URL: {Url}", url);
                return null;
            }
        }

        private static void ConfigureBrowserHeaders(HttpClient httpClient)
        {
            httpClient.DefaultRequestHeaders.Accept.Clear();
            httpClient.DefaultRequestHeaders.Accept.ParseAdd(BrowserAcceptHeader);
            httpClient.DefaultRequestHeaders.AcceptLanguage.Clear();
            httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd(BrowserAcceptLanguageHeader);
            httpClient.DefaultRequestHeaders.UserAgent.Clear();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);
        }
    }
}
