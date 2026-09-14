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
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Api.Services
{
    /// <summary>
    /// A series number is free text on the metadata side and is not always a lone number.
    /// An omnibus carries a range such as "1-4", which decimal.TryParse rejects, so the value
    /// used to vanish from the {SeriesNumber} naming token while the AudibleBookMetadata
    /// overload of the same token rendered it intact.
    /// </summary>
    [Trait("Category", "FileNamingService")]
    [Trait("Name", "FileNamingService_SeriesNumberTokenTests")]
    public class FileNamingService_SeriesNumberTokenTests : BaseTests
    {
        private readonly FileNamingService _service;

        public FileNamingService_SeriesNumberTokenTests()
        {
            var configService = new Mock<IConfigurationService>();
            configService.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(new ApplicationSettings());
            _service = new FileNamingService(configService.Object, new Mock<ILogger<FileNamingService>>().Object);
        }

        [Theory]
        [InlineData("1-4", "1-4")]
        [InlineData("1 - 4", "1 - 4")]
        [InlineData("1-3, 5", "1-3, 5")]
        [InlineData("3", "3")]
        [InlineData("1.5", "1.5")]
        public void SeriesNumberToken_RendersRangeAndPlainPositionsAlike(string seriesNumber, string expected)
        {
            var metadata = new Audiobook { Title = "Anything", SeriesNumber = seriesNumber }
                .CreateBasicAudioMetadata();

            var rendered = _service.ApplyNamingPattern("{SeriesNumber}", metadata, treatAsFilename: true);

            Assert.Equal(expected, rendered);
        }

        [Fact]
        public void SeriesNumberToken_RangeDoesNotFallBackToTrackNumber()
        {
            var metadata = new Audiobook { Title = "Anything", SeriesNumber = "1-4" }
                .CreateBasicAudioMetadata();
            metadata.TrackNumber = 7;

            var rendered = _service.ApplyNamingPattern("{SeriesNumber}", metadata, treatAsFilename: true);

            Assert.Equal("1-4", rendered);
        }

        [Fact]
        public void SeriesNumberToken_EmptySeriesNumberStillFallsBackToTrackNumber()
        {
            var metadata = new Audiobook { Title = "Anything", SeriesNumber = null }
                .CreateBasicAudioMetadata();
            metadata.TrackNumber = 7;

            var rendered = _service.ApplyNamingPattern("{SeriesNumber}", metadata, treatAsFilename: true);

            Assert.Equal("7", rendered);
        }

        [Fact]
        public void SeriesNumberToken_AgreesAcrossTheAudioAndAudibleMetadataBuilders()
        {
            var audioMetadata = new Audiobook { Title = "Anything", SeriesNumber = "1-4" }
                .CreateBasicAudioMetadata();
            var audibleMetadata = new AudibleBookMetadata { Title = "Anything", SeriesNumber = "1-4" };

            var fromAudio = _service.ApplyNamingPattern("{SeriesNumber}", audioMetadata, treatAsFilename: true);
            var fromAudible = _service.ApplyNamingPattern("{SeriesNumber}", audibleMetadata, treatAsFilename: true);

            Assert.Equal(fromAudible, fromAudio);
            Assert.Equal("1-4", fromAudio);
        }

        [Fact]
        public void BuildNamingMetadata_CarriesTheRawSeriesNumberForImport()
        {
            var audiobook = new Audiobook { Title = "Anything", SeriesNumber = "1-4" };

            var metadata = DownloadImportService.BuildNamingMetadata(audiobook, null, "fallback");

            Assert.Null(metadata.SeriesPosition);
            Assert.Equal("1-4", metadata.SeriesPositionText);
        }
    }
}
