using Listenarr.Application.Search.Matching;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.Matching;

[Trait("Name", "ReleaseNarratorCreditTitleTests")]
[Trait("Category", "Application")]
public sealed class ReleaseNarratorCreditTitleTests : BaseTests
{
    private static readonly string[] Narrators = ["Greg Wise", "Dan Stevens"];

    private static bool Confirmed(string release, string title = "Dracula", string[]? authors = null)
    {
        var book = new Audiobook { Title = title, Authors = [.. authors ?? ["Bram Stoker"]], Narrators = [.. Narrators], Language = "English" };
        return new BookIdentityMatcher().Evaluate(
            BookMatchContext.From(book, catalog: TestAnnotationCatalog.Current), new() { ReleaseName = release }).IsConfirmed;
    }

    // Regression guards: these were already rejected before the credit walk changed, because the unexplained
    // leading title leaves the release incomplete. They pin that this stays true.
    [Theory]
    [InlineData("Frankenstein - Narrated by Greg Wise, Dracula [M4B]")]
    [InlineData("Frankenstein - Narrated by Greg Wise - Dracula [M4B]")]
    [InlineData("Frankenstein - Narrated by Greg Wise, Dan Stevens / Dracula [M4B]")]
    [InlineData("Frankenstein - Read by Greg Wise, Dracula [M4B]")]
    [InlineData("Frankenstein - Narrator: Greg Wise, Dracula [M4B]")]
    [InlineData("Frankenstein - Narrator : Greg Wise, Dracula [M4B]")]
    public void GuardAnUnexplainedLeadingTitleBeforeACreditIsRejected(string release)
    {
        Assert.False(Confirmed(release));
    }

    [Theory]
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

    // The credit walk stops at any character a credit name cannot contain, so a field between the credit and
    // the title separates them.
    [Theory]
    [InlineData("Bram Stoker - Narrated by Greg Wise – Dracula [M4B]")]
    [InlineData("Bram Stoker - Narrated by Greg Wise - 2019 - Dracula [M4B]")]
    [InlineData("Bram Stoker - Narrated by Greg Wise, 2019, Dracula [M4B]")]
    [InlineData("Bram Stoker - Narrated by Greg Wise, Dan Stevens; Dracula [M4B]")]
    [InlineData("Bram Stoker - Narrated by Greg Wise [M4B] - Dracula")]
    [InlineData("Narrated by Greg Wise [M4B] - Dracula by Bram Stoker")]
    public void AFieldThatIsNotACreditNameEndsTheCreditBeforeTheTitle(string release)
    {
        Assert.True(Confirmed(release));
    }

    // Judgement call: a letters-only field (Unabridged) between the credit and the title cannot be told apart
    // from a further credit name, so the title is still treated as inside the credit and the release is missed.
    [Theory]
    [InlineData("Bram Stoker - Narrated by Greg Wise - Unabridged - Dracula [M4B]")]
    [InlineData("Bram Stoker - Read by Greg Wise - Unabridged - Dracula [M4B]")]
    public void ALettersOnlyFieldAfterTheCreditDoesNotEndTheCredit(string release)
    {
        Assert.False(Confirmed(release));
    }

    // The keyword lies between the leading bracket and the title, so the credit leads and the title is inside it.
    [Fact]
    public void ACreditAfterALeadingBracketStillReachesTheTitle()
    {
        Assert.False(Confirmed("[M4B] Narrated by Greg Wise - Dracula by Bram Stoker"));
    }

    // The walk crosses '&' and '.', so a narrator pair or an initialled narrator does not end the credit early.
    [Theory]
    [InlineData("Bram Stoker - Narrated by Greg Wise & Dracula [M4B]")]
    [InlineData("Bram Stoker - Narrated by Greg Wise & Dan Stevens, Dracula [M4B]")]
    [InlineData("Bram Stoker - Narrated by Greg Wise, Dan Stevens & Dracula [M4B]")]
    [InlineData("Narrated by Greg Wise & Dan Stevens - Dracula by Bram Stoker [M4B]")]
    [InlineData("Bram Stoker - Narrated by B.J. Harrison, Dracula [M4B]")]
    [InlineData("Bram Stoker - Narrated by Greg Wise, B.J. Harrison / Dracula [M4B]")]
    [InlineData("Bram Stoker - Narrated by John W. Michaels - Dracula [M4B]")]
    public void ATitleAfterAPairOrInitialledNarratorIsNotTheBook(string release)
    {
        Assert.False(Confirmed(release));
    }

    [Theory]
    [InlineData("Dracula by Bram Stoker & Mary Shelley - Narrated by Greg Wise [M4B]")]
    [InlineData("Dracula by Bram Stoker & Mary Shelley - Narrated by Greg Wise, Dan Stevens [M4B]")]
    public void AnAuthorPairBeforeTheCreditStillMatches(string release)
    {
        Assert.True(Confirmed(release, authors: ["Bram Stoker", "Mary Shelley"]));
    }
}
