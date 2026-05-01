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

using Listenarr.Api.Models;
using Listenarr.Domain.Models;

namespace Listenarr.Api.Converters
{
    public static class StartupConfigDtoConverter
    {
        public static StartupConfigDto FromStartupConfig(StartupConfig? config, string? requestedApiVersion)
            => new()
            {
                AuthenticationRequired = config?.IsAuthenticationEnabled() == true,
                ApiVersion = NormalizeApiVersionString(config?.ApiVersion) ?? NormalizeApiVersionString(requestedApiVersion) ?? "1",
            };

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
}
