using Microsoft.Extensions.Logging.Abstractions;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.Matching;

[Trait("Name", "EditionSelectionTests")]
[Trait("Category", "Application")]
public sealed class EditionSelectionTests : BaseTests
{
    private const int RequestedScore = 100;
    private const int AlternativeScore = 900;

    private static SearchResult Release(string id, string release) => new()
    {
        Id = id,
        Title = release,
        Format = "M4B",
        Quality = "M4B",
        Size = 100 * 1024 * 1024,
        IdentityEvidence = new() { ReleaseName = release }
    };

    private static Audiobook Book(string? subtitle) => new()
    { Title = "A Princess of Mars", Subtitle = subtitle, Authors = ["Edgar Rice Burroughs"], Asin = "B008DFUGCQ" };

    private static async Task<string?> SelectedId(Audiobook book, params (SearchResult Result, int Score)[] candidates)
    {
        var scores = candidates.ToDictionary(c => c.Result, c => c.Score, ReferenceEqualityComparer.Instance);
        var quality = new Mock<IQualityProfileService>();
        quality.Setup(q => q.ScoreSearchResults(It.IsAny<List<SearchResult>>(), It.IsAny<QualityProfile>()))
            .ReturnsAsync((List<SearchResult> results, QualityProfile _) => results.Select(r =>
                new QualityScore { SearchResult = r, TotalScore = (int)scores[r] }).ToList());
        var scorer = new AutomaticSearchScoringService(quality.Object, Mock.Of<IAudiobookRepository>(),
            NullLogger<AutomaticSearchScoringService>.Instance, TestAnnotationCatalog.CreateProvider());
        var assessment = await scorer.ScoreAsync(book, candidates.Select(c => c.Result).ToList(), new QualityProfile());
        return scorer.SelectBest(assessment)?.SearchResult.Id;
    }

    [Theory]
    [InlineData("[Abridged]")]
    [InlineData("[Dramatized Adaptation]")]
    public async Task RequestedEditionBeatsAHigherScoringAlternativeEdition(string alternativeEdition)
    {
        var requested = Release("requested", "Edgar Rice Burroughs - A Princess of Mars [Unabridged] [M4B]");
        var alternative = Release("alternative", $"Edgar Rice Burroughs - A Princess of Mars {alternativeEdition} [M4B]");

        var selected = await SelectedId(Book("Unabridged"), (requested, RequestedScore), (alternative, AlternativeScore));

        Assert.Equal("requested", selected);
    }

    [Fact]
    public async Task RequestedEditionBeatsHigherScoringAbridgedAndDramatizedAlternatives()
    {
        var requested = Release("requested", "Edgar Rice Burroughs - A Princess of Mars [Unabridged] [M4B]");
        var abridged = Release("abridged", "Edgar Rice Burroughs - A Princess of Mars [Abridged] [M4B]");
        var dramatized = Release("dramatized", "Edgar Rice Burroughs - A Princess of Mars [Dramatized Adaptation] [M4B]");

        var selected = await SelectedId(Book("Unabridged"),
            (requested, RequestedScore), (abridged, AlternativeScore), (dramatized, AlternativeScore + 1));

        Assert.Equal("requested", selected);
    }

    [Fact]
    public async Task WhenBothReleasesAreEligibleTheHigherScoreWins()
    {
        var lower = Release("lower", "Edgar Rice Burroughs - A Princess of Mars [Unabridged] [M4B]");
        var higher = Release("higher", "Edgar Rice Burroughs - A Princess of Mars [Unabridged] [MP3]");

        var selected = await SelectedId(Book("Unabridged"), (lower, RequestedScore), (higher, AlternativeScore));

        Assert.Equal("higher", selected);
    }
}
