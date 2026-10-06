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

namespace Listenarr.Tests.Features.Application.Search.Scoring
{
    [Trait("Name", "MusicReleaseClassifierTests")]
    [Trait("Category", "Application")]
    public sealed class MusicReleaseClassifierTests : BaseTests
    {
        private static SearchResult Result(string? category = null, string? title = "Some Audiobook Title", string? format = null, string? quality = null)
        {
            return new SearchResult
            {
                Title = title ?? string.Empty,
                Category = category ?? string.Empty,
                Format = format ?? string.Empty,
                Quality = quality ?? string.Empty
            };
        }

        [Theory]
        [InlineData("3000")]
        [InlineData("3010")]
        [InlineData("3040")]
        public void NewznabMusicCategoryId_IsRejected(string category)
        {
            var result = Result(category: category);

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.True(looksLikeMusic);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        [Fact]
        public void NewznabAudiobooksCategory3030_IsNeverRejectedOnCategory()
        {
            var result = Result(category: "3030");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
            Assert.Null(reason);
        }

        [Fact]
        public void MissingCategory_FailsOpen_IsNotRejected()
        {
            var result = Result(category: null);

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
            Assert.Null(reason);
        }

        [Fact]
        public void DefaultAudiobookCategoryFallback_IsNotRejected()
        {
            // TorznabResponseParser defaults an unparsed <category> element to the literal "Audiobook".
            var result = Result(category: "Audiobook");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        [Theory]
        [InlineData("Music")]
        [InlineData("music")]
        [InlineData("Audio - Music")]
        public void TorrentIndexerMusicCategoryName_IsRejected(string category)
        {
            var result = Result(category: category);

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.True(looksLikeMusic);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        [Fact]
        public void CategoryNamingBothMusicAndAudiobook_AudiobookTokenWins()
        {
            // Defensive: a category string naming both tokens should never be rejected just because
            // "music" appears somewhere in it (e.g. an indexer category literally named this way).
            var result = Result(category: "Audiobook - Music Themed");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        [Theory]
        [InlineData("2000")]
        [InlineData("7020")]
        [InlineData("8010")]
        public void NonMusicNumericCategory_IsNotRejected(string category)
        {
            var result = Result(category: category);

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void SceneStyleAlbumTitle_IsRejected()
        {
            var result = Result(category: null, title: "Some Artist - Some Album (2019) [FLAC]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.True(looksLikeMusic);
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        [Fact]
        public void DiscographyTitle_IsRejected()
        {
            var result = Result(category: null, title: "Some Artist Discography (1990-2010) [MP3]");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.True(looksLikeMusic);
        }

        [Fact]
        public void SceneStyleAlbumTitle_WithExplicitAudiobookSignal_IsNotRejected()
        {
            // Audiobook signal words in the title always win over the narrow shape heuristic.
            var result = Result(category: null, title: "Some Book (2019) [FLAC] - Unabridged, narrated by Someone");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void OrdinaryAudiobookTitle_IsNotRejected()
        {
            var result = Result(category: null, title: "Author Name - Great Book Title (Unabridged)");

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }

        [Fact]
        public void EmptyTitleAndCategory_FailsOpen()
        {
            var result = Result(category: null, title: null);

            var looksLikeMusic = MusicReleaseClassifier.LooksLikeMusicRelease(result, out var reason);

            Assert.False(looksLikeMusic);
        }
    }
}
