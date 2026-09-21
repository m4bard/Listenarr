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
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Api.Features.Indexers
{
    /// <summary>
    /// Drives the real request pipeline, because the rule under test is enforced by the
    /// <c>[ApiController]</c> model-state filter. Calling a controller action directly, as the
    /// older indexer tests do, skips model binding and would prove nothing.
    /// </summary>
    [Trait("Name", "IndexerCategoryRequirementTests")]
    [Trait("Category", "Api")]
    public sealed class IndexerCategoryRequirementTests : BaseTests, IClassFixture<ListenarrWebApplicationFactory>
    {
        private const string ApiBase = "/api/v1";

        private readonly ListenarrWebApplicationFactory _factory;

        public IndexerCategoryRequirementTests(ListenarrWebApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task Create_NewznabWithoutCategories_IsRefused()
        {
            using var client = _factory.CreateClient();

            var response = await PostIndexerAsync(client, BuildIndexerJson("Newznab", categories: ""));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(body);
            var errors = document.RootElement.GetProperty("errors");

            // The message must be attached to the Categories field, not to the form as a whole,
            // so the client can point at the input the user has to fix.
            Assert.True(
                errors.TryGetProperty("Categories", out var categoryErrors),
                $"Expected a Categories validation error. Body was: {body}");
            Assert.Contains(
                categoryErrors.EnumerateArray(),
                entry => (entry.GetString() ?? string.Empty).Contains(
                    "At least one category",
                    StringComparison.Ordinal));
        }

        [Fact]
        public async Task Create_NewznabWithCategories_IsAccepted()
        {
            // The control for the refusal above: the same request, differing only in the field
            // under test, must succeed. Without it a blanket rejection would look like a pass.
            using var client = _factory.CreateClient();

            var response = await PostIndexerAsync(client, BuildIndexerJson("Newznab", categories: "3030,3040"));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("3030,3040", document.RootElement.GetProperty("categories").GetString());
        }

        [Fact]
        public async Task Create_WithoutCategoriesField_AppliesTheAudiobookDefault()
        {
            using var client = _factory.CreateClient();

            var response = await PostIndexerAsync(client, BuildIndexerJson("Newznab", categories: null));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(
                IndexerCategorySelection.AudiobookDefault,
                document.RootElement.GetProperty("categories").GetString());
        }

        [Theory]
        [InlineData("InternetArchive")]
        [InlineData("MyAnonamouse")]
        public async Task Create_ImplementationThatIgnoresCategories_IsAcceptedWithoutThem(string implementation)
        {
            // These two never send a cat= parameter, so demanding categories would only make them
            // unsaveable. The indexer form hides the field for Internet Archive entirely.
            using var client = _factory.CreateClient();

            var response = await PostIndexerAsync(client, BuildIndexerJson(implementation, categories: ""));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        [Fact]
        public async Task Create_WithAnExplicitNullCategoryList_IsNotAWayPastTheRule()
        {
            // An explicit null binds the same as an omitted field, so it has to behave the same:
            // the default is applied rather than an unconstrained indexer being stored.
            using var client = _factory.CreateClient();

            var json = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["name"] = $"ExplicitNull {Guid.NewGuid():N}",
                ["type"] = "Usenet",
                ["implementation"] = "Newznab",
                ["url"] = "https://indexer.example.com",
                ["categories"] = null
            });

            var response = await PostIndexerAsync(client, json);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(
                IndexerCategorySelection.AudiobookDefault,
                document.RootElement.GetProperty("categories").GetString());
        }

        [Fact]
        public async Task Update_OmittingCategories_LeavesTheStoredListAlone()
        {
            // An omitted field means "not supplied", so it must not rewrite what is stored. The
            // hazard this guards is quiet: narrowing a list to the default looks like a success
            // and reads back as a plausible value.
            using var client = _factory.CreateClient();

            var created = await PostIndexerAsync(client, BuildIndexerJson("Newznab", "3030,3040,7020"));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            using var createdDocument = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            var id = createdDocument.RootElement.GetProperty("id").GetInt32();
            var name = createdDocument.RootElement.GetProperty("name").GetString();

            var updated = await PutIndexerAsync(client, id, BuildIndexerJson("Newznab", null, name));
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

            using var document = JsonDocument.Parse(await updated.Content.ReadAsStringAsync());
            Assert.Equal("3030,3040,7020", document.RootElement.GetProperty("categories").GetString());
        }

        [Fact]
        public async Task Update_SupplyingCategories_ReplacesTheStoredList()
        {
            // The apparatus check for the preservation tests either side of this one. Both of
            // those assert that a stored list survives, so an Update that never wrote categories
            // at all would satisfy them; this is the one that fails in that case.
            using var client = _factory.CreateClient();

            var created = await PostIndexerAsync(client, BuildIndexerJson("Newznab", "3030"));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            using var createdDocument = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            var id = createdDocument.RootElement.GetProperty("id").GetInt32();
            var name = createdDocument.RootElement.GetProperty("name").GetString();

            var updated = await PutIndexerAsync(client, id, BuildIndexerJson("Newznab", "3030,7020", name));
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

            using var document = JsonDocument.Parse(await updated.Content.ReadAsStringAsync());
            Assert.Equal("3030,7020", document.RootElement.GetProperty("categories").GetString());

            // Re-read, so this cannot pass on a response that merely echoed the request back.
            var reread = await client.GetAsync($"{ApiBase}/indexers/{id}");
            using var rereadDocument = JsonDocument.Parse(await reread.Content.ReadAsStringAsync());
            Assert.Equal("3030,7020", rereadDocument.RootElement.GetProperty("categories").GetString());
        }

        [Fact]
        public async Task Update_SwitchingOntoACategoryAwareImplementation_DoesNotCarryAnEmptyListAcross()
        {
            // The validator judges the request and Update merges it, so the two come apart here.
            // An Internet Archive indexer is exempt and may be stored with no categories; moving
            // it onto Newznab would otherwise take that empty list across, through the very
            // endpoint the rule guards.
            using var client = _factory.CreateClient();

            var created = await PostIndexerAsync(client, BuildIndexerJson("InternetArchive", ""));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            using var createdDocument = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            var id = createdDocument.RootElement.GetProperty("id").GetInt32();
            var name = createdDocument.RootElement.GetProperty("name").GetString();

            var updated = await PutIndexerAsync(client, id, BuildIndexerJson("Newznab", null, name));
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

            using var document = JsonDocument.Parse(await updated.Content.ReadAsStringAsync());
            Assert.Equal(
                IndexerCategorySelection.AudiobookDefault,
                document.RootElement.GetProperty("categories").GetString());
        }

        [Fact]
        public async Task Update_OmittingCategories_DoesNotBackfillAStoredNullList()
        {
            // The same rule seen from the legacy side. Quietly writing a default here would
            // repair a row the user never touched and report success while doing it.
            var id = await InsertLegacyCategorylessIndexerAsync();

            using var client = _factory.CreateClient();
            var response = await PutIndexerAsync(
                client,
                id,
                BuildIndexerJson("Newznab", null, "Legacy Categoryless"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            AssertCategoriesReadAsUnset(document.RootElement);
        }

        [Fact]
        public async Task TestDraft_WithoutCategories_IsRefusedBeforeTheIndexerIsContacted()
        {
            // The draft-test action binds the same entity, so the rule reaches it too. That
            // matches the family: Sonarr and Readarr pass forceValidate: true on Test, making it
            // the strictest path rather than a way around the validator.
            using var client = _factory.CreateClient();

            var response = await PostIndexerAsync(
                client,
                BuildIndexerJson("Newznab", categories: ""),
                path: "indexers/test");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(
                document.RootElement.TryGetProperty("errors", out var errors)
                    && errors.TryGetProperty("Categories", out _),
                "Expected the draft test to be refused by validation, not by the connection attempt.");
        }

        [Fact]
        public async Task StoredIndexerWithNullCategories_StillLoadsAndIsReadable()
        {
            // The row is written as raw SQL so the column really is NULL, the way it is on an
            // install that predates this rule. Going through the entity would apply the default
            // and quietly test nothing.
            var id = await InsertLegacyCategorylessIndexerAsync();

            using var client = _factory.CreateClient();

            var single = await client.GetAsync($"{ApiBase}/indexers/{id}");
            Assert.Equal(HttpStatusCode.OK, single.StatusCode);

            using var document = JsonDocument.Parse(await single.Content.ReadAsStringAsync());
            Assert.Equal("Legacy Categoryless", document.RootElement.GetProperty("name").GetString());
            AssertCategoriesReadAsUnset(document.RootElement);

            // It must survive the list endpoint too: one unreadable row there would blank the
            // whole indexer page rather than just its own entry.
            var all = await client.GetAsync($"{ApiBase}/indexers");
            Assert.Equal(HttpStatusCode.OK, all.StatusCode);

            using var listDocument = JsonDocument.Parse(await all.Content.ReadAsStringAsync());
            Assert.Contains(
                listDocument.RootElement.EnumerateArray(),
                entry => entry.GetProperty("id").GetInt32() == id);
        }

        [Fact]
        public async Task StoredIndexerWithNullCategories_IsRefusedOnlyWhenSavedAgain()
        {
            var id = await InsertLegacyCategorylessIndexerAsync();

            using var client = _factory.CreateClient();
            var token = await GetAntiforgeryTokenAsync(client);

            using var request = new HttpRequestMessage(HttpMethod.Put, $"{ApiBase}/indexers/{id}")
            {
                Content = new StringContent(
                    BuildIndexerJson("Newznab", categories: "", name: "Legacy Categoryless"),
                    Encoding.UTF8,
                    "application/json")
            };
            request.Headers.Add("X-XSRF-TOKEN", token);

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            // Refusing the write must not have disturbed the stored row.
            var reread = await client.GetAsync($"{ApiBase}/indexers/{id}");
            using var document = JsonDocument.Parse(await reread.Content.ReadAsStringAsync());
            AssertCategoriesReadAsUnset(document.RootElement);
        }

        /// <summary>
        /// The API serializes with <c>WhenWritingNull</c>, so an unset category list reaches the
        /// client as an absent property rather than a null one.
        /// </summary>
        private static void AssertCategoriesReadAsUnset(JsonElement indexer)
        {
            if (indexer.TryGetProperty("categories", out var categories))
            {
                Assert.Equal(JsonValueKind.Null, categories.ValueKind);
            }
        }

        private async Task<int> InsertLegacyCategorylessIndexerAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var contextFactory = scope.ServiceProvider
                .GetRequiredService<IDbContextFactory<ListenArrDbContext>>();
            await using var context = await contextFactory.CreateDbContextAsync();

            var name = "Legacy Categoryless";
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO Indexers
                    (Name, Type, Implementation, Url, Categories, EnableRss, EnableAutomaticSearch,
                     EnableInteractiveSearch, EnableAnimeStandardSearch, IsEnabled, Priority,
                     MinimumAge, Retention, MaximumSize, CreatedAt, UpdatedAt)
                VALUES
                    ({0}, 'Usenet', 'Newznab', 'https://legacy.example.com', NULL, 1, 1, 1, 0, 1, 25,
                     0, 0, 0, '2026-01-01 00:00:00', '2026-01-01 00:00:00')
                """,
                name);

            var stored = await context.Indexers
                .AsNoTracking()
                .Where(indexer => indexer.Name == name)
                .OrderByDescending(indexer => indexer.Id)
                .FirstAsync();

            // Guards the apparatus: if the insert had written a value, every assertion about the
            // legacy read path would pass for the wrong reason.
            Assert.Null(stored.Categories);
            return stored.Id;
        }

        private async Task<HttpResponseMessage> PostIndexerAsync(
            HttpClient client,
            string json,
            string path = "indexers")
        {
            var token = await GetAntiforgeryTokenAsync(client);

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/{path}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-XSRF-TOKEN", token);

            return await client.SendAsync(request);
        }

        private async Task<HttpResponseMessage> PutIndexerAsync(HttpClient client, int id, string json)
        {
            var token = await GetAntiforgeryTokenAsync(client);

            using var request = new HttpRequestMessage(HttpMethod.Put, $"{ApiBase}/indexers/{id}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-XSRF-TOKEN", token);

            return await client.SendAsync(request);
        }

        private static string BuildIndexerJson(string implementation, string? categories, string? name = null)
        {
            var payload = new Dictionary<string, object?>
            {
                ["name"] = name ?? $"{implementation} {Guid.NewGuid():N}",
                ["type"] = "Usenet",
                ["implementation"] = implementation,
                ["url"] = "https://indexer.example.com",
                ["apiKey"] = "test-key",
                ["isEnabled"] = true
            };

            // A null here means "omit the field", which is the case that must pick up the default.
            if (categories != null)
            {
                payload["categories"] = categories;
            }

            return JsonSerializer.Serialize(payload);
        }

        /// <summary>
        /// Fetches a CSRF token. The test client carries its own cookie container, so the paired
        /// antiforgery cookie travels on its own and is only issued on the first call.
        /// </summary>
        private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
        {
            var response = await client.GetAsync($"{ApiBase}/antiforgery/token");
            response.EnsureSuccessStatusCode();

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var token = json.RootElement.GetProperty("token").GetString();
            Assert.False(string.IsNullOrWhiteSpace(token));

            return token!;
        }
    }
}
