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
using Listenarr.Application.Metadata.Faults;
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
                if (!response.IsSuccessStatusCode)
                {
                    // One non-success is a positive answer about the record: the provider was
                    // asked and says there is nothing under that identifier. That one becomes
                    // null, which is what every caller above reads as "no such book".
                    //
                    // Nothing else does. A 500, a 502 and a 403 say only that the request did
                    // not get an answer, and flattening them into the same null makes the
                    // client state, on the provider's behalf, something the provider never
                    // said.
                    if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                    {
                        _logger.LogWarning("Audible API has no record ({StatusCode}) for URL {Url}", response.StatusCode, url);
                        return null;
                    }

                    // 429 is the documented shape of pushback. 403 is the one Audible reaches
                    // for more often: the catalog endpoints take no credentials, so there is
                    // no authorization here to fail, and a forbidden catalog read in practice
                    // means the caller has been shut out for asking too much. Both get the
                    // same answer, which is to stop asking for a while.
                    if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)
                    {
                        var retryAfter = ReadRetryAfter(response);
                        _logger.LogWarning(
                            "Audible API asked for less traffic ({StatusCode}) for URL {Url}; retry after {RetryAfter}",
                            response.StatusCode,
                            url,
                            retryAfter?.ToString() ?? "unspecified");
                        // The URL is logged, not carried. This message reaches API clients
                        // through catches that report what the exception said, and the query
                        // string holds the ASIN or the search terms the request was built from.
                        throw new MetadataProviderThrottledException(
                            "The Audible API asked for less traffic",
                            retryAfter);
                    }

                    throw new HttpRequestException(
                        $"The Audible API did not answer (status {(int)response.StatusCode})",
                        null,
                        response.StatusCode);
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
                // A refused connection, a name that will not resolve, a status that was not an
                // answer: none of them is evidence about the book, and all of them propagate.
                // One log line covers the transport faults and the statuses raised above.
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
