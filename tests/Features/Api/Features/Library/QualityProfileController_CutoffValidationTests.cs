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
using System.Text;
using System.Text.Json;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks;

namespace Listenarr.Tests.Features.Api.Features.Library
{
    /// <summary>
    /// The cutoff rule has to refuse the three bad shapes at the API, accept a good one, and
    /// leave reads alone. The last part matters more in Listenarr than it does in Readarr:
    /// QualityProfileService.GetAllAsync, GetByIdAsync and GetDefaultAsync all write back
    /// through EnsureProfileHasRequiredQualitiesAsync, so a rule in the service or repository
    /// write path would have turned a GET of an existing bad profile into a failure.
    /// </summary>
    [Trait("Name", "QualityProfileController_CutoffValidationTests")]
    [Trait("Category", "Api")]
    public sealed class QualityProfileController_CutoffValidationTests
        : BaseTests, IClassFixture<ListenarrWebApplicationFactory>
    {
        private const string CutoffMessage = "Cutoff must be an allowed quality or group";

        private readonly ListenarrWebApplicationFactory _factory;

        public QualityProfileController_CutoffValidationTests(ListenarrWebApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Create_WithACutoffNamingAnAllowedQuality_IsAccepted()
        {
            using var client = _factory.CreateClient();
            var csrfToken = await GetAntiforgeryTokenAsync(client);

            using var response = await PostProfileAsync(
                client,
                csrfToken,
                BuildProfilePayload("Cutoff control: accepted", cutoffQuality: "AAC 256kbps"));

            var body = await response.Content.ReadAsStringAsync();

            Assert.True(
                response.StatusCode == HttpStatusCode.Created,
                $"Expected 201 for a valid cutoff, got {(int)response.StatusCode}: {body}");
            Assert.DoesNotContain(CutoffMessage, body, StringComparison.Ordinal);

            using var created = JsonDocument.Parse(body);
            Assert.Equal(
                "AAC 256kbps",
                created.RootElement.GetProperty("cutoffQuality").GetString());
        }

        [Fact]
        public async Task Create_WithABlankCutoff_IsRefused()
        {
            using var client = _factory.CreateClient();
            var csrfToken = await GetAntiforgeryTokenAsync(client);

            using var nullCutoff = await PostProfileAsync(
                client,
                csrfToken,
                BuildProfilePayload("Cutoff refused: null", cutoffQuality: null));
            using var emptyCutoff = await PostProfileAsync(
                client,
                csrfToken,
                BuildProfilePayload("Cutoff refused: empty", cutoffQuality: string.Empty));

            await AssertRefusedForCutoffAsync(nullCutoff);
            await AssertRefusedForCutoffAsync(emptyCutoff);
        }

        [Fact]
        public async Task Create_WithACutoffTheProfileDoesNotCarry_IsRefused()
        {
            using var client = _factory.CreateClient();
            var csrfToken = await GetAntiforgeryTokenAsync(client);

            using var response = await PostProfileAsync(
                client,
                csrfToken,
                BuildProfilePayload("Cutoff refused: off the ladder", cutoffQuality: "OPUS 96kbps"));

            await AssertRefusedForCutoffAsync(response);
        }

        [Fact]
        public async Task Create_WithACutoffOnAPresentButDisallowedQuality_IsRefused()
        {
            using var client = _factory.CreateClient();
            var csrfToken = await GetAntiforgeryTokenAsync(client);

            using var response = await PostProfileAsync(
                client,
                csrfToken,
                BuildProfilePayload(
                    "Cutoff refused: disabled rung",
                    cutoffQuality: "MP3 128kbps",
                    disallow: "MP3 128kbps"));

            await AssertRefusedForCutoffAsync(response);
        }

        [Fact]
        public async Task Update_CannotMoveAGoodProfileOntoADisallowedCutoff()
        {
            using var client = _factory.CreateClient();
            var csrfToken = await GetAntiforgeryTokenAsync(client);

            using var createResponse = await PostProfileAsync(
                client,
                csrfToken,
                BuildProfilePayload("Cutoff refused on update", cutoffQuality: "AAC 256kbps"));
            var createdBody = await createResponse.Content.ReadAsStringAsync();
            Assert.True(
                createResponse.StatusCode == HttpStatusCode.Created,
                $"Setup expected 201, got {(int)createResponse.StatusCode}: {createdBody}");

            using var created = JsonDocument.Parse(createdBody);
            var id = created.RootElement.GetProperty("id").GetInt32();

            var payload = BuildProfilePayload(
                "Cutoff refused on update",
                cutoffQuality: "MP3 128kbps",
                disallow: "MP3 128kbps");
            payload["id"] = id;

            using var updateResponse = await SendProfileAsync(
                client,
                csrfToken,
                HttpMethod.Put,
                $"{ProfilesRoute}/{id}",
                payload);
            await AssertRefusedForCutoffAsync(updateResponse);

            // The stored profile is untouched by the refused update.
            using var reread = await client.GetAsync($"{ProfilesRoute}/{id}");
            using var rereadBody = JsonDocument.Parse(await reread.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, reread.StatusCode);
            Assert.Equal(
                "AAC 256kbps",
                rereadBody.RootElement.GetProperty("cutoffQuality").GetString());
        }

        [Fact]
        public async Task AlreadyStoredProfileWithABlankCutoff_StillLoadsAndIsReturned()
        {
            // Written straight to the database, the way an install upgrading into this rule
            // already holds it. This is also the control for the refusals above: the same shape
            // the API now rejects is still perfectly storable underneath it.
            var id = await StoreProfileDirectlyAsync(
                "Stored blank cutoff",
                cutoffQuality: string.Empty,
                isDefault: true);

            using var client = _factory.CreateClient();

            using var byId = await client.GetAsync($"{ProfilesRoute}/{id}");
            var byIdBody = await byId.Content.ReadAsStringAsync();
            Assert.True(
                byId.StatusCode == HttpStatusCode.OK,
                $"Expected 200 reading a stored bad profile, got {(int)byId.StatusCode}: {byIdBody}");

            using var parsed = JsonDocument.Parse(byIdBody);
            Assert.Equal(string.Empty, parsed.RootElement.GetProperty("cutoffQuality").GetString());
            Assert.Equal("Stored blank cutoff", parsed.RootElement.GetProperty("name").GetString());

            // GetAllAsync and GetDefaultAsync both write back through
            // EnsureProfileHasRequiredQualitiesAsync for a default profile; neither may fail.
            using var all = await client.GetAsync(ProfilesRoute);
            Assert.Equal(HttpStatusCode.OK, all.StatusCode);
            using var allBody = JsonDocument.Parse(await all.Content.ReadAsStringAsync());
            Assert.Contains(
                allBody.RootElement.EnumerateArray(),
                profile => profile.GetProperty("id").GetInt32() == id);

            using var byDefault = await client.GetAsync($"{ProfilesRoute}/default");
            Assert.Equal(HttpStatusCode.OK, byDefault.StatusCode);
        }

        [Fact]
        public async Task AlreadyStoredProfileWithADisallowedCutoff_StillLoadsAndIsReturned()
        {
            var id = await StoreProfileDirectlyAsync(
                "Stored disallowed cutoff",
                cutoffQuality: "MP3 128kbps",
                isDefault: false,
                disallow: "MP3 128kbps");

            using var client = _factory.CreateClient();

            using var byId = await client.GetAsync($"{ProfilesRoute}/{id}");
            var byIdBody = await byId.Content.ReadAsStringAsync();
            Assert.True(
                byId.StatusCode == HttpStatusCode.OK,
                $"Expected 200 reading a stored bad profile, got {(int)byId.StatusCode}: {byIdBody}");

            using var parsed = JsonDocument.Parse(byIdBody);
            Assert.Equal("MP3 128kbps", parsed.RootElement.GetProperty("cutoffQuality").GetString());
        }

        private string ProfilesRoute => $"{TestUtils.ResolveApiBasePath(_factory.Services)}/qualityprofile";

        private Task<HttpResponseMessage> PostProfileAsync(
            HttpClient client,
            string csrfToken,
            Dictionary<string, object?> payload) =>
            SendProfileAsync(client, csrfToken, HttpMethod.Post, ProfilesRoute, payload);

        private static async Task<HttpResponseMessage> SendProfileAsync(
            HttpClient client,
            string csrfToken,
            HttpMethod method,
            string route,
            Dictionary<string, object?> payload)
        {
            using var request = new HttpRequestMessage(method, route)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json")
            };
            request.Headers.Add("X-XSRF-TOKEN", csrfToken);
            return await client.SendAsync(request);
        }

        private string AntiforgeryRoute =>
            $"{TestUtils.ResolveApiBasePath(_factory.Services)}/antiforgery/token";

        private async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
        {
            using var response = await client.GetAsync(AntiforgeryRoute);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var token = json.RootElement.GetProperty("token").GetString();
            Assert.False(string.IsNullOrWhiteSpace(token));
            return token!;
        }

        private async Task<int> StoreProfileDirectlyAsync(
            string name,
            string? cutoffQuality,
            bool isDefault,
            string? disallow = null)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ListenArrDbContext>();

            var profile = new QualityProfile
            {
                Name = name,
                CutoffQuality = cutoffQuality,
                IsDefault = isDefault,
                Qualities = BuildLadder(disallow)
            };

            db.QualityProfiles.Add(profile);
            await db.SaveChangesAsync();
            return profile.Id;
        }

        private static List<QualityDefinition> BuildLadder(string? disallow)
        {
            var ladder = new (string Quality, string Codec, int? Bitrate)[]
            {
                ("AAC 320kbps", "AAC", 320),
                ("AAC 256kbps", "AAC", 256),
                ("MP3 320kbps", "MP3", 320),
                ("MP3 128kbps", "MP3", 128)
            };

            return ladder
                .Select((rung, index) => new QualityDefinition
                {
                    Quality = rung.Quality,
                    Codec = rung.Codec,
                    Bitrate = rung.Bitrate,
                    Priority = index,
                    Allowed = !string.Equals(rung.Quality, disallow, StringComparison.Ordinal)
                })
                .ToList();
        }

        private static Dictionary<string, object?> BuildProfilePayload(
            string name,
            string? cutoffQuality,
            string? disallow = null)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = name,
                ["cutoffQuality"] = cutoffQuality,
                ["isDefault"] = false,
                ["qualities"] = BuildLadder(disallow)
                    .Select(quality => new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["quality"] = quality.Quality,
                        ["allowed"] = quality.Allowed,
                        ["priority"] = quality.Priority,
                        ["codec"] = quality.Codec,
                        ["bitrate"] = quality.Bitrate,
                        ["isLossless"] = quality.IsLossless
                    })
                    .ToList()
            };
        }

        private static async Task AssertRefusedForCutoffAsync(HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(
                response.StatusCode == HttpStatusCode.BadRequest,
                $"Expected 400 for a bad cutoff, got {(int)response.StatusCode}: {body}");
            Assert.Contains(CutoffMessage, body, StringComparison.Ordinal);

            using var problem = JsonDocument.Parse(body);
            var errors = problem.RootElement.GetProperty("errors");
            var cutoffErrors = errors
                .EnumerateObject()
                .Where(property => property.Name.EndsWith(
                    "cutoffQuality",
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            var cutoffError = Assert.Single(cutoffErrors);
            Assert.Contains(
                cutoffError.Value.EnumerateArray(),
                message => string.Equals(message.GetString(), CutoffMessage, StringComparison.Ordinal));
        }
    }
}
