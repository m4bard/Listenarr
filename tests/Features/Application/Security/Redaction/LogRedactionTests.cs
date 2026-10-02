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
namespace Listenarr.Tests.Features.Application.Security.Redaction
{
    public class LogRedactionTests
    {
        [Fact]
        public void RedactText_ReplacesSensitiveEnvironmentValues()
        {
            var key = "LISTENARR_API_KEY";
            var secret = "supersecret-TEST-123";
            try
            {
                Environment.SetEnvironmentVariable(key, secret);

                var inputs = new[]
                {
                    $"This is a log line containing the secret: {secret}",
                    $"Multiple {secret} occurrences {secret}"
                };

                foreach (var input in inputs)
                {
                    var redacted = LogRedaction.RedactText(input, LogRedaction.GetSensitiveValuesFromEnvironment());
                    Assert.DoesNotContain(secret, redacted);
                    Assert.Contains("<redacted>", redacted);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }

        [Fact]
        public void RedactText_LeavesUnrelatedTextUnchanged_WhenNoSecretValueAppears()
        {
            // Regression for #298: SystemProcessRunner calls RedactText unconditionally on every
            // subprocess's stdout/stderr before logging it. A fallback in RedactText appended a
            // literal " <redacted>" suffix whenever ANY of the known secret env vars was set,
            // even if its value never appeared in the text. That corrupted ffprobe's JSON stdout
            // (no longer valid JSON once something was appended after the closing brace) any time
            // an unrelated .env file set e.g. PASSWORD or API_KEY for a different service entirely,
            // breaking JsonSerializer.Deserialize for every file with a misleading error.
            var key = "LISTENARR_API_KEY";
            var secret = "some-unrelated-secret-value-999";
            var ffprobeLikeJson = "{\"format\":{\"duration\":\"320.000000\",\"bit_rate\":\"128000\"}}";

            try
            {
                Environment.SetEnvironmentVariable(key, secret);

                var redacted = LogRedaction.RedactText(ffprobeLikeJson, LogRedaction.GetSensitiveValuesFromEnvironment());

                Assert.Equal(ffprobeLikeJson, redacted);
            }
            finally
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }

        [Fact]
        public void GetSensitiveValuesFromEnvironment_ReturnsSetVariables()
        {
            var key = "LISTENARR_API_KEY";
            var secret = "env-secret-XYZ";
            try
            {
                Environment.SetEnvironmentVariable(key, secret);
                var vals = LogRedaction.GetSensitiveValuesFromEnvironment();
                Assert.Contains(secret, vals);
            }
            finally
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }
    }
}
