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
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Security.Outbound;

public static class OutboundRequestSecurity
{
    public static bool TryValidateExternalHttpUrl(string url, out string reason, bool allowPrivateTargets = false)
    {
        reason = string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            reason = "Invalid URL format";
            return false;
        }

        return TryValidateExternalHttpUri(uri, out reason, allowPrivateTargets);
    }

    public static bool TryValidateExternalHttpUri(Uri uri, out string reason, bool allowPrivateTargets = false)
    {
        reason = string.Empty;

        if (!uri.IsAbsoluteUri)
        {
            reason = "URL must be absolute";
            return false;
        }

        if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            reason = $"Unsupported URL scheme '{uri.Scheme}'";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            reason = "URLs with embedded credentials are not allowed";
            return false;
        }

        if (!allowPrivateTargets)
        {
            var host = uri.Host ?? string.Empty;
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            {
                reason = "Localhost or local-network hostnames are not allowed";
                return false;
            }

            if (IPAddress.TryParse(host, out var ip) && SecurityRequestUtils.IsPrivateOrLoopback(ip))
            {
                reason = "Private or loopback IP targets are not allowed";
                return false;
            }
        }

        return true;
    }

    public static async Task<bool> TryValidateResolvedExternalHttpUriAsync(Uri uri, ILogger logger, bool allowPrivateTargets = false)
    {
        if (allowPrivateTargets)
        {
            return true;
        }

        try
        {
            var host = uri.Host;
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            if (IPAddress.TryParse(host, out var parsedIp))
            {
                return !SecurityRequestUtils.IsPrivateOrLoopback(parsedIp);
            }

            var addresses = await Dns.GetHostAddressesAsync(host);
            if (addresses == null || addresses.Length == 0)
            {
                logger.LogWarning("Blocked outbound request because DNS resolution returned no addresses: {Host}", host);
                return false;
            }

            var privateOrLoopback = addresses.FirstOrDefault(SecurityRequestUtils.IsPrivateOrLoopback);
            if (privateOrLoopback != null)
            {
                logger.LogWarning(
                    "Blocked outbound request because DNS resolved to private/loopback address. Host={Host}, Address={Address}",
                    host,
                    privateOrLoopback);
                return false;
            }

            return true;
        }
        catch (SocketException ex)
        {
            logger.LogWarning(ex, "Blocked outbound request because DNS resolution failed for host {Host}", uri.Host);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            logger.LogWarning(ex, "Blocked outbound request due to unexpected DNS validation error for host {Host}", uri.Host);
            return false;
        }
    }

    public static async Task<(HttpResponseMessage Response, Uri FinalUri)> SendWithValidatedRedirectsAsync(
        Func<Uri, HttpRequestMessage> requestFactory,
        Uri initialUri,
        HttpClient noRedirectClient,
        ILogger logger,
        bool allowPrivateTargets = false,
        int maxRedirects = 5,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
        CancellationToken cancellationToken = default)
    {
        if (requestFactory == null) throw new ArgumentNullException(nameof(requestFactory));
        if (noRedirectClient == null) throw new ArgumentNullException(nameof(noRedirectClient));

        var (response, finalUri) = await SendWithValidatedRedirectsCoreAsync(
            requestFactory,
            initialUri,
            async (request, _, token) => await noRedirectClient.SendAsync(request, completionOption, token),
            logger,
            allowPrivateTargets,
            maxRedirects,
            cancellationToken);

        // The client always returns a response, so the loop never ends without one.
        return (response!, finalUri);
    }

    /// <summary>
    /// Sends a request and follows redirects itself, validating every target exactly as the
    /// client-based overload does, but hands each hop to <paramref name="sendHop"/> instead of
    /// sending it directly. A null from <paramref name="sendHop"/> means the hop was not sent and
    /// ends the loop with no response.
    /// </summary>
    /// <param name="requestFactory">Builds the request for each hop's target.</param>
    /// <param name="initialUri">The first target.</param>
    /// <param name="sendHop">Sends one hop: the request, its index (0 for the first send) and the
    /// caller's token. Returns the response, or null when the hop is not sent.</param>
    /// <param name="logger">Receives the address validation warnings.</param>
    /// <param name="allowPrivateTargets">Whether private and loopback targets are allowed.</param>
    /// <param name="maxRedirects">How many redirects may be followed after the first send.</param>
    /// <param name="cancellationToken">The caller's token, passed to every hop.</param>
    /// <returns>The final response, or null when a hop was not sent; and the last target.</returns>
    public static async Task<(HttpResponseMessage? Response, Uri FinalUri)> SendWithValidatedRedirectsAsync(
        Func<Uri, HttpRequestMessage> requestFactory,
        Uri initialUri,
        Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage?>> sendHop,
        ILogger logger,
        bool allowPrivateTargets = false,
        int maxRedirects = 5,
        CancellationToken cancellationToken = default)
    {
        if (requestFactory == null) throw new ArgumentNullException(nameof(requestFactory));
        if (sendHop == null) throw new ArgumentNullException(nameof(sendHop));

        return await SendWithValidatedRedirectsCoreAsync(
            requestFactory, initialUri, sendHop, logger, allowPrivateTargets, maxRedirects, cancellationToken);
    }

    // The one redirect loop behind both public overloads, so every hop runs the same validation.
    private static async Task<(HttpResponseMessage? Response, Uri FinalUri)> SendWithValidatedRedirectsCoreAsync(
        Func<Uri, HttpRequestMessage> requestFactory,
        Uri initialUri,
        Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage?>> sendHop,
        ILogger logger,
        bool allowPrivateTargets,
        int maxRedirects,
        CancellationToken cancellationToken)
    {
        Uri currentUri = initialUri;
        HttpResponseMessage? response = null;

        for (var redirectCount = 0; redirectCount <= maxRedirects; redirectCount++)
        {
            if (!TryValidateExternalHttpUri(currentUri, out var uriValidationReason, allowPrivateTargets))
            {
                throw new InvalidOperationException($"Blocked outbound URL: {uriValidationReason}");
            }

            if (!await TryValidateResolvedExternalHttpUriAsync(currentUri, logger, allowPrivateTargets))
            {
                throw new InvalidOperationException("Blocked outbound URL: DNS resolved to private or loopback address");
            }

            response?.Dispose();
            using var request = requestFactory(currentUri);
            response = await sendHop(request, redirectCount, cancellationToken);
            if (response == null)
            {
                return (null, currentUri);
            }

            if (IsRedirectStatusCode(response.StatusCode))
            {
                var location = response.Headers.Location;
                if (location == null)
                {
                    throw new HttpRequestException($"Redirect response from {currentUri} did not include a Location header.");
                }

                var nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
                if (!TryValidateExternalHttpUri(nextUri, out var redirectValidationReason, allowPrivateTargets))
                {
                    throw new InvalidOperationException($"Blocked redirect target: {redirectValidationReason}");
                }

                currentUri = nextUri;
                continue;
            }

            var finalUri = response.RequestMessage?.RequestUri ?? currentUri;
            if (!TryValidateExternalHttpUri(finalUri, out var finalValidationReason, allowPrivateTargets))
            {
                throw new InvalidOperationException($"Blocked final outbound URL: {finalValidationReason}");
            }

            if (!await TryValidateResolvedExternalHttpUriAsync(finalUri, logger, allowPrivateTargets))
            {
                throw new InvalidOperationException("Blocked final outbound URL: DNS resolved to private or loopback address");
            }

            return (response, finalUri);
        }

        response?.Dispose();
        throw new HttpRequestException($"Too many redirects while sending outbound request (>{maxRedirects}).");
    }

    public static bool IsRedirectStatusCode(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.Moved
               || statusCode == HttpStatusCode.Redirect
               || statusCode == HttpStatusCode.RedirectMethod
               || statusCode == HttpStatusCode.TemporaryRedirect
               || (int)statusCode == 308;
    }
}

