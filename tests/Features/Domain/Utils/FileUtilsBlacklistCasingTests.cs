using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Utils;

[Trait("Name", "FileUtilsBlacklistCasingTests")]
[Trait("Category", "FileUtils")]
public sealed class FileUtilsBlacklistCasingTests : BaseTests
{
    // The two sides of this comparison come from different places and there is nothing
    // that forces them to agree on case: the file extension is read off disk, the
    // blacklist entries are typed by a user into settings. Every case below therefore
    // pairs a deliberately mismatched file and blacklist entry.

    [Theory]
    [InlineData("notes.txt", ".TXT")]
    [InlineData("notes.TXT", ".txt")]
    [InlineData("notes.TxT", ".tXt")]
    [InlineData("notes.NFO", ".nfo")]
    public void IsBlacklistedFile_BlocksWhenOnlyTheCasingDiffers(string fileName, string blacklistEntry)
    {
        Assert.True(FileUtils.IsBlacklistedFile(fileName, [blacklistEntry]));
    }

    [Theory]
    [InlineData("notes.txt", ".txt")]
    [InlineData("notes.TXT", ".TXT")]
    public void IsBlacklistedFile_StillBlocksWhenTheCasingMatches(string fileName, string blacklistEntry)
    {
        Assert.True(FileUtils.IsBlacklistedFile(fileName, [blacklistEntry]));
    }

    [Theory]
    [InlineData("book.mp3", ".txt")]
    [InlineData("book.MP3", ".TXT")]
    public void IsBlacklistedFile_LeavesUnlistedExtensionsAlone(string fileName, string blacklistEntry)
    {
        Assert.False(FileUtils.IsBlacklistedFile(fileName, [blacklistEntry]));
    }

    // ApplicationSettings spreads the normalized OrdinalIgnoreCase set into a List<string>,
    // which drops the comparer. This reproduces the shape a production caller actually
    // passes rather than relying on a set that happens to carry the right comparer.
    [Fact]
    public void IsBlacklistedFile_BlocksWhenTheBlacklistArrivesAsAPlainList()
    {
        List<string> blacklist = [.. FileUtils.NormalizeExtensions([".TXT", ".NFO"])];

        Assert.True(FileUtils.IsBlacklistedFile("notes.txt", blacklist));
        Assert.True(FileUtils.IsBlacklistedFile("readme.nfo", blacklist));
    }

    [Theory]
    [InlineData("partial.tmp")]
    [InlineData("partial.TMP")]
    public void IsBlacklistedFile_KeepsBlockingTempFilesRegardlessOfCasing(string fileName)
    {
        Assert.True(FileUtils.IsBlacklistedFile(fileName, []));
        Assert.True(FileUtils.IsBlacklistedFile(fileName, null));
    }
}
