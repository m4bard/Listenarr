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
using System.Globalization;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks.Metadata;

namespace Listenarr.Tests.Features.Application.Downloads.Import
{
    /// <summary>
    /// The series position of an audiobook reaches the naming pattern as text. Its parse and its
    /// render must not depend on the machine culture, or a de-DE host turns 1.5 into 15 or 1,5.
    /// </summary>
    [Trait("Name", nameof(DownloadImportService_SeriesPositionCultureTests))]
    [Trait("Category", "DownloadProcessingJob")]
    public class DownloadImportService_SeriesPositionCultureTests : BaseTests
    {
        private readonly MetadataServiceMock _metadataServiceMock = new();

        public override async Task InitializeAsync()
        {
            _services.AddSingleton<IMetadataService>(_metadataServiceMock);
            Init();
            await AddAuthorizedRootAsync(FileService.GetTempPath());
        }

        [Theory]
        [InlineData("{Title} [{SeriesNumber:'#'0.##}]", "Culture Title [#1.5]")]
        [InlineData("{Title} [{SeriesNumber}]", "Culture Title [1.5]")]
        public async Task MultiFileImport_UnderGermanCulture_RendersSeriesPositionInvariantly(string pattern, string expectedName)
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                var outputDir = FileService.GetTempDirectory("listenarr-series-culture");
                var srcDir = FileService.GetTempDirectory("listenarr-series-culture-src");
                var part1 = await FileService.GetFileAsync(srcDir, "Part 1.mp3", "one");
                var part2 = await FileService.GetFileAsync(srcDir, "Part 2.mp3", "two");

                _metadataServiceMock.AddMetadata(@"\.mp3$", new AudioMetadata { Title = "Culture Title", Format = "mp3", BitRate = 128000 });

                var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
                    .WithBasePath(outputDir)
                    .WithSeriesNumber("1.5")
                    .Build());

                await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
                    .WithOutputPath(outputDir)
                    .WithMetadataProcessing()
                    .WithCopyFileOnCompleted()
                    .WithFolderNamingPattern("")
                    .WithFileNamingPattern(pattern)
                    .WithMultiFileNamingPattern(pattern + "-{DiskNumber:00}")
                    .Build());

                var results = await _provider.GetRequiredService<IDownloadImportService>()
                    .ImportDownloadFilesAsync(audiobook, [part1, part2]);

                var finalNames = results
                    .Where(r => r.Success && !string.IsNullOrWhiteSpace(r.FinalPath))
                    .Select(r => Path.GetFileName(r.FinalPath!))
                    .Order()
                    .ToList();

                Assert.Equal([$"{expectedName}-01.mp3", $"{expectedName}-02.mp3"], finalNames);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }
    }
}
