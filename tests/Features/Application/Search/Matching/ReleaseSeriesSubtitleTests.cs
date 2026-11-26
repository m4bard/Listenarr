using Listenarr.Tests.Common;
using Listenarr.Application.Search.Matching;

namespace Listenarr.Tests.Features.Application.Search.Matching;

[Trait("Name", "ReleaseSeriesSubtitleTests")]
[Trait("Category", "Application")]
public sealed class ReleaseSeriesSubtitleTests : BaseTests
{
    private static Audiobook Book(string? subtitle = "Northern Shore Season 2, Book 3", string? series = "Northern Shore", string? position = "3") => new()
    { Title = "Orchard", Subtitle = subtitle, Authors = ["Marisol Vega"], Series = series, SeriesNumber = position };
    private static BookIdentityAssessment Match(Audiobook book, string release) => new BookIdentityMatcher().Evaluate(
        BookMatchContext.From(book, catalog: TestAnnotationCatalog.Current), new() { ReleaseName = release });

    [Theory]
    [InlineData("Marisol Vega - Northern Shore - Orchard 2010")]
    [InlineData("Marisol Vega - Northern Shore - Orchard")]
    [InlineData("Marisol Vega - Northern Shore Season 2 - Orchard 2010")]
    [InlineData("Marisol Vega - Northern Shore, Book 3 - Orchard 2010")]
    [InlineData("Marisol Vega - Northern Shore Season 2, Book 3 - Orchard 2010")]
    [InlineData("Marisol Vega - Orchard 2010")]
    public void SeriesOnlySubtitleDoesNotHideTheSeriesNamedInTheRelease(string release)
    {
        var result = Match(Book(series: null, position: null), release);
        Assert.True(result.IsConfirmed, result.Status + " " + result.Reason);
    }

    [Theory]
    [InlineData("Marisol Vega - Northern Shore, Book 4 - Orchard 2010", "series_position_conflict")]
    [InlineData("Marisol Vega - Northern Shore 4 - Orchard 2010", "series_position_conflict")]
    [InlineData("Marisol Vega - Other Coast - Orchard 2010", "unexplained_release_text")]
    [InlineData("Other Writer - Northern Shore - Orchard 2010", "unexplained_release_text")]
    [InlineData("Marisol Vega - Northern Shore - Orchard Grove 2010", "unexplained_release_text")]
    [InlineData("Marisol Vega - Northern Shore - Grove 2010", "complete_title_not_found")]
    public void SeasonlessSeriesSpellingStaysBoundToTheStoredBookAndTitle(string release, string reason)
    {
        var result = Match(Book(series: null, position: null), release);
        Assert.False(result.IsConfirmed);
        Assert.Equal(reason, result.Reason);
    }

    [Fact]
    public void StoredSeriesPositionIsNotOverriddenBySubtitleSeason()
    {
        var result = Match(Book(series: "Northern Shore", position: "2"), "Marisol Vega - Northern Shore, Book 3 - Orchard 2010");
        Assert.False(result.IsConfirmed);
        Assert.Equal("series_position_conflict", result.Reason);
    }

    [Fact]
    public void ProviderSuppliedDifferentAuthorStillConflicts()
    {
        var evidence = new CandidateIdentityEvidence
        {
            ReleaseName = "Marisol Vega - Northern Shore - Orchard 2010",
            Authors = [new("Other Writer", IdentityEvidenceOrigin.ProviderSupplied)]
        };
        var result = new BookIdentityMatcher().Evaluate(
            BookMatchContext.From(Book(series: null, position: null), catalog: TestAnnotationCatalog.Current), evidence);
        Assert.Equal("author_conflict", result.Reason);
    }

    [Theory]
    [InlineData("A Gentle Descriptive Subtitle", "Marisol Vega - Orchard 2010", true)]
    [InlineData("A Gentle Descriptive Subtitle", "Marisol Vega - Orchard - A Gentle Descriptive Subtitle 2010", true)]
    [InlineData("A Gentle Descriptive Subtitle", "Marisol Vega - Orchard - A Different Subtitle 2010", false)]
    [InlineData("A Gentle Descriptive Subtitle", "Marisol Vega - Northern Shore - Orchard 2010", true)]
    [InlineData("Season 2", "Marisol Vega - Northern Shore - Orchard 2010", true)]
    [InlineData("Book 3 of 5", "Marisol Vega - Northern Shore - Orchard 2010", true)]
    public void OtherSubtitleShapesKeepTheirBehaviourWhenTheSeriesIsStored(string subtitle, string release, bool expected) =>
        Assert.Equal(expected, Match(Book(subtitle), release).IsConfirmed);
}
