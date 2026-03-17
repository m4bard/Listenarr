using System.IO;
using Listenarr.Api.Services;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class UnmatchedScanBackgroundServiceTests
    {
        [Fact]
        public void BuildGroupedFilesForFolder_MergesForewordIntoSingleBookGroup()
        {
            var folder = @"D:\test\Jack of Shadows - Roger Zelazny (narrated by Eric Jason Martin)";
            var files = new[]
            {
                Path.Combine(folder, "(Foreword by Joe Haldeman).mp3"),
                Path.Combine(folder, "Chapter 01.mp3"),
                Path.Combine(folder, "Chapter 02.mp3")
            };

            var groups = UnmatchedScanBackgroundService.BuildGroupedFilesForFolder(files, folder);

            var group = Assert.Single(groups);
            Assert.Equal(3, group.Count);
            Assert.Contains(Path.Combine(folder, "(Foreword by Joe Haldeman).mp3"), group);
            Assert.Contains(Path.Combine(folder, "Chapter 01.mp3"), group);
            Assert.Contains(Path.Combine(folder, "Chapter 02.mp3"), group);
        }

        [Fact]
        public void BuildGroupedFilesForFolder_KeepsDistinctTitlesSeparated()
        {
            var folder = @"D:\test\Roger Zelazny";
            var files = new[]
            {
                Path.Combine(folder, "Jack of Shadows.mp3"),
                Path.Combine(folder, "Lord of Light.mp3")
            };

            var groups = UnmatchedScanBackgroundService.BuildGroupedFilesForFolder(files, folder);

            Assert.Equal(2, groups.Count);
            Assert.Contains(groups, group => group.Single() == Path.Combine(folder, "Jack of Shadows.mp3"));
            Assert.Contains(groups, group => group.Single() == Path.Combine(folder, "Lord of Light.mp3"));
        }
    }
}
