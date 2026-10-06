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
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Audiobooks.Quality
{
    /// <summary>
    /// Operator decision (tracker #336): an opt-in, default-off content-type gate that rejects
    /// clearly-music releases when ApplicationSettings.RejectClearlyMusicReleases is enabled.
    /// Covers the four required cases: default-off is a no-op, toggle-on rejects a music category,
    /// toggle-on fails open on Audiobooks/ambiguous category, toggle-on rejects a torrent-indexer
    /// music category equivalent.
    /// </summary>
    [Trait("Name", "QualityProfileMusicCategoryGateTests")]
    [Trait("Category", "Application")]
    public sealed class QualityProfileMusicCategoryGateTests : BaseTests
    {
        private static QualityProfile MakeProfile() => new QualityProfile
        {
            MinimumSize = 0,
            MaximumSize = 0,
            MustNotContain = new List<string>(),
            MustContain = new List<string>(),
            MinimumSeeders = 0,
            MaximumAge = 0
        };

        private static SearchResult MakeTorrentResult(string category, string title = "Author Name - Great Book (Unabridged)")
        {
            return new SearchResult
            {
                Title = title,
                Category = category,
                DownloadType = "torrent",
                Size = 150 * 1024 * 1024,
                Seeders = 10,
                Format = "mp3",
                Quality = "320",
                PublishedDate = DateTime.UtcNow.ToString("o")
            };
        }

        private static QualityProfileService CreateService(bool rejectMusicReleases)
        {
            var configurationService = new Mock<IConfigurationService>();
            configurationService
                .Setup(c => c.GetApplicationSettingsAsync())
                .ReturnsAsync(new ApplicationSettings { RejectClearlyMusicReleases = rejectMusicReleases });

            return new QualityProfileService(
                Mock.Of<IQualityProfileRepository>(),
                NullLogger<QualityProfileService>.Instance,
                indexerRepository: null,
                configurationService: configurationService.Object);
        }

        [Fact]
        public async Task ToggleOff_MusicCategoryRelease_IsStillAcceptedDefaultBehaviorUnchanged()
        {
            var service = CreateService(rejectMusicReleases: false);
            var profile = MakeProfile();
            var result = MakeTorrentResult(category: "3010");

            var score = await service.ScoreSearchResult(result, profile);

            Assert.False(score.IsRejected);
        }

        [Fact]
        public async Task ToggleOn_MusicCategoryRelease_IsRejected()
        {
            var service = CreateService(rejectMusicReleases: true);
            var profile = MakeProfile();
            var result = MakeTorrentResult(category: "3010");

            var score = await service.ScoreSearchResult(result, profile);

            Assert.True(score.IsRejected);
        }

        [Theory]
        [InlineData("3030")] // Audiobooks itself, in the standard Newznab taxonomy
        [InlineData("")]     // no category/ambiguous signal
        public async Task ToggleOn_AudiobooksOrAmbiguousCategory_FailsOpen_IsAccepted(string category)
        {
            var service = CreateService(rejectMusicReleases: true);
            var profile = MakeProfile();
            var result = MakeTorrentResult(category: category);

            var score = await service.ScoreSearchResult(result, profile);

            Assert.False(score.IsRejected);
        }

        [Fact]
        public async Task ToggleOn_TorrentIndexerMusicCategoryEquivalent_IsRejected()
        {
            var service = CreateService(rejectMusicReleases: true);
            var profile = MakeProfile();
            // Torrent indexers routinely expose categories as free text rather than a Newznab id.
            var result = MakeTorrentResult(category: "Music");

            var score = await service.ScoreSearchResult(result, profile);

            Assert.True(score.IsRejected);
        }

        [Fact]
        public async Task ToggleOn_AppliesAcrossScoreSearchResults_NotJustSingleResult()
        {
            var service = CreateService(rejectMusicReleases: true);
            var profile = MakeProfile();
            var musicResult = MakeTorrentResult(category: "3010");
            var audiobookResult = MakeTorrentResult(category: "3030");

            var scores = await service.ScoreSearchResults(new List<SearchResult> { musicResult, audiobookResult }, profile);

            Assert.Equal(2, scores.Count);
            Assert.Contains(scores, s => ReferenceEquals(s.SearchResult, musicResult) && s.IsRejected);
            Assert.Contains(scores, s => ReferenceEquals(s.SearchResult, audiobookResult) && !s.IsRejected);
        }
    }
}
