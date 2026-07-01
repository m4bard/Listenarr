using System.Text.Json;
using Listenarr.Infrastructure.DownloadClients.Common;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.DownloadClients.Common
{
    [Trait("Name", "TorrentClientPathMapperTests")]
    [Trait("Category", "DownloadClientPathMapping")]
    public class TorrentClientPathMapperTests : BaseTests
    {
        [Fact]
        public void BuildTransmissionSourceFiles_PreservesTopLevelFolderWhitespace()
        {
            var downloadDir = FileUtils.GetAbsolutePath("downloads");
            using var document = JsonDocument.Parse(
                """
                [
                  { "name": " Book Folder /chapter1.m4b" }
                ]
                """);

            var sourceFiles = TorrentClientPathMapper.BuildTransmissionSourceFiles(downloadDir, document.RootElement);

            var expected = FileUtils.CombineWithOptionalBase(downloadDir, " Book Folder /chapter1.m4b");
            Assert.Equal([expected], sourceFiles);
            Assert.StartsWith(downloadDir + Path.DirectorySeparatorChar, expected, StringComparison.Ordinal);
            Assert.Contains(" Book Folder ", expected, StringComparison.Ordinal);
        }

        [Fact]
        public void BuildTransmissionSourceFiles_PreservesTrailingFolderWhitespace()
        {
            var downloadDir = FileUtils.GetAbsolutePath("downloads");
            using var document = JsonDocument.Parse(
                """
                [
                  { "name": "Book Folder /chapter1.m4b" }
                ]
                """);

            var sourceFiles = TorrentClientPathMapper.BuildTransmissionSourceFiles(downloadDir, document.RootElement);

            var expected = FileUtils.CombineWithOptionalBase(downloadDir, "Book Folder /chapter1.m4b");
            Assert.Equal([expected], sourceFiles);
            Assert.StartsWith(downloadDir + Path.DirectorySeparatorChar, expected, StringComparison.Ordinal);
            Assert.Contains("Book Folder ", expected, StringComparison.Ordinal);
        }

        [Fact]
        public void BuildQbittorrentSourceFiles_PreservesTorrentFolderWhitespace()
        {
            var savePath = FileUtils.GetAbsolutePath("downloads");
            var files = ParseFiles(
                """
                [
                  { "name": " Book Folder /chapter1.m4b" }
                ]
                """);

            var sourceFiles = TorrentClientPathMapper.BuildQbittorrentSourceFiles(savePath, files);

            var expected = Path.Join(savePath, " Book Folder ", "chapter1.m4b");
            Assert.Equal([expected], sourceFiles);
        }

        [Fact]
        public void ResolveQbittorrentContentPath_PreservesSharedTopLevelFolderWhitespace()
        {
            var savePath = FileUtils.GetAbsolutePath("downloads");
            var files = ParseFiles(
                """
                [
                  { "name": " Book Folder /chapter1.m4b" },
                  { "name": " Book Folder /chapter2.m4b" }
                ]
                """);

            var contentPath = TorrentClientPathMapper.ResolveQbittorrentContentPath(savePath, files);

            Assert.Equal(Path.Join(savePath, " Book Folder "), contentPath);
        }

        [Fact]
        public void BuildQbittorrentSourceFiles_RootedChildPathsStayUnderSavePath()
        {
            var savePath = FileUtils.GetAbsolutePath("downloads");
            var files = ParseFiles(
                """
                [
                  { "name": "/ Book Folder /chapter1.m4b" }
                ]
                """);

            var sourceFile = Assert.Single(TorrentClientPathMapper.BuildQbittorrentSourceFiles(savePath, files));

            Assert.Equal(Path.Join(savePath, " Book Folder ", "chapter1.m4b"), sourceFile);
            Assert.True(FileUtils.IsPathSameOrInside(sourceFile, savePath));
        }

        private static List<Dictionary<string, JsonElement>> ParseFiles(string json)
        {
            using var document = JsonDocument.Parse(json);
            var files = new List<Dictionary<string, JsonElement>>();

            foreach (var element in document.RootElement.EnumerateArray())
            {
                var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in element.EnumerateObject())
                {
                    map[property.Name] = property.Value.Clone();
                }

                files.Add(map);
            }

            return files;
        }
    }
}
