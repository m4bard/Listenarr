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
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Api.Features.Library
{
    [Trait("Area", "LibraryApi")]
    [Trait("Name", "LibraryController_BasePathTests")]
    [Trait("Category", "LibraryController")]
    public class LibraryController_BasePathTests : BaseTests
    {
        private const string RootPath = "/server/mnt/drive/Audiobooks";
        private const string FileNamingPattern = "{Author}/{Series}/{Title}";

        [Theory]
        [InlineData(FileSystemCaseSensitivity.Sensitive, false)]
        [InlineData(FileSystemCaseSensitivity.Insensitive, true)]
        [Trait("Method", "CalculateBasePath")]
        [Trait("Scenario", "CaseOnlyDirectoriesUseResolvedSemantics")]
        public void CalculateBasePath_CaseOnlyDirectoriesUseResolvedSemantics(
            FileSystemCaseSensitivity caseSensitivity,
            bool expectCaseFoldedDirectory)
        {
            var root = FileService.GetTempDirectory("library-path-planner");
            var first = Path.Join(root, "CaseBook", "one.m4b");
            var second = Path.Join(root, "casebook", "two.m4b");
            var semantics = new FileSystemPathSemantics(
                FileSystemPathSemantics.CurrentHostDefault.Syntax,
                caseSensitivity);

            var result = LibraryPathPlanner.CalculateBasePath(
                [first, second],
                _provider.GetRequiredService<IFileSystem>(),
                _provider.GetRequiredService<ILogger<LibraryController_BasePathTests>>(),
                semantics);

            if (expectCaseFoldedDirectory)
            {
                Assert.Equal(Path.Join(root, "CaseBook"), result);
            }
            else
            {
                Assert.NotEqual(Path.Join(root, "CaseBook"), result);
            }
        }

        [Fact]
        [Trait("Method", "ComputeAudiobookBaseDirectoryFromPattern")]
        [Trait("Scenario", "NonSeriesBook_ReturnsCorrectPath")]
        public void ComputeAudiobookBaseDirectoryFromPattern_NonSeriesBook_ReturnsCorrectPath()
        {
            // Given
            var audiobook = new AudiobookBuilder()
                .WithTitle("The Buffalo Hunter Hunter")
                .WithAuthor("Stephen Graham Jones")
                .WithYear("2025")
                .Build();

            var fileNamingService = _provider.GetRequiredService<IFileNamingService>();

            // When
            var result = LibraryPathPlanner.ComputeAudiobookBaseDirectoryFromPattern(audiobook, RootPath, FileNamingPattern, fileNamingService);

            // Then
            Assert.Equal(Path.Join(RootPath, "Stephen Graham Jones", "The Buffalo Hunter Hunter"), result);
        }

        [Fact]
        [Trait("Method", "ComputeAudiobookBaseDirectoryFromPattern")]
        [Trait("Scenario", "SeriesBook_ReturnsCorrectPath")]
        public void ComputeAudiobookBaseDirectoryFromPattern_SeriesBook_ReturnsCorrectPath()
        {
            // Given
            var audiobook = new AudiobookBuilder()
                .WithTitle("The Gunslinger")
                .WithAuthor("Stephen King")
                .WithYear("1982")
                .WithSeries("The Dark Tower")
                .WithSeriesNumber("1")
                .Build();

            var fileNamingService = _provider.GetRequiredService<IFileNamingService>();

            // When
            var result = LibraryPathPlanner.ComputeAudiobookBaseDirectoryFromPattern(audiobook, RootPath, FileNamingPattern, fileNamingService);

            // Then
            Assert.Equal(Path.Join(RootPath, "Stephen King", "The Dark Tower", "The Gunslinger"), result);
        }
    }
}
