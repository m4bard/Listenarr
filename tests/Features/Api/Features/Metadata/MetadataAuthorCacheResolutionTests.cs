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

namespace Listenarr.Tests.Features.Api.Features.Metadata
{
    /// <summary>
    /// How the metadata workflows reach a cached author row when the name lookup misses, and what
    /// they hand back when nothing was found. Every ASIN here is a fixture literal.
    /// </summary>
    [Trait("Name", "MetadataAuthorCacheResolutionTests")]
    [Trait("Category", "Metadata")]
    public class MetadataAuthorCacheResolutionTests : BaseTests
    {
        private static (MetadataLookupCacheWorkflow Lookup, MetadataImageCacheWorkflow Images) Build(
            Mock<IAudiobookRepository> repository,
            Mock<IImageCacheService> imageCache)
        {
            var logger = Mock.Of<ILogger<MetadataController>>();
            var images = new MetadataImageCacheWorkflow(repository.Object, imageCache.Object, logger);
            var lookup = new MetadataLookupCacheWorkflow(repository.Object, imageCache.Object, images, logger);
            return (lookup, images);
        }

        [Fact]
        public async Task ResolvePersistedAuthorCacheAsync_WhenTheNameMisses_DoesNotScanForAnAsin()
        {
            var repository = new Mock<IAudiobookRepository>();
            var imageCache = new Mock<IImageCacheService>();

            repository
                .Setup(repo => repo.GetCachedAuthorByNameAsync("Author One", "us"))
                .ReturnsAsync((AuthorCacheEntry?)null);
            repository
                .Setup(repo => repo.GetAuthorAsinByNameAsync("Author One"))
                .ReturnsAsync("FIXTURESHR1");
            repository
                .Setup(repo => repo.GetCachedAuthorByAsinAsync("FIXTURESHR1", "us"))
                .ReturnsAsync(new AuthorCacheEntry
                {
                    AuthorName = "Author Two",
                    AuthorNameNormalized = "author two",
                    AuthorAsin = "FIXTURESHR1",
                    Region = "us"
                });

            var (lookup, _) = Build(repository, imageCache);

            var resolved = await lookup.ResolvePersistedAuthorCacheAsync("Author One", "us", null);

            // The removed fallback could only fire where the name lookup had missed, which is
            // precisely where the row it finds belongs to somebody else. Asserting on the returned
            // row cannot tell "the fallback is gone" from "the fallback ran and missed", so the
            // call itself is what is asserted.
            Assert.Null(resolved);
            repository.Verify(repo => repo.GetAuthorAsinByNameAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ResolvePersistedAuthorCacheAsync_WhenTheNameHits_ReturnsThatRow()
        {
            var repository = new Mock<IAudiobookRepository>();
            var imageCache = new Mock<IImageCacheService>();

            repository
                .Setup(repo => repo.GetCachedAuthorByNameAsync("Author One", "us"))
                .ReturnsAsync(new AuthorCacheEntry
                {
                    AuthorName = "Author One",
                    AuthorNameNormalized = "author one",
                    AuthorAsin = "FIXTUREAUT1",
                    Region = "us"
                });

            var (lookup, _) = Build(repository, imageCache);

            var resolved = await lookup.ResolvePersistedAuthorCacheAsync("Author One", "us", null);

            // CONTROL. The unique-index-keyed name lookup already covers every row that genuinely
            // belongs to this name, and it must keep doing so.
            Assert.NotNull(resolved);
            Assert.Equal("FIXTUREAUT1", resolved!.AuthorAsin);
        }

        [Fact]
        public async Task ProbeAuthorImageCacheAsync_WhenNoCandidateHasAnImage_ReturnsNoAsin()
        {
            var repository = new Mock<IAudiobookRepository>();
            var imageCache = new Mock<IImageCacheService>();

            repository
                .Setup(repo => repo.GetCachedAuthorByNameAsync("Author One", "us"))
                .ReturnsAsync((AuthorCacheEntry?)null);
            repository
                .Setup(repo => repo.GetAuthorAsinByNameAsync("Author One"))
                .ReturnsAsync("FIXTURESHR1");
            imageCache
                .Setup(service => service.GetCachedImagePathAsync(It.IsAny<string>()))
                .ReturnsAsync((string?)null);

            var (_, images) = Build(repository, imageCache);

            var probe = await images.ProbeAuthorImageCacheAsync("Author One", "us", null);

            // A probe that found nothing used to hand back an ASIN anyway, and the author lookup
            // seeds its resolved identity from whatever comes back here.
            Assert.Null(probe.Asin);
            Assert.Null(probe.CachedPath);
        }

        [Fact]
        public async Task ProbeAuthorImageCacheAsync_WithAHintedAsin_ReturnsTheHint()
        {
            var repository = new Mock<IAudiobookRepository>();
            var imageCache = new Mock<IImageCacheService>();

            repository
                .Setup(repo => repo.GetCachedAuthorByNameAsync("Author One", "us"))
                .ReturnsAsync((AuthorCacheEntry?)null);
            repository
                .Setup(repo => repo.GetAuthorAsinByNameAsync("Author One"))
                .ReturnsAsync((string?)null);
            imageCache
                .Setup(service => service.GetCachedImagePathAsync(It.IsAny<string>()))
                .ReturnsAsync((string?)null);

            var (_, images) = Build(repository, imageCache);

            var probe = await images.ProbeAuthorImageCacheAsync("Author One", "us", "FIXTUREAUT1");

            // CONTROL. An ASIN the caller already had is evidence; handing it back is not a guess.
            Assert.Equal("FIXTUREAUT1", probe.Asin);
        }

        [Fact]
        public async Task ProbeAuthorImageCacheAsync_WhenACandidateHasAnImage_ReturnsThatPair()
        {
            var repository = new Mock<IAudiobookRepository>();
            var imageCache = new Mock<IImageCacheService>();

            repository
                .Setup(repo => repo.GetCachedAuthorByNameAsync("Author One", "us"))
                .ReturnsAsync((AuthorCacheEntry?)null);
            repository
                .Setup(repo => repo.GetAuthorAsinByNameAsync("Author One"))
                .ReturnsAsync("FIXTUREAUT1");
            imageCache
                .Setup(service => service.GetCachedImagePathAsync("FIXTUREAUT1"))
                .ReturnsAsync("config/cache/images/authors/FIXTUREAUT1.jpg");

            var (_, images) = Build(repository, imageCache);

            var probe = await images.ProbeAuthorImageCacheAsync("Author One", "us", null);

            // CONTROL. The probe's actual job still works.
            Assert.Equal("FIXTUREAUT1", probe.Asin);
            Assert.Equal("/config/cache/images/authors/FIXTUREAUT1.jpg", probe.CachedPath);
        }
    }
}
