using Listenarr.Application.Search.Matching;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.Matching;

[Trait("Name", "ReleaseNarratorCreditTitleTests")]
[Trait("Category", "Application")]
public sealed class ReleaseNarratorCreditTitleTests : BaseTests
{
    private static readonly string[] Narrators = ["Greg Wise", "Dan Stevens"];

    private static bool Confirmed(string release, string title = "Dracula")
    {
        var book = new Audiobook { Title = title, Authors = ["Bram Stoker"], Narrators = [.. Narrators], Language = "English" };
        return new BookIdentityMatcher().Evaluate(
            BookMatchContext.From(book, catalog: TestAnnotationCatalog.Current), new() { ReleaseName = release }).IsConfirmed;
    }

    [Theory]
    [InlineData("Frankenstein - Narrated by Greg Wise, Dracula [M4B]")]
    [InlineData("Frankenstein - Narrated by Greg Wise - Dracula [M4B]")]
    [InlineData("Frankenstein - Narrated by Greg Wise, Dan Stevens / Dracula [M4B]")]
    [InlineData("Frankenstein - Read by Greg Wise, Dracula [M4B]")]
    [InlineData("Frankenstein - Narrator: Greg Wise, Dracula [M4B]")]
    [InlineData("Frankenstein - Narrator : Greg Wise, Dracula [M4B]")]
    [InlineData("Bram Stoker - Narrated by Greg Wise, Dracula [M4B]")]
    [InlineData("Bram Stoker - Narrated by Greg Wise - Dracula [M4B]")]
    [InlineData("Bram Stoker - Read by Greg Wise, Dracula [M4B]")]
    [InlineData("Bram Stoker - Narrator: Greg Wise, Dracula [M4B]")]
    [InlineData("Bram Stoker - Narrated by Greg Wise, Dan Stevens / Dracula [M4B]")]
    [InlineData("Narrated by Greg Wise, Dracula by Bram Stoker [M4B]")]
    [InlineData("Narrated by Greg Wise - Dracula by Bram Stoker [M4B]")]
    [InlineData("Narrated by Greg Wise, Dan Stevens / Dracula by Bram Stoker [M4B]")]
    [InlineData("Read by Greg Wise, Dracula by Bram Stoker [M4B]")]
    [InlineData("Narrator: Greg Wise, Dracula by Bram Stoker [M4B]")]
    public void ATitleInsideAMultiNameNarratorCreditIsNotTheBook(string release)
    {
        Assert.False(Confirmed(release));
    }

    [Theory]
    [InlineData("Dracula by Bram Stoker - Narrated by Greg Wise [M4B]")]
    [InlineData("Dracula by Bram Stoker - Narrated by Greg Wise, Dan Stevens [M4B]")]
    [InlineData("Dracula - Bram Stoker - Narrator: Greg Wise, Dan Stevens [M4B]")]
    [InlineData("Bram Stoker - Dracula - Narrated by Greg Wise [M4B]")]
    [InlineData("Dracula by Bram Stoker [Narrated by Greg Wise] [M4B]")]
    [InlineData("[Narrated by Greg Wise] Dracula by Bram Stoker [M4B]")]
    [InlineData("Narrated by Greg Wise [M4B] Dracula by Bram Stoker")]
    [InlineData("Narrated by Greg Wise; Dracula by Bram Stoker [M4B]")]
    public void ATitleOutsideTheNarratorCreditStillMatches(string release)
    {
        Assert.True(Confirmed(release));
    }
}
