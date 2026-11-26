using Listenarr.Tests.Common;
using Listenarr.Application.Search.Matching;

namespace Listenarr.Tests.Features.Application.Search.Matching;

[Trait("Name", "SeriesLabelSubtitleShapeTests")]
[Trait("Category", "Application")]
public sealed class SeriesLabelSubtitleShapeTests : BaseTests
{
    private static Audiobook Book(string? subtitle = "Gardener's Season 2, Book 1", string? series = "Gardener's", string? position = "5") => new()
    {
        Title = "Wordone",
        Subtitle = subtitle,
        Authors = ["Marisol Vega", "Ramon de Ocampo"],
        SeriesMemberships = series == null ? [] : [new() { SeriesName = series, SeriesNumber = position }]
    };
    private static BookIdentityAssessment Match(Audiobook book, string release) => new BookIdentityMatcher().Evaluate(
        BookMatchContext.From(book, catalog: TestAnnotationCatalog.Current), new() { ReleaseName = release });

    [Theory]
    [InlineData("Marisol.Vega.-.Gardeners.-.Wordone.2021")]
    [InlineData("Marisol.Vega.-.Gardener's.-.Wordone.2021")]
    [InlineData("Marisol.Vega.-.Wordone.2021")]
    [InlineData("Ramon.de.Ocampo.-.Gardeners.-.Wordone.2021")]
    public void StoredSeriesWithWholeSeriesPositionAcceptsReleaseWithoutSeasonText(string release)
    {
        var result = Match(Book(), release);
        Assert.True(result.IsConfirmed, result.Status + " " + result.Reason);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("5")]
    [InlineData("")]
    public void SubtitlePositionAndStoredPositionAreIndependent(string stored) =>
        Assert.True(Match(Book(position: stored), "Marisol.Vega.-.Gardeners.-.Wordone.2021").IsConfirmed);

    [Theory]
    [InlineData("Marisol.Vega.-.Gardeners.6.-.Wordone.2021", "5")]
    [InlineData("Marisol.Vega.-.Other.Coast.-.Wordone.2021", "5")]
    [InlineData("Other.Writer.-.Gardeners.-.Wordone.2021", "5")]
    [InlineData("Marisol.Vega.-.Gardeners.-.Wordone.Grove.2021", "5")]
    [InlineData("Marisol.Vega.-.Gardeners.-.Word.2021", "5")]
    public void WrongNumberSeriesAuthorOrTitleStillRejects(string release, string stored) =>
        Assert.False(Match(Book(position: stored), release).IsConfirmed);

    [Fact]
    public void RealPositionConflictWithoutLabelSubtitleStillRejects() =>
        Assert.False(Match(Book(subtitle: null, position: "5"), "Marisol.Vega.-.Gardeners.6.-.Wordone.2021").IsConfirmed);
}
