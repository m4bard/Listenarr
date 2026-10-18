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
    [Collection(ProcessEnvironmentCollection.Name)]
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

        [Fact]
        public void SanitizeWebhookUrl_Telegram_MasksTokenSegment_KeepsMethodAndQueryDropped()
        {
            var token = "123456789:AAExampleBotTokenValueNotReal";
            var url = $"https://api.telegram.org/bot{token}/sendMessage?chat_id=987654321";

            var sanitized = LogRedaction.SanitizeWebhookUrl(url);

            Assert.DoesNotContain(token, sanitized);
            Assert.DoesNotContain("987654321", sanitized);
            Assert.Equal("https://api.telegram.org/bot<redacted>/sendMessage", sanitized);
        }

        [Fact]
        public void SanitizeWebhookUrl_Discord_MasksIdAndTokenSegments_KeepsApiWebhooksPath()
        {
            var id = "111222333444555666";
            var token = "DiscordWebhookTokenValueNotReal";
            var url = $"https://discord.com/api/webhooks/{id}/{token}";

            var sanitized = LogRedaction.SanitizeWebhookUrl(url);

            Assert.DoesNotContain(id, sanitized);
            Assert.DoesNotContain(token, sanitized);
            Assert.Contains("api/webhooks", sanitized);
            Assert.Equal("https://discord.com/api/webhooks/<redacted>/<redacted>", sanitized);
        }

        [Fact]
        public void SanitizeWebhookUrl_Slack_MasksEverythingAfterServices()
        {
            var team = "T00000000";
            var bot = "B00000000";
            var secret = "XXXXXXXXXXXXXXXXXXXXXXXX";
            var url = $"https://hooks.slack.com/services/{team}/{bot}/{secret}";

            var sanitized = LogRedaction.SanitizeWebhookUrl(url);

            Assert.DoesNotContain(team, sanitized);
            Assert.DoesNotContain(bot, sanitized);
            Assert.DoesNotContain(secret, sanitized);
            Assert.StartsWith("https://hooks.slack.com/services/", sanitized);
        }

        [Fact]
        public void SanitizeWebhookUrl_Pushover_DropsQueryString_KeepsHostAndPath()
        {
            var token = "app-token-abc123";
            var user = "user-key-xyz789";
            var url = $"https://api.pushover.net/1/messages.json?token={token}&user={user}";

            var sanitized = LogRedaction.SanitizeWebhookUrl(url);

            Assert.DoesNotContain(token, sanitized);
            Assert.DoesNotContain(user, sanitized);
            Assert.DoesNotContain("?", sanitized);
            Assert.Equal("https://api.pushover.net/1/messages.json", sanitized);
        }

        [Fact]
        public void SanitizeWebhookUrl_DiscordappLegacyDomain_MasksIdAndTokenSegments()
        {
            var id = "111222333444555666";
            var token = "DiscordWebhookTokenValueNotReal";
            var url = $"https://discordapp.com/api/webhooks/{id}/{token}";

            var sanitized = LogRedaction.SanitizeWebhookUrl(url);

            Assert.DoesNotContain(id, sanitized);
            Assert.DoesNotContain(token, sanitized);
            Assert.Contains("api/webhooks", sanitized);
            Assert.Equal("https://discordapp.com/api/webhooks/<redacted>/<redacted>", sanitized);
        }

        [Fact]
        public void SanitizeWebhookUrl_PushbulletUriSchemeToken_DoesNotLeakTokenAsHost()
        {
            // pushbullet://TOKEN is a legacy Listenarr webhook format (see
            // NotificationService.Webhooks.cs). The access token sits where a
            // URI's host would be; it must not survive sanitization.
            var token = "o.ExamplePushbulletTokenValueNotReal";
            var url = $"pushbullet://{token}";

            var sanitized = LogRedaction.SanitizeWebhookUrl(url);

            // Uri lowercases the host, so a case-sensitive DoesNotContain(token, ...) would pass
            // even if the token leaked through unredacted. Check case-insensitively instead.
            Assert.DoesNotContain(token, sanitized, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("pushbullet://<redacted>", sanitized);
        }

        [Fact]
        public void SanitizeWebhookUrl_ControlCase_NonCredentialUrl_PathSurvivesIntact()
        {
            // No query string, no known-provider host, no credential-shaped path segments.
            // A version that just blanks everything would fail this assertion, not just the ones above.
            var url = "https://example.com/some/harmless/path";

            var sanitized = LogRedaction.SanitizeWebhookUrl(url);

            Assert.Equal("https://example.com/some/harmless/path", sanitized);
        }

        [Fact]
        public void SanitizeWebhookUrl_GenericUrl_MasksPathTokenThatIsAlsoAnEnvironmentSecret()
        {
            var token = "generic-path-token-not-a-real-secret";
            var previous = Environment.GetEnvironmentVariable("TOKEN");
            try
            {
                Environment.SetEnvironmentVariable("TOKEN", token);

                var sanitized = LogRedaction.SanitizeWebhookUrl($"https://example.com/hooks/{token}?x=1");

                Assert.DoesNotContain(token, sanitized, StringComparison.OrdinalIgnoreCase);
                Assert.Equal("https://example.com/hooks/<redacted>", sanitized);
            }
            finally
            {
                Environment.SetEnvironmentVariable("TOKEN", previous);
            }
        }

        [Theory]
        [InlineData("tok\\en", "https://example.com/hooks/tok\\en")]
        [InlineData("tok%7Een", "https://example.com/hooks/tok%7Een")]
        [InlineData("a/../secretpart", "https://example.com/hooks/a/../secretpart")]
        public void SanitizeWebhookUrl_SecretRewrittenByUriCanonicalization_IsMasked(string token, string url)
        {
            var previous = Environment.GetEnvironmentVariable("TOKEN");
            try
            {
                Environment.SetEnvironmentVariable("TOKEN", token);

                var sanitized = LogRedaction.SanitizeWebhookUrl(url);

                Assert.Equal("https://example.com/hooks/<redacted>", sanitized);
            }
            finally
            {
                Environment.SetEnvironmentVariable("TOKEN", previous);
            }
        }

        // A secret Uri would rewrite must stay masked even when something else in the same URL
        // is already <redacted> (a second secret, or a provider's own token segments). Where it
        // sits before a provider's anchor segment, the whole path is dropped.
        [Theory]
        [InlineData("plainsecret1234", "tok\\en", "https://example.com/hooks/plainsecret1234/tok\\en", "https://example.com/hooks/<redacted>/<redacted>")]
        [InlineData("plainsecret1234", "tok%7Een", "https://example.com/hooks/plainsecret1234/tok%7Een", "https://example.com/hooks/<redacted>/<redacted>")]
        [InlineData("tok\\en", null, "https://discord.com/tok\\en/api/webhooks/123/abc", "https://discord.com/<redacted>")]
        [InlineData("x/../secretpart", null, "https://discord.com/api/x/../secretpart/webhooks/123/abc", "https://discord.com/<redacted>")]
        [InlineData("tok%7Een", null, "https://api.telegram.org/bot123:abc/tok%7Een", "https://api.telegram.org/bot<redacted>/<redacted>")]
        [InlineData("tok\\en", null, "https://hooks.slack.com/tok\\en/services/T/B/X", "https://hooks.slack.com/<redacted>")]
        [InlineData("pathpart?querypart", null, "https://example.com/hooks/pathpart?querypart", "https://example.com/hooks/<redacted>")]
        public void SanitizeWebhookUrl_SecretRewrittenByUri_IsMaskedAlongsideOtherRedactions(string token, string? secret, string url, string expected)
        {
            var previousToken = Environment.GetEnvironmentVariable("TOKEN");
            var previousSecret = Environment.GetEnvironmentVariable("SECRET");
            try
            {
                Environment.SetEnvironmentVariable("TOKEN", token);
                Environment.SetEnvironmentVariable("SECRET", secret);

                Assert.Equal(expected, LogRedaction.SanitizeWebhookUrl(url));
            }
            finally
            {
                Environment.SetEnvironmentVariable("TOKEN", previousToken);
                Environment.SetEnvironmentVariable("SECRET", previousSecret);
            }
        }

        // A secret the structural step already removes (userinfo, or an unparseable URL) needs
        // no extra masking, so the host or the usual placeholder is still logged.
        [Theory]
        [InlineData("userinfo-pass-value", "https://user:userinfo-pass-value@example.com/hooks", "https://example.com/hooks")]
        [InlineData("rawsecret77", "not a url rawsecret77", "[invalid-url]")]
        public void SanitizeWebhookUrl_SecretDroppedByStructure_KeepsUsualOutput(string token, string url, string expected)
        {
            var previous = Environment.GetEnvironmentVariable("TOKEN");
            try
            {
                Environment.SetEnvironmentVariable("TOKEN", token);

                Assert.Equal(expected, LogRedaction.SanitizeWebhookUrl(url));
            }
            finally
            {
                Environment.SetEnvironmentVariable("TOKEN", previous);
            }
        }

        // An env secret that overlaps a provider's host, scheme or anchor segment must not push
        // the URL into the generic branch, which would log the provider token in full.
        [Theory]
        [InlineData("e", "https://hooks.slack.com/services/T0001/B0002/slacktokenvalue", "https://hooks.slack.com/<redacted>")]
        [InlineData("services", "https://hooks.slack.com/services/T0001/B0002/slacktokenvalue", "https://hooks.slack.com/<redacted>")]
        [InlineData("hooks", "https://discord.com/api/webhooks/123456/discordtokenvalue", "https://discord.com/<redacted>")]
        [InlineData("discord.com", "https://discord.com/api/webhooks/123456/discordtokenvalue", "https://<redacted>/api/webhooks/<redacted>/<redacted>")]
        [InlineData("bot", "https://api.telegram.org/bot123456:telegramtokenvalue/sendMessage", "https://api.telegram.org/<redacted>")]
        [InlineData("api", "https://api.telegram.org/bot123456:telegramtokenvalue/sendMessage", "https://<redacted>.telegram.org/bot<redacted>/sendMessage")]
        [InlineData("telegram", "https://api.telegram.org/bot123456:telegramtokenvalue/sendMessage", "https://api.<redacted>.org/bot<redacted>/sendMessage")]
        [InlineData("pushbullet", "pushbullet://pbtokenvalue", "<redacted>://<redacted>")]
        public void SanitizeWebhookUrl_SecretOverlapsProviderAnchor_ProviderTokenStaysMasked(string token, string url, string expected)
        {
            var previous = Environment.GetEnvironmentVariable("TOKEN");
            try
            {
                Environment.SetEnvironmentVariable("TOKEN", token);

                Assert.Equal(expected, LogRedaction.SanitizeWebhookUrl(url));
            }
            finally
            {
                Environment.SetEnvironmentVariable("TOKEN", previous);
            }
        }

        // One case per masking step: a secret only Uri decoding reveals (%7E), a secret in the
        // host or scheme (which Uri lowercases), and overlapping or nested secrets.
        [Theory]
        [InlineData("tok~en", null, "https://example.com/hooks/tok%7Een", "https://example.com/hooks/<redacted>")]
        [InlineData("secrethost", null, "https://secrethost.example.com/hooks", "https://<redacted>.example.com/hooks")]
        [InlineData("https", null, "https://example.com/hooks", "<redacted>://example.com/hooks")]
        [InlineData("ab", "bcdefghijk", "https://example.com/hooks/abcdefghijk", "https://example.com/hooks/<redacted>")]
        [InlineData("secretvalue", "value", "https://example.com/hooks/secretvalue", "https://example.com/hooks/<redacted>")]
        public void SanitizeWebhookUrl_SecretInDecodedFormHostSchemeOrOverlap_IsMaskedWhole(string token, string? secret, string url, string expected)
        {
            var previousToken = Environment.GetEnvironmentVariable("TOKEN");
            var previousSecret = Environment.GetEnvironmentVariable("SECRET");
            try
            {
                Environment.SetEnvironmentVariable("TOKEN", token);
                Environment.SetEnvironmentVariable("SECRET", secret);

                Assert.Equal(expected, LogRedaction.SanitizeWebhookUrl(url));
            }
            finally
            {
                Environment.SetEnvironmentVariable("TOKEN", previousToken);
                Environment.SetEnvironmentVariable("SECRET", previousSecret);
            }
        }
    }
}
