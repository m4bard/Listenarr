using System;
using Listenarr.Domain.Models;

namespace Listenarr.Api.Services.Adapters
{
    internal static class DownloadClientUriBuilder
    {
        public static string BuildAuthority(DownloadClientConfiguration client)
        {
            return BuildUri(client, "/").GetLeftPart(UriPartial.Authority);
        }

        public static Uri BuildUri(
            DownloadClientConfiguration client,
            string path,
            bool includeCredentials = false)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            var rawHost = (client.Host ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(rawHost))
            {
                throw new InvalidOperationException("Download client host is required.");
            }

            var scheme = client.UseSSL ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
            var hostWithScheme = rawHost.Contains("://", StringComparison.Ordinal)
                ? rawHost
                : $"{scheme}://{rawHost}";

            if (!Uri.TryCreate(hostWithScheme, UriKind.Absolute, out var parsedHost) || string.IsNullOrWhiteSpace(parsedHost.Host))
            {
                throw new InvalidOperationException($"Invalid download client host '{rawHost}'.");
            }

            var builder = new UriBuilder(parsedHost)
            {
                Scheme = scheme,
                Port = client.Port > 0 ? client.Port : (parsedHost.IsDefaultPort ? -1 : parsedHost.Port),
                Path = NormalizePath(path),
                Query = string.Empty,
                Fragment = string.Empty
            };

            if (includeCredentials
                && !string.IsNullOrWhiteSpace(client.Username)
                && !string.IsNullOrWhiteSpace(client.Password))
            {
                builder.UserName = client.Username;
                builder.Password = client.Password;
            }
            else
            {
                builder.UserName = string.Empty;
                builder.Password = string.Empty;
            }

            return builder.Uri;
        }

        private static string NormalizePath(string path)
        {
            var trimmed = (path ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return "/";
            }

            return trimmed.StartsWith("/", StringComparison.Ordinal)
                ? trimmed
                : "/" + trimmed;
        }
    }
}
