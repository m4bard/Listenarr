using System.Text.Json;
using Listenarr.Api.Services;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class PathMetadataParserTests
    {
        [Fact]
        public void ParseEmbeddedTagsFromFfprobeJson_ParsesStandardAsinTag()
        {
            var doc = JsonDocument.Parse("""
            {
              "format": {
                "tags": {
                  "album": "Alchemised",
                  "album_artist": "SenLinYu",
                  "ASIN": "B0DQR9D4YG"
                }
              }
            }
            """);

            var result = PathMetadataParser.ParseEmbeddedTagsFromFfprobeJson(doc.RootElement);

            Assert.Equal("Alchemised", result.Title);
            Assert.Equal("SenLinYu", result.Author);
            Assert.Equal("B0DQR9D4YG", result.Asin);
        }

        [Fact]
        public void ParseEmbeddedTagsFromFfprobeJson_ParsesMp3UserTextAsinTag()
        {
            var doc = JsonDocument.Parse("""
            {
              "format": {
                "tags": {
                  "album": "Alchemised",
                  "TXXX:ASIN": "B0DQR9D4YG"
                }
              }
            }
            """);

            var result = PathMetadataParser.ParseEmbeddedTagsFromFfprobeJson(doc.RootElement);

            Assert.Equal("B0DQR9D4YG", result.Asin);
        }

        [Fact]
        public void ParseEmbeddedTagsFromFfprobeJson_ParsesAppleFreeformAsinTag()
        {
            var doc = JsonDocument.Parse("""
            {
              "format": {
                "tags": {
                  "album": "Alchemised",
                  "----:com.apple.iTunes:ASIN": "amazon://asin/B0DQR9D4YG"
                }
              }
            }
            """);

            var result = PathMetadataParser.ParseEmbeddedTagsFromFfprobeJson(doc.RootElement);

            Assert.Equal("B0DQR9D4YG", result.Asin);
        }

        [Fact]
        public void ParseEmbeddedTagsFromFfprobeJson_ParsesColonSuffixedAsinTag()
        {
            var doc = JsonDocument.Parse("""
            {
              "format": {
                "tags": {
                  "ASIN:": "B0DQR9D4YG"
                }
              }
            }
            """);

            var result = PathMetadataParser.ParseEmbeddedTagsFromFfprobeJson(doc.RootElement);

            Assert.Equal("B0DQR9D4YG", result.Asin);
        }

        [Fact]
        public void ParseEmbeddedTagsFromFfprobeJson_ParsesCdekTagContainingAsin()
        {
            var doc = JsonDocument.Parse("""
            {
              "format": {
                "tags": {
                  "CDEK:": "amazon://asin/B0DQR9D4YG"
                }
              }
            }
            """);

            var result = PathMetadataParser.ParseEmbeddedTagsFromFfprobeJson(doc.RootElement);

            Assert.Equal("B0DQR9D4YG", result.Asin);
        }
    }
}
