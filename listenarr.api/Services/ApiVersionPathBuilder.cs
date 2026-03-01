using System;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace Listenarr.Api.Services
{
    /// <summary>
    /// Utility methods for building versioned API paths from request context.
    /// Falls back to v1 when no explicit version can be resolved.
    /// </summary>
    public static class ApiVersionPathBuilder
    {
        private const string DefaultApiVersion = "1";
        private static readonly Regex ApiVersionFromPathRegex = new(@"^/api/v(?<version>\d+(?:\.\d+)?)(?:/|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LeadingApiPrefixRegex = new(@"^/api(?:/v\d+(?:\.\d+)?)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string NormalizeApiVersionString(string? version)
        {
            if (string.IsNullOrWhiteSpace(version)) return DefaultApiVersion;

            var trimmed = version.Trim();
            if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
            {
                trimmed = trimmed[1..];
            }

            // Normalize equivalent forms like 1.0 / 1.0.0 to just 1.
            if (Regex.IsMatch(trimmed, @"^\d+(?:\.0+)+$"))
            {
                var major = trimmed.Split('.')[0];
                return string.IsNullOrWhiteSpace(major) ? DefaultApiVersion : major;
            }

            return string.IsNullOrWhiteSpace(trimmed) ? DefaultApiVersion : trimmed;
        }

        public static string ResolveApiVersion(HttpContext? context, string? fallbackVersion = null)
        {
            var fallback = NormalizeApiVersionString(fallbackVersion);

            try
            {
                if (context?.Request?.RouteValues?.TryGetValue("version", out var routeVersionObj) == true)
                {
                    var routeVersion = routeVersionObj?.ToString();
                    if (!string.IsNullOrWhiteSpace(routeVersion))
                    {
                        return NormalizeApiVersionString(routeVersion);
                    }
                }
            }
            catch { }

            try
            {
                var path = context?.Request?.Path.Value;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var match = ApiVersionFromPathRegex.Match(path);
                    if (match.Success)
                    {
                        var parsed = match.Groups["version"].Value;
                        if (!string.IsNullOrWhiteSpace(parsed))
                        {
                            return NormalizeApiVersionString(parsed);
                        }
                    }
                }
            }
            catch { }

            return fallback;
        }

        public static string GetApiVersionSegment(HttpContext? context, string? fallbackVersion = null)
            => $"v{ResolveApiVersion(context, fallbackVersion)}";

        public static string BuildApiPath(string endpoint, HttpContext? context = null, string? fallbackVersion = null)
        {
            var normalizedEndpoint = NormalizeEndpoint(endpoint);
            return $"/api/{GetApiVersionSegment(context, fallbackVersion)}{normalizedEndpoint}";
        }

        public static string BuildImagePath(string identifier, HttpContext? context = null, string? fallbackVersion = null, string? sourceUrl = null)
        {
            var encodedIdentifier = Uri.EscapeDataString(identifier ?? string.Empty);
            var path = BuildApiPath($"/images/{encodedIdentifier}", context, fallbackVersion);
            if (string.IsNullOrWhiteSpace(sourceUrl)) return path;
            return $"{path}?url={Uri.EscapeDataString(sourceUrl)}";
        }

        private static string NormalizeEndpoint(string endpoint)
        {
            var normalized = string.IsNullOrWhiteSpace(endpoint) ? "/" : endpoint.Trim();
            if (!normalized.StartsWith('/')) normalized = "/" + normalized;

            normalized = LeadingApiPrefixRegex.Replace(normalized, string.Empty);
            if (string.IsNullOrWhiteSpace(normalized)) return "/";
            return normalized.StartsWith('/') ? normalized : "/" + normalized;
        }
    }
}