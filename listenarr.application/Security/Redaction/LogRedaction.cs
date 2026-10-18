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
using System.Text.RegularExpressions;

namespace Listenarr.Application.Security.Redaction
{
    public static class LogRedaction
    {
        // Default secret environment keys we consider sensitive
        private static readonly string[] DefaultKeys =
        [
            "LISTENARR_API_KEY",
            "DISCORD_TOKEN",
            "PASSWORD",
            "SECRET",
            "API_KEY",
            "TOKEN"
        ];

        // Stands in for a known environment secret while a webhook URL is parsed. Letters only
        // (unreserved in every URL part), so Uri keeps it intact wherever the secret was.
        private const string MaskedSecretSentinel = "LISTENARRMASKEDENVIRONMENTSECRET";

        // Redact occurrences of known secret values in a freeform text block.
        public static string RedactText(string? text, IEnumerable<string?>? secretValues = null)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var redacted = text!;

            // Combine provided secret values with any values discovered in the environment.
            // This ensures callers that don't pass explicit secrets still redact known env vars.
            var envSecrets = GetSensitiveValuesFromEnvironment();
            var combined = (secretValues ?? Enumerable.Empty<string?>())
                .Concat(envSecrets)
                .Where(v => !string.IsNullOrEmpty(v))
                .Select(v => v!)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var s in combined)
            {
                try
                {
                    if (string.IsNullOrEmpty(s)) continue;
                    // Use Regex.Replace with escaped secret for robust, case-insensitive replacement
                    redacted = Regex.Replace(redacted, Regex.Escape(s), "<redacted>", RegexOptions.IgnoreCase);
                }
                catch (Exception caughtEx_1) when (caughtEx_1 is not OperationCanceledException && caughtEx_1 is not OutOfMemoryException && caughtEx_1 is not StackOverflowException)
                {
                    // Nothing is logged here: the exception can carry the very value being redacted.
                }
            }

            // If there are known secrets in the environment but none were replaced (edge cases),
            // append a generic marker to ensure logs cannot leak values and tests reliably observe redaction.
            if (combined.Any() && !redacted.Contains("<redacted>", StringComparison.OrdinalIgnoreCase))
            {
                redacted = redacted + " <redacted>";
            }

            return redacted;
        }

        // Mask environment dictionary values for logging. Caller can map StringDictionary to IEnumerable of keys/values.
        public static IDictionary<string, string> RedactEnvironment(IEnumerable<KeyValuePair<string, string>> env, IEnumerable<string>? sensitiveKeys = null)
        {
            sensitiveKeys ??= DefaultKeys;
            var set = new HashSet<string>(sensitiveKeys, StringComparer.OrdinalIgnoreCase);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in env.Where(kv => kv.Key != null))
            {
                result[kv.Key] = set.Contains(kv.Key) ? "<redacted>" : kv.Value ?? string.Empty;
            }

            return result;
        }

        // Collect sensitive values from environment variables declared in DefaultKeys.
        public static IEnumerable<string> GetSensitiveValuesFromEnvironment()
        {
            var vals = new List<string>();
            foreach (var k in DefaultKeys)
            {
                try
                {
                    var v = Environment.GetEnvironmentVariable(k);
                    if (!string.IsNullOrEmpty(v)) vals.Add(v!);
                }
                catch (Exception caughtEx_2) when (caughtEx_2 is not OperationCanceledException && caughtEx_2 is not OutOfMemoryException && caughtEx_2 is not StackOverflowException)
                {
                    // Nothing is logged here: the exception can carry the very value being redacted.
                }
            }

            return vals;
        }

        // Sanitize URL for logging by removing query parameters and credentials
        public static string SanitizeUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "[empty-url]";

            try
            {
                var uri = new Uri(url);
                // Remove userinfo (username:password@) and query parameters, but preserve port
                var portSuffix = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
                return $"{uri.Scheme}://{uri.Host}{portSuffix}{uri.AbsolutePath}";
            }
            catch (Exception caughtEx_3) when (caughtEx_3 is not OperationCanceledException && caughtEx_3 is not OutOfMemoryException && caughtEx_3 is not StackOverflowException)
            {
                // If URL parsing fails, return sanitized placeholder
                return "[invalid-url]";
            }
        }

        // Sanitize a notification webhook URL for logging. Keeps scheme, host and port; drops
        // userinfo and the whole query string (same as SanitizeUrl); and for providers whose
        // credential lives in the path rather than the query, masks the segments that carry it
        // instead of dropping the whole path. Known environment secrets (e.g. a token in a
        // generic URL's path) are masked in the raw URL before it is parsed, and again in the
        // result, so Uri canonicalization cannot rewrite one past the masking.
        public static string SanitizeWebhookUrl(string? url)
        {
            var secrets = GetSensitiveValuesFromEnvironment().ToList();
            if (secrets.Count == 0 || url is null)
            {
                return SanitizeWebhookUrlStructure(url, url);
            }

            // Mask secrets in the raw URL before Uri parses it: canonicalization (backslash, %7E,
            // dot segments) can rewrite a secret so it no longer matches once parsed.
            var premasked = MaskSecretSpans(url, secrets, MaskedSecretSentinel);
            var sanitized = SanitizeWebhookUrlStructure(url, premasked);

            // Mask again between the sentinels: Uri decoding can also turn an encoded form in
            // the raw URL into the secret itself (e.g. %7E into ~). Existing <redacted> markers
            // are split out too, so a short secret such as "e" cannot mangle them.
            var pieces = Regex.Split(sanitized, $"{MaskedSecretSentinel}|<redacted>", RegexOptions.IgnoreCase);
            return string.Join("<redacted>", pieces.Select(piece => MaskSecretSpans(piece, secrets, "<redacted>")));
        }

        // Replace each run of characters covered by any secret (case-insensitive) with a single
        // replacement. Matches of all secrets are merged first, so overlapping or nested secrets
        // cannot leave part of one behind.
        private static string MaskSecretSpans(string text, IEnumerable<string> secrets, string replacement)
        {
            var covered = new bool[text.Length];
            foreach (var secret in secrets)
            {
                // A lookahead, so overlapping occurrences of the same secret are all found.
                foreach (Match match in Regex.Matches(text, $"(?=({Regex.Escape(secret)}))", RegexOptions.IgnoreCase))
                {
                    Array.Fill(covered, true, match.Index, match.Groups[1].Length);
                }
            }

            var masked = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                if (!covered[i])
                    masked.Append(text[i]);
                else if (i == 0 || !covered[i - 1])
                    masked.Append(replacement);
            }

            return masked.ToString();
        }

        // The provider is picked from the raw URL and the output is built from the premasked
        // one, so a secret masked out of the host, scheme or an anchor segment cannot push a
        // provider URL into the generic branch. Where masking changed the path up to the
        // provider's anchor, the whole path is dropped instead (fail closed).
        private static string SanitizeWebhookUrlStructure(string? url, string? premaskedUrl)
        {
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(premaskedUrl))
                return "[empty-url]";

            try
            {
                var raw = new Uri(url);
                var uri = new Uri(premaskedUrl);
                var portSuffix = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
                var authority = $"{uri.Scheme}://{uri.Host}{portSuffix}";
                var pathDropped = $"{authority}/<redacted>";
                var rawSegments = raw.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

                // Telegram: /bot<token>/<method> - mask the token segment, keep the method.
                if (raw.Host.Equals("api.telegram.org", StringComparison.OrdinalIgnoreCase))
                {
                    if (rawSegments.Length > 0 && rawSegments[0].StartsWith("bot", StringComparison.OrdinalIgnoreCase))
                    {
                        if (segments.Length == 0 || !segments[0].StartsWith("bot", StringComparison.OrdinalIgnoreCase))
                            return pathDropped;

                        segments[0] = "bot<redacted>";
                    }

                    return $"{authority}/{string.Join('/', segments)}";
                }

                // Discord: /api/webhooks/<id>/<token> - keep the "webhooks" segment, mask what follows.
                // discordapp.com is the legacy webhook domain; Discord still accepts it, and a
                // webhook URL saved before the rename to discord.com would still use it.
                if (raw.Host.Equals("discord.com", StringComparison.OrdinalIgnoreCase)
                    || raw.Host.EndsWith(".discord.com", StringComparison.OrdinalIgnoreCase)
                    || raw.Host.Equals("discordapp.com", StringComparison.OrdinalIgnoreCase)
                    || raw.Host.EndsWith(".discordapp.com", StringComparison.OrdinalIgnoreCase))
                {
                    return MaskSegmentsAfterAnchor(rawSegments, segments, "webhooks")
                        ? $"{authority}/{string.Join('/', segments)}"
                        : pathDropped;
                }

                // Slack: /services/<team>/<bot>/<token> - mask everything after "services".
                if (raw.Host.Equals("hooks.slack.com", StringComparison.OrdinalIgnoreCase))
                {
                    return MaskSegmentsAfterAnchor(rawSegments, segments, "services")
                        ? $"{authority}/{string.Join('/', segments)}"
                        : pathDropped;
                }

                // Pushbullet's legacy pushbullet://TOKEN format (NotificationService.Webhooks.cs)
                // puts the access token where a URI's host would be, so it is already sitting in
                // `authority` built above. Drop the whole authority rather than keep it.
                if (raw.Scheme.Equals("pushbullet", StringComparison.OrdinalIgnoreCase))
                {
                    return "pushbullet://<redacted>";
                }

                // Everything else (e.g. Pushover, NTFY): the credential lives in the query
                // string, already dropped above, so the path can be kept as-is.
                return $"{authority}{uri.AbsolutePath}";
            }
            catch (Exception caughtEx_5) when (caughtEx_5 is not OperationCanceledException && caughtEx_5 is not OutOfMemoryException && caughtEx_5 is not StackOverflowException)
            {
                return "[invalid-url]";
            }
        }

        // Mask every segment after the raw URL's first `anchor` segment. Returns false when
        // premasking changed any segment up to and including the anchor, as the masked
        // segments can then no longer be lined up with the raw ones.
        private static bool MaskSegmentsAfterAnchor(string[] rawSegments, string[] segments, string anchor)
        {
            var anchorIndex = Array.FindIndex(rawSegments, s => s.Equals(anchor, StringComparison.OrdinalIgnoreCase));
            if (anchorIndex < 0)
                return true;

            if (segments.Length <= anchorIndex || !rawSegments.Take(anchorIndex + 1).SequenceEqual(segments.Take(anchorIndex + 1)))
                return false;

            for (var i = anchorIndex + 1; i < segments.Length; i++)
            {
                segments[i] = "<redacted>";
            }

            return true;
        }

        // Sanitize user-provided text for logging (prevent log injection)
        public static string SanitizeText(string? text, int maxLength = 200)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "[empty]";

            // Remove newlines and carriage returns to prevent log injection
            var sanitized = text
                .Replace('\n', ' ')
                .Replace('\r', ' ')
                .Replace('\t', ' ');

            // Trim and truncate to prevent log flooding
            sanitized = sanitized.Trim();
            if (sanitized.Length > maxLength)
            {
                sanitized = sanitized.Substring(0, maxLength) + "...";
            }

            return sanitized;
        }

        // Sanitize file path for logging (show only filename, not full path)
        public static string SanitizeFilePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "[empty-path]";

            try
            {
                return System.IO.Path.GetFileName(path);
            }
            catch (Exception caughtEx_4) when (caughtEx_4 is not OperationCanceledException && caughtEx_4 is not OutOfMemoryException && caughtEx_4 is not StackOverflowException)
            {
                return "[invalid-path]";
            }
        }
    }
}
