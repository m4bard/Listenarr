using System.Collections.Generic;
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
                Path.Join(folder, "(Foreword by Joe Haldeman).mp3"),
                Path.Join(folder, "Chapter 01.mp3"),
                Path.Join(folder, "Chapter 02.mp3")
            };

            var groups = UnmatchedScanBackgroundService.BuildGroupedFilesForFolder(files, folder);

            var group = Assert.Single(groups);
            Assert.Equal(3, group.Count);
            Assert.Contains(Path.Join(folder, "(Foreword by Joe Haldeman).mp3"), group);
            Assert.Contains(Path.Join(folder, "Chapter 01.mp3"), group);
            Assert.Contains(Path.Join(folder, "Chapter 02.mp3"), group);
        }

        [Fact]
        public void BuildGroupedFilesForFolder_KeepsDistinctTitlesSeparated()
        {
            var folder = @"D:\test\Roger Zelazny";
            var files = new[]
            {
                Path.Join(folder, "Jack of Shadows.mp3"),
                Path.Join(folder, "Lord of Light.mp3")
            };

            var groups = UnmatchedScanBackgroundService.BuildGroupedFilesForFolder(files, folder);

            Assert.Equal(2, groups.Count);
            Assert.Contains(groups, group => group.Single() == Path.Join(folder, "Jack of Shadows.mp3"));
            Assert.Contains(groups, group => group.Single() == Path.Join(folder, "Lord of Light.mp3"));
        }

        [Fact]
        public void BuildGroupedFilesForFolder_UsesEmbeddedTitleAndAuthorToMergeMixedFolderTracks()
        {
            var folder = @"D:\test\test-import";
            var foreword = Path.Join(folder, "(Foreword by Joe Haldeman).mp3");
            var chapter1 = Path.Join(folder, "Chapter 01.mp3");
            var alchemised = Path.Join(folder, "Alchemised (Spanish Edition)_ No queda nadie a quien salvar.m4b");
            var files = new[]
            {
                foreword,
                chapter1,
                alchemised
            };

            var embeddedTags = new Dictionary<string, PathParsedMetadata>(StringComparer.OrdinalIgnoreCase)
            {
                [foreword] = new() { Title = "Jack of Shadows", Author = "Roger Zelazny" },
                [chapter1] = new() { Title = "Jack of Shadows", Author = "Roger Zelazny" },
                [alchemised] = new() { Title = "Alchemised (Spanish Edition)", Author = "SenLinYu" }
            };

            var groups = UnmatchedScanBackgroundService.BuildGroupedFilesForFolder(files, folder, embeddedTags);

            Assert.Equal(2, groups.Count);
            Assert.Contains(groups, group => group.Count == 2 && group.Contains(foreword) && group.Contains(chapter1));
            Assert.Contains(groups, group => group.Count == 1 && group.Contains(alchemised));
        }

        [Fact]
        public void BuildGroupedFilesForFolder_UsesAuthorToKeepSameTitleSeparated()
        {
            var folder = @"D:\test\same-title";
            var fileA = Path.Join(folder, "Book A.m4b");
            var fileB = Path.Join(folder, "Book B.m4b");
            var files = new[] { fileA, fileB };

            var embeddedTags = new Dictionary<string, PathParsedMetadata>(StringComparer.OrdinalIgnoreCase)
            {
                [fileA] = new() { Title = "Shared Title", Author = "Roger Zelazny" },
                [fileB] = new() { Title = "Shared Title", Author = "SenLinYu" }
            };

            var groups = UnmatchedScanBackgroundService.BuildGroupedFilesForFolder(files, folder, embeddedTags);

            Assert.Equal(2, groups.Count);
            Assert.Contains(groups, group => group.Single() == fileA);
            Assert.Contains(groups, group => group.Single() == fileB);
        }
    }
}
