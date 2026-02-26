using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class LibraryController_IdentifierDeduplicationTests : IClassFixture<ListenarrWebApplicationFactory>
    {
        private readonly ListenarrWebApplicationFactory _factory;

        public LibraryController_IdentifierDeduplicationTests(ListenarrWebApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task GetAudiobookIdentifiers_SuppressesImportedLegacyDuplicate_WhenManualAsinExistsWithRegion()
        {
            int audiobookId;

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ListenArrDbContext>();

                var audiobook = new Audiobook
                {
                    Title = "Identifier Dedup Test",
                    Monitored = true,
                    // Legacy primary ASIN mirrors the manual identifier but loses region.
                    Asin = "B0DQR9D4YG",
                    ExternalIdentifiers = new List<AudiobookExternalIdentifier>
                    {
                        new AudiobookExternalIdentifier
                        {
                            Type = AudiobookExternalIdentifierType.Asin,
                            ValueRaw = "B0DQR9D4YG",
                            ValueNormalized = "B0DQR9D4YG",
                            Region = "us",
                            IsPrimary = true,
                            Source = AudiobookExternalIdentifierSource.Manual
                        },
                        new AudiobookExternalIdentifier
                        {
                            Type = AudiobookExternalIdentifierType.Asin,
                            ValueRaw = "B0DQR9D4YG",
                            ValueNormalized = "B0DQR9D4YG",
                            Region = null,
                            IsPrimary = true,
                            Source = AudiobookExternalIdentifierSource.Imported
                        },
                        new AudiobookExternalIdentifier
                        {
                            Type = AudiobookExternalIdentifierType.Asin,
                            ValueRaw = "B0DQR5KHHF",
                            ValueNormalized = "B0DQR5KHHF",
                            Region = null,
                            IsPrimary = false,
                            Source = AudiobookExternalIdentifierSource.Imported
                        }
                    }
                };

                db.Audiobooks.Add(audiobook);
                await db.SaveChangesAsync();
                audiobookId = audiobook.Id;
            }

            var client = _factory.CreateClient();
            var response = await client.GetAsync($"/api/library/{audiobookId}/identifiers");
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var json = await JsonDocument.ParseAsync(stream);

            var identifiers = json.RootElement.GetProperty("identifiers").EnumerateArray().ToList();
            var duplicateCandidates = identifiers
                .Where(i =>
                    JsonPropertyStringEquals(i, "type", "Asin") &&
                    JsonPropertyStringEquals(i, "valueNormalized", "B0DQR9D4YG"))
                .ToList();

            Assert.Single(duplicateCandidates);
            Assert.True(JsonPropertyStringEquals(duplicateCandidates[0], "source", "Manual"));
            Assert.True(JsonPropertyStringEquals(duplicateCandidates[0], "region", "us"));

            // Distinct imported alternate ASIN should still be present.
            Assert.Contains(identifiers, i =>
                JsonPropertyStringEquals(i, "type", "Asin") &&
                JsonPropertyStringEquals(i, "valueNormalized", "B0DQR5KHHF"));
        }

        private static bool JsonPropertyStringEquals(JsonElement element, string propertyName, string expected)
        {
            if (!element.TryGetProperty(propertyName, out var property)) return false;
            if (property.ValueKind == JsonValueKind.Null) return false;
            var actual = property.ToString();
            return string.Equals(actual, expected, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
