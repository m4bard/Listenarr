using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Downloads;

[Trait("Name", "ReleaseIdentityTests")]
[Trait("Category", "Domain")]
public sealed class ReleaseIdentityTests : BaseTests
{
    private const string HexHash = "ABCDEF0123456789ABCDEF0123456789ABCDEF01";
    private const string OtherHexHash = "0123456789ABCDEF0123456789ABCDEF01234567";

    [Fact]
    public void KeyFor_PrefersTheInfoHash_SoOneReleaseIsOneKeyAcrossIndexers()
    {
        var fromOneIndexer = ReleaseIdentity.KeyFor(HexHash, "A Book");
        var fromAnother = ReleaseIdentity.KeyFor(HexHash.ToLowerInvariant(), "A Different Listing Title");

        Assert.Equal(fromOneIndexer, fromAnother);
        Assert.StartsWith(ReleaseIdentity.InfoHashPrefix, fromOneIndexer);
    }

    [Fact]
    public void KeyFor_TitleDifferingOnlyByCaseAndSpacing_IsTheSameRelease()
    {
        var one = ReleaseIdentity.KeyFor(null, "Some  Book   Unabridged");
        var two = ReleaseIdentity.KeyFor(null, "  some book unabridged  ");

        Assert.Equal(one, two);
        Assert.StartsWith(ReleaseIdentity.TitlePrefix, one);
    }

    [Fact]
    public void KeyFor_TitleInDecomposedAndPrecomposedForm_IsTheSameRelease()
    {
        // The same title with e-acute as one codepoint and as a letter plus a combining accent.
        // Both forms are served by real indexers for the same release; without the NFC pass they
        // are two keys, and the blocklist silently stops working for every accented title.
        // Spelled with escapes rather than literal characters so the distinction survives an
        // editor, a copy-paste or a repository checkout that normalises text.
        const string precomposedTitle = "Les Mis\u00E9rables Unabridged";
        const string decomposedTitle = "Les Mise\u0301rables Unabridged";

        // The control for this test: the two inputs really are different strings, so an
        // implementation that only trims and lowercases cannot make them agree.
        Assert.NotEqual(precomposedTitle, decomposedTitle);

        Assert.Equal(
            ReleaseIdentity.KeyFor(null, precomposedTitle),
            ReleaseIdentity.KeyFor(null, decomposedTitle));
    }

    [Fact]
    public void KeyFor_WithNothingIdentifying_ReturnsNull()
    {
        // Better to record nothing than to invent a key that would match the wrong release later.
        Assert.Null(ReleaseIdentity.KeyFor(null, null));
        Assert.Null(ReleaseIdentity.KeyFor("   ", "  "));
    }

    [Fact]
    public void KeyFor_WithAHashThatIsNotAHash_FallsBackToTheTitleRatherThanMintingOne()
    {
        // "ABCDEF" is the shape a truncated or garbled hash arrives in. Accepting it would put a
        // key in the database that no correctly parsed magnet can ever reproduce.
        var key = ReleaseIdentity.KeyFor("ABCDEF", "Some Book Unabridged");

        Assert.Equal(ReleaseIdentity.KeyFor(null, "Some Book Unabridged"), key);
        Assert.StartsWith(ReleaseIdentity.TitlePrefix, key);
    }

    [Theory]
    // The pair Sonarr pins in its own fixtures, at
    // src/NzbDrone.Core.Test/Download/DownloadClientTests/QBittorrentTests/QBittorrentFixture.cs:469.
    [InlineData(
        "magnet:?xt=urn:btih:ZPBPA2P6ROZPKRHK44D5OW6NHXU5Z6KR&tr=udp",
        "cbc2f069fe8bb2f544eae707d75bcd3de9dcf951")]
    [InlineData(
        "magnet:?xt=urn:btih:CBC2F069FE8BB2F544EAE707D75BCD3DE9DCF951&tr=udp",
        "cbc2f069fe8bb2f544eae707d75bcd3de9dcf951")]
    // Percent-encoded, which the previous substring search missed entirely.
    [InlineData(
        "magnet:?xt=urn%3Abtih%3ACBC2F069FE8BB2F544EAE707D75BCD3DE9DCF951&dn=x",
        "cbc2f069fe8bb2f544eae707d75bcd3de9dcf951")]
    // The indexed parameter name a multi-topic magnet uses. Same marker once decoded.
    [InlineData(
        "magnet:?xt.1=urn:btih:CBC2F069FE8BB2F544EAE707D75BCD3DE9DCF951",
        "cbc2f069fe8bb2f544eae707d75bcd3de9dcf951")]
    // No trailing separator, and already lower case.
    [InlineData(
        "magnet:?xt=urn:btih:zpbpa2p6rozpkrhk44d5ow6nhxu5z6kr",
        "cbc2f069fe8bb2f544eae707d75bcd3de9dcf951")]
    public void TorrentHashFrom_BothEncodingsOfOneTorrent_NormaliseToOneHash(string magnet, string expected)
    {
        Assert.Equal(expected, ReleaseIdentity.TorrentHashFrom(magnet));
    }

    [Theory]
    [InlineData("magnet:?xt=urn:btih:&dn=x")]
    [InlineData("magnet:?xt=urn:btih:notahashatall&dn=x")]
    // 32 characters, so the right length for base32, but '1' and '0' are outside the alphabet.
    [InlineData("magnet:?xt=urn:btih:10101010101010101010101010101010")]
    // 40 characters, so the right length for hex, but 'z' is not a hex digit.
    [InlineData("magnet:?xt=urn:btih:zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [InlineData("https://indexer.example.com/download/abc.torrent")]
    [InlineData("")]
    [InlineData(null)]
    public void TorrentHashFrom_WhenThereIsNoUsableHash_AnswersNull(string? magnet)
    {
        // The old substring extraction answered the empty string for the first of these, and
        // "btih:" then became a key shared by every release with a broken magnet.
        Assert.Null(ReleaseIdentity.TorrentHashFrom(magnet));
    }

    [Fact]
    public void Matches_RowWrittenUnderAnInfoHash_StillMatchesWhenTheIndexerServesNoMagnet()
    {
        // The branch-switching defect. The release was grabbed while the indexer served a magnet,
        // so the row is keyed on the info-hash; the next search returns the same release as a
        // .torrent URL with no magnet, so no hash can be derived from it at all. The advertised
        // title and size on the row are what carry the match.
        var entry = new BlockedRelease
        {
            ReleaseIdentifier = ReleaseIdentity.KeyFor(HexHash, "Some Book Unabridged M4B")!,
            Title = "Some Book Unabridged M4B",
            Size = 734_003_200
        };

        var laterSearch = new SearchResult
        {
            Title = "Some Book Unabridged M4B",
            TorrentUrl = "https://indexer.example.com/download/abc.torrent",
            Size = 734_003_200
        };

        // Both halves of the precondition, so the assertion below cannot pass because the two
        // sides happened to agree on a key after all: the row is keyed on the hash, and the later
        // listing can produce no hash at all.
        Assert.StartsWith(ReleaseIdentity.InfoHashPrefix, entry.ReleaseIdentifier);
        Assert.StartsWith(ReleaseIdentity.TitlePrefix, ReleaseIdentity.For(laterSearch));
        Assert.NotEqual(entry.ReleaseIdentifier, ReleaseIdentity.For(laterSearch));

        Assert.True(ReleaseIdentity.Matches(entry, laterSearch));
    }

    [Fact]
    public void Matches_RowWrittenUnderATitle_StillMatchesWhenTheIndexerStartsServingAMagnet()
    {
        // The same defect in the other direction, which Readarr's own lookup does not close: a
        // release carrying a hash is only ever looked up by hash there.
        var entry = new BlockedRelease
        {
            ReleaseIdentifier = ReleaseIdentity.KeyFor(null, "Some Book Unabridged M4B")!,
            Title = "Some Book Unabridged M4B",
            Size = 734_003_200
        };

        var laterSearch = new SearchResult
        {
            Title = "Some Book Unabridged M4B",
            MagnetLink = $"magnet:?xt=urn:btih:{HexHash}&dn=x",
            Size = 734_003_200
        };

        Assert.StartsWith(ReleaseIdentity.TitlePrefix, entry.ReleaseIdentifier);
        Assert.StartsWith(ReleaseIdentity.InfoHashPrefix, ReleaseIdentity.For(laterSearch));
        Assert.NotEqual(entry.ReleaseIdentifier, ReleaseIdentity.For(laterSearch));

        Assert.True(ReleaseIdentity.Matches(entry, laterSearch));
    }

    [Fact]
    public void Matches_TwoDifferentTorrents_DoNotMatchEvenWhenTitleAndSizeAgree()
    {
        // Distinct v1 info-hashes are distinct torrents, so the hash has to settle it rather than
        // falling through to a title comparison that would over-block.
        var entry = new BlockedRelease
        {
            ReleaseIdentifier = ReleaseIdentity.KeyFor(HexHash, "Some Book Unabridged M4B")!,
            Title = "Some Book Unabridged M4B",
            Size = 734_003_200
        };

        var other = new SearchResult
        {
            Title = "Some Book Unabridged M4B",
            MagnetLink = $"magnet:?xt=urn:btih:{OtherHexHash}&dn=x",
            Size = 734_003_200
        };

        Assert.False(ReleaseIdentity.Matches(entry, other));
    }

    [Fact]
    public void Matches_SizeReportedSlightlyDifferently_IsStillTheSameRelease()
    {
        // The reason a digest over an exact byte count could not be relaxed later. Two listings of
        // one release disagree about size by a rounding or a padded-file difference, and Readarr
        // allows 2 MB for exactly this (BlocklistService.cs:157-167). The pair below sits either
        // side of the boundary, so a tolerance that is absent, wrong or one-sided fails here.
        var entry = new BlockedRelease
        {
            ReleaseIdentifier = ReleaseIdentity.KeyFor(null, "Some Book Unabridged")!,
            Title = "Some Book Unabridged",
            Size = 734_003_200
        };

        var justInside = new SearchResult
        {
            Title = "Some Book Unabridged",
            Size = 734_003_200 + ReleaseIdentity.SizeToleranceBytes
        };
        var justOutside = new SearchResult
        {
            Title = "Some Book Unabridged",
            Size = 734_003_200 + ReleaseIdentity.SizeToleranceBytes + 1
        };

        Assert.True(ReleaseIdentity.Matches(entry, justInside));
        Assert.False(ReleaseIdentity.Matches(entry, justOutside));
    }

    [Fact]
    public void Matches_RowWithNoRecordedSize_MatchesAnySize()
    {
        // A missing stored field is no constraint, which is the opposite of what a digest does
        // with one. Readarr: HasSameSize returns true when item.Size has no value
        // (BlocklistService.cs:157-161).
        var entry = new BlockedRelease
        {
            ReleaseIdentifier = ReleaseIdentity.KeyFor(null, "Some Book Unabridged")!,
            Title = "Some Book Unabridged",
            Size = null
        };

        Assert.True(ReleaseIdentity.Matches(
            entry, new SearchResult { Title = "Some Book Unabridged", Size = 1 }));
        Assert.True(ReleaseIdentity.Matches(
            entry, new SearchResult { Title = "Some Book Unabridged", Size = 9_000_000_000 }));
    }

    [Fact]
    public void Matches_CandidateReportingNoSize_IsTreatedAsNoSizeRatherThanZeroBytes()
    {
        // SearchResult.Size is a non-nullable long defaulting to 0, so an indexer that omits the
        // size attribute hands the filter a candidate reporting zero bytes rather than one
        // reporting no size. Deliberately treated as no constraint, which is more tolerant than
        // Readarr's HasSameSize, where a zero is compared as a number and a sized row does not
        // match it. The alternative is that a dead release re-listed without a size never matches
        // the row written when it failed, which is the re-grab loop this feature exists to end.
        //
        // The control is the second half: a candidate reporting one byte is a real measurement and
        // does not match, so this is about zero specifically and not about the tolerance being
        // broken.
        var entry = new BlockedRelease
        {
            ReleaseIdentifier = ReleaseIdentity.KeyFor(null, "Some Book Unabridged")!,
            Title = "Some Book Unabridged",
            Size = 734_003_200
        };

        var reportsNoSize = new SearchResult { Title = "Some Book Unabridged" };
        Assert.Equal(0, reportsNoSize.Size);

        Assert.True(ReleaseIdentity.Matches(entry, reportsNoSize));
        Assert.False(ReleaseIdentity.Matches(
            entry, new SearchResult { Title = "Some Book Unabridged", Size = 1 }));
    }

    [Fact]
    public void Matches_RowWhoseTitleColumnDisagreesWithItsKey_MatchesOnEither()
    {
        // Two keys, either can match, applies within the title key as well. Reading the column and
        // only falling back to the key's own title when the column is empty made the column shadow
        // the key: a row whose column said "Unknown" would not match the title its key was built
        // from, even though the key holds the answer.
        //
        // The two cannot disagree today, so this pins the rule rather than reporting a live bug.
        var columnIsWrong = new BlockedRelease
        {
            ReleaseIdentifier = ReleaseIdentity.KeyFor(null, "The Real Advertised Title")!,
            Title = "Unknown",
            Size = null
        };

        var keyIsUnhelpful = new BlockedRelease
        {
            ReleaseIdentifier = ReleaseIdentity.KeyFor(HexHash, "The Real Advertised Title")!,
            Title = "The Real Advertised Title",
            Size = null
        };

        var listing = new SearchResult { Title = "The Real Advertised Title", Size = 734_003_200 };

        // The case the write side can actually produce: a download with no title at all, so the
        // column is empty and only the key carries one.
        var columnIsEmpty = new BlockedRelease
        {
            ReleaseIdentifier = ReleaseIdentity.KeyFor(null, "The Real Advertised Title")!,
            Title = string.Empty,
            Size = null
        };

        Assert.True(ReleaseIdentity.Matches(columnIsWrong, listing));
        Assert.True(ReleaseIdentity.Matches(columnIsEmpty, listing));
        Assert.True(ReleaseIdentity.Matches(keyIsUnhelpful, listing));

        // The control: neither title agreeing still means no match, so this widened the match
        // rather than removing the title check.
        var neitherAgrees = new BlockedRelease
        {
            ReleaseIdentifier = ReleaseIdentity.KeyFor(null, "Quite Another Book")!,
            Title = "Unknown",
            Size = null
        };
        Assert.False(ReleaseIdentity.Matches(neitherAgrees, listing));
    }

    [Fact]
    public void Matches_ADifferentTitleEntirely_DoesNotMatch()
    {
        var entry = new BlockedRelease
        {
            ReleaseIdentifier = ReleaseIdentity.KeyFor(null, "Some Book Unabridged")!,
            Title = "Some Book Unabridged",
            Size = 734_003_200
        };

        Assert.False(ReleaseIdentity.Matches(
            entry, new SearchResult { Title = "Quite Another Book", Size = 734_003_200 }));
    }

    [Fact]
    public void Matches_ARowWithNothingUsable_MatchesNothing()
    {
        var empty = new BlockedRelease();

        Assert.False(ReleaseIdentity.Matches(
            empty, new SearchResult { Title = "Some Book Unabridged", Size = 1 }));
    }
}
