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

namespace Listenarr.Tests.Features.Application.Downloads.Submission;

/// <summary>
/// A held-back .torrent or NZB fetch of the chosen release is not a server error: the search
/// answers that it did not grab, with the time it may be tried again.
/// </summary>
[Trait("Name", "SearchAndDownloadDeferralTests")]
[Trait("Category", "Application")]
public sealed class SearchAndDownloadDeferralTests : BaseTests
{
    private const string InfoHash = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";

    [Fact]
    public async Task SearchAndDownloadAsync_WhenTheGrabFetchIsDeferred_ReturnsTheDeferralInsteadOfThrowing()
    {
        var untilUtc = new DateTime(2026, 3, 2, 15, 0, 0, DateTimeKind.Utc);
        var search = new Mock<ISearchService>();
        search.Setup(service => service.SearchIndexersDetailedAsync(
                It.IsAny<string>(),
                It.IsAny<IndexerSearchContext>(),
                It.IsAny<IReadOnlyCollection<int>?>(),
                It.IsAny<bool>(),
                It.IsAny<SearchRequest?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IndexerSearchReport([
                new IndexerSearchOutcome(
                    1,
                    IndexerSearchStatus.Completed,
                    [
                        new IndexerSearchResult
                        {
                            Title = "A Princess of Mars",
                            MagnetLink = $"magnet:?xt=urn:btih:{InfoHash}&dn=book",
                            Size = 800_000_000,
                            Seeders = 20
                        }
                    ],
                    RequestsSent: 1)
            ]));
        var preparer = new Mock<IDownloadSubmissionPreparer>();
        preparer.Setup(p => p.PrepareAsync(It.IsAny<TrustedDownloadCandidate>(), It.IsAny<string>()))
            .ThrowsAsync(new IndexerRequestDeferredException(new RequestDeferral(untilUtc, RequestDeferralReason.RateLimited)));
        _services.AddSingleton(search.Object);
        _services.AddSingleton(preparer.Object);
        Init();

        await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfigurationBuilder()
            .WithHost("localhost")
            .WithPort(8080)
            .WithUsername("admin")
            .WithPassword("admin")
            .WithType("qbittorrent")
            .Build());
        var qualityProfile = await _qualityProfileRepository.AddAsync(new QualityProfileBuilder().Build());
        var audiobook = await _audiobookRepository.AddAsync(
            new AudiobookBuilder().WithTitle("A Princess of Mars").WithQualityProfile(qualityProfile).Build());

        var result = await _provider.GetRequiredService<IDownloadService>().SearchAndDownloadAsync(audiobook.Id);

        Assert.False(result.Success);
        Assert.Equal(new RequestDeferral(untilUtc, RequestDeferralReason.RateLimited), result.Deferral);
        Assert.Contains(untilUtc.ToString("O"), result.Message, StringComparison.Ordinal);
    }
}
