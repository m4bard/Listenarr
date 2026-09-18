using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Downloads.Submission;

[Trait("Name", "BlockedReleaseFilterTests")]
[Trait("Category", "Application")]
public sealed class BlockedReleaseFilterTests : BaseTests
{
    private const string InfoHash = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";
    private const string OtherInfoHash = "0000000000000000000000000000000000000001";
    private const string Magnet = "magnet:?xt=urn:btih:ABCDEF1234567890ABCDEF1234567890ABCDEF12&dn=book";

    [Fact]
    public async Task ExcludeAsync_DropsAReleaseAlreadyBlockedForThisBook()
    {
        // The loop in #838: the only result is the one that already failed, so a search
        // that cannot tell must grab it again.
        var blocklist = BlocklistReturning(Row(InfoHash, "Book", null));

        var kept = await BlockedReleaseFilter.ExcludeAsync(
            blocklist.Object, 7, [Scored(Magnet)], NullLogger.Instance);

        Assert.Empty(kept);
    }

    [Fact]
    public async Task ExcludeAsync_KeepsADifferentReleaseOfTheSameBook()
    {
        // Blocking one release must not ban the title. If this ever fails, a single bad
        // release takes the book out of circulation permanently.
        var blocklist = BlocklistReturning(Row(OtherInfoHash, "A Quite Different Listing", null));

        var kept = await BlockedReleaseFilter.ExcludeAsync(
            blocklist.Object, 7, [Scored(Magnet)], NullLogger.Instance);

        Assert.Single(kept);
    }

    [Fact]
    public async Task ExcludeAsync_MatchesATorrentByInfoHashEvenWhenTheUrlChanged()
    {
        // Indexers hand back different URLs for the same torrent. Keying on the URL alone
        // would let the same release back in on the next search.
        var blocklist = BlocklistReturning(Row(InfoHash, "Book", null));

        var moved = Scored(Magnet);
        moved.SearchResult.TorrentUrl = "https://indexer.example.com/a-completely-different-path.torrent";

        var kept = await BlockedReleaseFilter.ExcludeAsync(
            blocklist.Object, 7, [moved], NullLogger.Instance);

        Assert.Empty(kept);
    }

    [Fact]
    public async Task ExcludeAsync_MatchesATorrentWhoseMagnetIsNowServedInBase32()
    {
        // One torrent, two encodings of the same info-hash. Two indexers advertising it in the
        // two forms used to produce two keys and the second one was never blocked.
        var blocklist = BlocklistReturning(
            Row("CBC2F069FE8BB2F544EAE707D75BCD3DE9DCF951", "Book", null));

        var base32 = Scored("magnet:?xt=urn:btih:ZPBPA2P6ROZPKRHK44D5OW6NHXU5Z6KR&tr=udp");

        var kept = await BlockedReleaseFilter.ExcludeAsync(
            blocklist.Object, 7, [base32], NullLogger.Instance);

        Assert.Empty(kept);
    }

    [Fact]
    public async Task ExcludeAsync_MatchesATorrentBlockedUnderItsHashWhenTheIndexerNowServesNoMagnet()
    {
        // The branch-switching defect at the level the filter sees it. The row was written when
        // the indexer served a magnet; this listing of the same release is a .torrent URL, so no
        // hash can be derived from it and the advertised title and size have to carry the match.
        var blocklist = BlocklistReturning(Row(InfoHash, "Some Book Unabridged M4B", 734003200));

        var noMagnet = new QualityScore
        {
            TotalScore = 90,
            SearchResult = new SearchResult
            {
                Title = "Some Book Unabridged M4B",
                Size = 734003200,
                TorrentUrl = "https://indexer.example.com/download/abc.torrent"
            }
        };

        var kept = await BlockedReleaseFilter.ExcludeAsync(
            blocklist.Object, 7, [noMagnet], NullLogger.Instance);

        Assert.Empty(kept);
    }

    [Fact]
    public async Task ExcludeAsync_WithNothingBlocked_DoesNotQueryPerResult()
    {
        var blocklist = new Mock<IBlocklistService>();
        blocklist.Setup(service => service.GetForAudiobookAsync(7))
            .ReturnsAsync([]);

        var kept = await BlockedReleaseFilter.ExcludeAsync(
            blocklist.Object, 7, [Scored(Magnet), Scored(Magnet)], NullLogger.Instance);

        Assert.Equal(2, kept.Count);
        blocklist.Verify(service => service.GetForAudiobookAsync(7), Times.Once);
    }

    [Fact]
    public async Task ExcludeAsync_DropsAUsenetReleaseWhoseDownloadLinkChangedSinceItWasBlocked()
    {
        // The defect this replaced, end to end. A Usenet indexer mints a new download link with a
        // new token on every grab, so the release blocked after one failure arrived at the next
        // search with a different URL. Keying on that URL meant the write side and the read side
        // computed different identities and the filter never matched, which on a live install
        // produced several hundred grabs of one dead post for a single book over half a day.
        const string title = "Some Book Unabridged";
        const long size = 734003200;

        var blocklist = BlocklistReturning(Row(null, title, size));

        var comesBackWithANewLink = new QualityScore
        {
            TotalScore = 90,
            SearchResult = new SearchResult
            {
                Title = title,
                Size = size,
                NzbUrl = "https://indexer.example.com/getnzb?id=abc&apikey=TOKEN2"
            }
        };

        var kept = await BlockedReleaseFilter.ExcludeAsync(
            blocklist.Object, 7, [comesBackWithANewLink], NullLogger.Instance);

        Assert.Empty(kept);
    }

    [Fact]
    public async Task ExcludeAsync_DropsAUsenetReleaseWhoseReportedSizeMovedSlightly()
    {
        // The reason size is a tolerant comparison rather than part of the key. An indexer that
        // rounds, reposts, or reports a padded size differs by a few bytes between two listings
        // of one release; with the size folded into a digest that was a silent miss every time.
        const string title = "Some Book Unabridged";

        var blocklist = BlocklistReturning(Row(null, title, 734003200));

        var slightlyDifferent = new QualityScore
        {
            TotalScore = 90,
            SearchResult = new SearchResult
            {
                Title = title,
                Size = 734003200 + 1024,
                NzbUrl = "https://indexer.example.com/b"
            }
        };

        var kept = await BlockedReleaseFilter.ExcludeAsync(
            blocklist.Object, 7, [slightlyDifferent], NullLogger.Instance);

        Assert.Empty(kept);
    }

    [Fact]
    public async Task ExcludeAsync_KeepsAUsenetReleaseOfADifferentSize()
    {
        // The other half, and the control for the test above: the tolerance is slack, not a
        // licence to ban every release sharing a title.
        const string title = "Some Book Unabridged";

        var blocklist = BlocklistReturning(Row(null, title, 734003200));

        var different = new QualityScore
        {
            TotalScore = 90,
            SearchResult = new SearchResult
            {
                Title = title,
                Size = 999999999,
                NzbUrl = "https://indexer.example.com/b"
            }
        };

        var kept = await BlockedReleaseFilter.ExcludeAsync(
            blocklist.Object, 7, [different], NullLogger.Instance);

        Assert.Single(kept);
    }

    private static Mock<IBlocklistService> BlocklistReturning(params BlockedRelease[] rows)
    {
        var blocklist = new Mock<IBlocklistService>();
        blocklist.Setup(service => service.GetForAudiobookAsync(7)).ReturnsAsync(rows);
        return blocklist;
    }

    // Built the way the failure path builds one: the key through ReleaseIdentity, and the
    // advertised title and size alongside it.
    private static BlockedRelease Row(string? infoHash, string title, long? size) => new()
    {
        AudiobookId = 7,
        ReleaseIdentifier = ReleaseIdentity.KeyFor(infoHash, title)!.Value,
        Title = title,
        Size = size
    };

    private static QualityScore Scored(string magnet) => new()
    {
        TotalScore = 90,
        SearchResult = new SearchResult { Title = "Book", MagnetLink = magnet }
    };
}
