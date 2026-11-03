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
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Listenarr.Tests.Mocks;

namespace Listenarr.Tests.Features.Api
{
    public class StartupConfigurationLockoutGuardTests : IClassFixture<ListenarrWebApplicationFactory>
    {
        private readonly ListenarrWebApplicationFactory _factory;

        public StartupConfigurationLockoutGuardTests(ListenarrWebApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task SaveStartupConfig_ReturnsBadRequest_WhenEnablingLoginWithoutAdminUser()
        {
            // The settings screen only offers the config.json download fallback for
            // non-4xx failures, so a refusal by the lockout guard must not be a 500.
            using var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IStartupConfigService>(_ =>
                        new StartupConfigServiceMock(new StartupConfig { AuthenticationRequired = "false" }));
                });
            });
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
            var csrfToken = await GetAntiforgeryTokenAsync(client);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/configuration/startupconfig")
            {
                Content = new StringContent("""{ "authenticationRequired": "true" }""", Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-XSRF-TOKEN", csrfToken);

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Cannot enable the login screen", body);
        }

        private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
        {
            var tokenResponse = await client.GetAsync("/api/v1/antiforgery/token");
            tokenResponse.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
            var token = json.RootElement.GetProperty("token").GetString();
            Assert.False(string.IsNullOrWhiteSpace(token));
            return token!;
        }
    }
}
