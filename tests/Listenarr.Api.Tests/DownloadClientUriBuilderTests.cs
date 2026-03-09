using System;
using Listenarr.Api.Services.Adapters;
using Listenarr.Domain.Models;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class DownloadClientUriBuilderTests
    {
        [Fact]
        public void ResolveTorrentAddTarget_PrefersValidatedMagnetLink()
        {
            var result = new SearchResult
            {
                MagnetLink = " magnet:?xt=urn:btih:abc123 ",
                TorrentUrl = "https://example.com/file.torrent"
            };

            var target = DownloadClientUriBuilder.ResolveTorrentAddTarget(result);

            Assert.True(target.IsMagnet);
            Assert.Equal("magnet:?xt=urn:btih:abc123", target.Value);
        }

        [Fact]
        public void ResolveTorrentAddTarget_RejectsInvalidMagnetScheme()
        {
            var result = new SearchResult
            {
                MagnetLink = "https://example.com/not-a-magnet"
            };

            var ex = Assert.Throws<ArgumentException>(() => DownloadClientUriBuilder.ResolveTorrentAddTarget(result));

            Assert.Contains("magnet scheme", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ResolveTorrentAddTarget_RejectsNonHttpTorrentUrl()
        {
            var result = new SearchResult
            {
                TorrentUrl = "ftp://example.com/file.torrent"
            };

            var ex = Assert.Throws<ArgumentException>(() => DownloadClientUriBuilder.ResolveTorrentAddTarget(result));

            Assert.Contains("HTTP or HTTPS", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
