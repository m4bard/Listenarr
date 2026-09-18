using System.Reflection;
using System.Text.RegularExpressions;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Downloads;

// A release key is PERSISTED. It is written into the blocklist when a release fails and is the
// uniqueness rule for a row, so its wire format is a storage contract and not an implementation
// detail. Change the prefix, the case folding, the whitespace rule or the Unicode form and every
// row already in the database becomes unreachable by its primary key.
//
// That is not hypothetical. The blocklist has had four defects and all four were one defect, a
// key derived one way in one place and another way somewhere else. The fix for that was to give
// one class sole ownership of the format. This file guards the other half: what that one owner is
// allowed to change about the format without anybody noticing.
//
// The relational assertions live next door in ReleaseIdentityTests: same release gives the same
// key, a different torrent does not match, size carries tolerance. All of those still pass if the
// case folding is dropped, if the whitespace collapse changes, or if the NFC pass is removed,
// because they compare two outputs of the same build against each other. These vectors are the
// statement those tests cannot make.
[Trait("Name", "ReleaseIdentityGoldenVectorTests")]
[Trait("Category", "Domain")]
public sealed class ReleaseIdentityGoldenVectorTests : BaseTests
{
    // The base32 and hex spellings of one torrent, as Sonarr pins them in its own fixtures at
    // src/NzbDrone.Core.Test/Download/DownloadClientTests/QBittorrentTests/QBittorrentFixture.cs:469.
    private const string Base32Hash = "ZPBPA2P6ROZPKRHK44D5OW6NHXU5Z6KR";
    private const string HexHash = "CBC2F069FE8BB2F544EAE707D75BCD3DE9DCF951";
    private const string ExpectedHashKey = "btih:cbc2f069fe8bb2f544eae707d75bcd3de9dcf951";

    // Each vector pins one branch and one normalisation rule to an exact output, through the
    // production entry point ReleaseIdentity.For(SearchResult) rather than an internal helper, so
    // a change anywhere between the magnet and the stored key lands here.
    public static TheoryData<string, string?, string?, string> Vectors() => new()
    {
        {
            "btih, hex magnet, folded to lower case",
            $"magnet:?xt=urn:btih:{HexHash}&tr=udp", "Some Book Title", ExpectedHashKey
        },
        {
            "btih, the base32 spelling of that same torrent, which must not become a second key",
            $"magnet:?xt=urn:btih:{Base32Hash}&tr=udp", "Some Book Title", ExpectedHashKey
        },
        {
            "btih, percent-encoded marker, which a raw substring search misses entirely",
            $"magnet:?xt=urn%3Abtih%3A{HexHash}&dn=x", "Some Book Title", ExpectedHashKey
        },
        {
            "btih, indexed parameter name, same marker once decoded",
            $"magnet:?xt.1=urn:btih:{HexHash}", "Some Book Title", ExpectedHashKey
        },
        {
            "title, a release name shaped the way indexers actually shape one",
            null,
            "Some.Book.Title.2019.Unabridged.M4B.64k-GROUP [eng]",
            "title:some.book.title.2019.unabridged.m4b.64k-group [eng]"
        },
        {
            "title, an apostrophe, brackets and braces carried through unchanged",
            null,
            "Author's Tale (Narrator) {B08XYZ1234}",
            "title:author's tale (narrator) {b08xyz1234}"
        },
        {
            "title, runs of whitespace collapsed and case folded",
            null, "  Some   BOOK   Title  ", "title:some book title"
        },
        {
            "title, an accent already in composed form",
            null, "Les Mis\u00E9rables Unabridged", "title:les mis\u00E9rables unabridged"
        },
        {
            "title, the same accent decomposed, which must compose to the identical key",
            null, "Les Mise\u0301rables Unabridged", "title:les mis\u00E9rables unabridged"
        },
        {
            "title, a magnet with an empty hash mints no hash key and the title carries it",
            "magnet:?xt=urn:btih:&dn=x", "Some Book Title", "title:some book title"
        },
        {
            "title, a magnet whose hash is the wrong length is not a hash",
            "magnet:?xt=urn:btih:ABCDEF&dn=x", "Some Book Title", "title:some book title"
        },
    };

    [Theory]
    [MemberData(nameof(Vectors))]
    public void For_ProducesTheExactPersistedKey(
        string because, string? magnetLink, string? title, string expected)
    {
        var result = new SearchResult
        {
            MagnetLink = magnetLink ?? string.Empty,
            Title = title ?? string.Empty
        };

        Assert.Equal(expected, ReleaseIdentity.For(result));
        Assert.False(string.IsNullOrWhiteSpace(because));
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void EveryPinnedKey_CarriesADeclaredPrefix(
        string because, string? magnetLink, string? title, string expected)
    {
        _ = because;
        _ = magnetLink;
        _ = title;

        Assert.Contains(
            ReleaseIdentity.KeyPrefixes,
            prefix => expected.StartsWith(prefix, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryDeclaredPrefix_HasAGoldenVector()
    {
        // The part the vectors cannot cover on their own. A new branch emitting a key under a
        // prefix nobody pinned would ship with no vector and nothing would notice, which is the
        // specific hole this file was asked to close.
        var covered = Vectors()
            .Select(row => (string)row[3]!)
            .Select(key => key[..(key.IndexOf(':') + 1)])
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(ReleaseIdentity.KeyPrefixes.OrderBy(p => p, StringComparer.Ordinal), covered.OrderBy(p => p, StringComparer.Ordinal));
    }

    // The shape the two tests above cannot see: a branch that returns a prefix as a bare inline
    // literal, with no named constant to reflect over. Measured, not assumed: adding
    // `return "nzbid:" + normalizedTitle;` to KeyFor ships green across all 3,326 tests with only
    // the reflection test in place.
    //
    // So this is a belt beside them rather than a replacement for either. It is the old
    // source-regex guard with its two known holes closed: `[a-z0-9]+` instead of `[a-z]+`, because
    // btih2 for BitTorrent v2 hashes is a realistic addition that digits excluded, and an optional
    // `$` with no closing quote required, so an interpolated key is caught too. What it still
    // cannot see is an extracted helper or a composed return, which is exactly what the reflection
    // test covers, and that division is the reason both are here.
    //
    // Zero findings is the correct state today, because every branch returns one of the declared
    // constants. A test whose pass state is "found nothing" has to prove its apparatus works, or a
    // broken regex and a clean file look identical, so the first assertion runs the same pattern
    // over a sample that must match.
    [Fact]
    public void NoBranch_ReturnsAKeyPrefixAsAnUndeclaredInlineLiteral()
    {
        const string sample = """
            if (x) { return "nzbid:" + y; }
            if (z) { return $"btih2:{w}"; }
            """;

        Assert.Equal(
            ["btih2:", "nzbid:"],
            LiteralPrefixesIn(sample).Order(StringComparer.Ordinal).ToArray());

        var source = File.ReadAllText(Path.Join(
            TestUtils.FindRepositoryRoot(), "listenarr.domain", "Downloads", "ReleaseIdentity.cs"));

        var undeclared = LiteralPrefixesIn(source)
            .Where(prefix => !ReleaseIdentity.KeyPrefixes.Contains(prefix))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            undeclared.Length == 0,
            "These key prefixes are returned as inline literals and are not declared in "
            + "ReleaseIdentity.KeyPrefixes, so nothing requires a golden vector for them: "
            + string.Join(", ", undeclared));
    }

    private static IEnumerable<string> LiteralPrefixesIn(string source) =>
        Regex.Matches(source, @"return\s+\$?""(?<prefix>[a-z0-9]+):")
            .Select(match => match.Groups["prefix"].Value + ":")
            .Distinct(StringComparer.Ordinal);

    [Fact]
    public void EveryPrefixConstantOnTheType_IsDeclaredInTheKeyPrefixSet()
    {
        // Reflection over the type rather than a regex over its source text. The realistic way a
        // prefix escapes the set is somebody adding a `const string SomethingPrefix` next to the
        // two that exist, writing a branch that returns it, and not touching the set or this file.
        // A source scan for string literals also has to guess at interpolation, extracted helpers
        // and prefixes containing a digit, and guesses wrong quietly; this does not.
        var declared = typeof(ReleaseIdentity)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral
                         && field.FieldType == typeof(string)
                         && field.Name.EndsWith("Prefix", StringComparison.Ordinal))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(declared);
        Assert.Equal(
            ReleaseIdentity.KeyPrefixes.OrderBy(p => p, StringComparer.Ordinal),
            declared.OrderBy(p => p, StringComparer.Ordinal));
    }
}
