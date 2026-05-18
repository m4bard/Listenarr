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
using System.Text.Json.Serialization;

namespace Listenarr.Domain.Models
{
    public class StartupConfig
    {
        public const string DefaultApiVersion = "1";

        // Minimal set of keys from the user's requested config.json. Keep names flexible.
        public string? LogLevel { get; set; }
        public bool? EnableSsl { get; set; }
        public int? Port { get; set; }
        public int? SslPort { get; set; }
        public string? UrlBase { get; set; }
        public string? BindAddress { get; set; }
        public string? ApiKey { get; set; }
        public string? UpdateMechanism { get; set; }
        public bool? LaunchBrowser { get; set; }
        public string? Branch { get; set; }
        public string? InstanceName { get; set; }
        public int? SyslogPort { get; set; }
        public bool? AnalyticsEnabled { get; set; }
        public string? ApiVersion { get; set; }

        // This is the new flag the user asked for. Accept both boolean or string-like values via JSON.
        [JsonPropertyName("AuthenticationRequired")]
        public string? AuthenticationRequired { get; set; }

        public string? SslCertPath { get; set; }
        public string? SslCertPassword { get; set; }

        // FFmpeg/ffprobe installer configuration
        public FfmpegConfig? Ffmpeg { get; set; }

        public bool IsAuthenticationEnabled()
            => IsAuthenticationRequiredValue(AuthenticationRequired);

        public string GetEffectiveApiVersion(string? requestedApiVersion = null)
            => NormalizeApiVersionString(ApiVersion)
               ?? NormalizeApiVersionString(requestedApiVersion)
               ?? DefaultApiVersion;

        public static bool IsAuthenticationRequiredValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (bool.TryParse(value, out var parsed))
            {
                return parsed;
            }

            return value.Trim().ToLowerInvariant() is "enabled" or "true" or "yes" or "1";
        }

        public static string? NormalizeApiVersionString(string? version)
        {
            if (string.IsNullOrWhiteSpace(version)) return null;
            var trimmed = version.Trim();
            if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
            {
                trimmed = trimmed[1..];
            }

            return TryNormalizeNumericApiVersion(trimmed, out var normalized) ? normalized : null;
        }

        private static bool TryNormalizeNumericApiVersion(string value, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var segments = new List<string>();
            var segmentStart = 0;

            for (var i = 0; i <= value.Length; i++)
            {
                if (i < value.Length && value[i] != '.')
                {
                    continue;
                }

                var segmentLength = i - segmentStart;
                if (segmentLength <= 0)
                {
                    return false;
                }

                var segment = value.Substring(segmentStart, segmentLength);
                for (var j = 0; j < segment.Length; j++)
                {
                    if (!char.IsDigit(segment[j]))
                    {
                        return false;
                    }
                }

                var nonZeroIndex = 0;
                while (nonZeroIndex < segment.Length - 1 && segment[nonZeroIndex] == '0')
                {
                    nonZeroIndex++;
                }

                segments.Add(segment[nonZeroIndex..]);
                segmentStart = i + 1;
            }

            while (segments.Count > 1 && segments[^1] == "0")
            {
                segments.RemoveAt(segments.Count - 1);
            }

            normalized = string.Join('.', segments);
            return !string.IsNullOrWhiteSpace(normalized);
        }
    }

    public class FfmpegConfig
    {
        // Provider key: e.g., "johnvansickle", "gyan", "evermeet", or "github:<owner>/<repo>"
        public string? Provider { get; set; }

        // Optional explicit asset name or tag to pin a release, e.g., "ffmpeg-6.0.zip" or "6.0"
        public string? ReleaseOverride { get; set; }

        // Optional URL template for checksum file discovery (e.g., GitHub releases assets or a SHA file)
        public string? ChecksumUrl { get; set; }

        // Optional architecture hint, e.g., "x86_64", "arm64"
        public string? Arch { get; set; }
    }
}
