using Microsoft.Extensions.Logging.Abstractions;
using Listenarr.Tests.Common;
using Listenarr.Application.Search.Matching;

namespace Listenarr.Tests.Features.Application.Search.Matching;

[Trait("Name", "ScoringPathProbeTests")]
[Trait("Category", "Application")]
public sealed class ScoringPathProbeTests : BaseTests
{
    private const string Rel = "Marisol.Vega.-.Gardeners.-.Wordone.2021";

    private static Audiobook Real(Action<Audiobook>? tweak = null)
    {
        var b = new Audiobook
        {
            Id = 7, Title = "Wordone", Subtitle = "Gardener's Season 2, Book 1",
            Authors = ["Marisol Vega", "Ramon de Ocampo"], Narrators = ["Ada Quill"], Language = "English", Asin = "B000000001",
            Series = "Gardener's", SeriesNumber = "5", Monitored = true, QualityProfileId = 1, QualityProfile = new() { Id = 1 }
        };
        tweak?.Invoke(b);
        return b;
    }

    private static async Task<string> Run(Audiobook book, SearchResult candidate)
    {
        var quality = new Mock<IQualityProfileService>();
        quality.Setup(q => q.ScoreSearchResults(It.IsAny<List<SearchResult>>(), It.IsAny<QualityProfile>()))
            .ReturnsAsync((List<SearchResult> c, QualityProfile _) => c.Select(r => new QualityScore { SearchResult = r, TotalScore = 67 }).ToList());
        var scorer = new AutomaticSearchScoringService(quality.Object, Mock.Of<IAudiobookRepository>(),
            NullLogger<AutomaticSearchScoringService>.Instance, TestAnnotationCatalog.CreateProvider());
        var a = await scorer.ScoreAsync(book, [candidate], book.QualityProfile!);
        var s = a.Candidates.Single();
        return $"{s.Identity.Status} {s.Identity.Reason} eligible={!s.IsRejected} [{string.Join(",", s.Compatibility.Reasons)}]";
    }

    private static SearchResult Usenet(string title = Rel, bool evidence = true) => new()
    {
        Id = Guid.NewGuid().ToString(), Title = title, Artist = "Unknown Author", Source = "idx", IndexerId = 1, Size = 1_100_000_000,
        DownloadType = "Usenet", NzbUrl = "http://x/y.nzb", Format = "MP3", Quality = "MP3",
        IdentityEvidence = evidence ? new() { ReleaseName = title, Authors = [] } : null
    };

    public static IEnumerable<object[]> Cases() =>
    [
        ["base", (Action<Audiobook>)(_ => { }), Usenet()],
        ["no-evidence-object", (Action<Audiobook>)(_ => { }), Usenet(evidence: false)],
        ["no-subtitle", (Action<Audiobook>)(b => b.Subtitle = null), Usenet()],
        ["no-series-fields", (Action<Audiobook>)(b => { b.Series = null; b.SeriesNumber = null; }), Usenet()],
        ["membership-only", (Action<Audiobook>)(b => { b.Series = null; b.SeriesNumber = null; b.SeriesMemberships = [new() { SeriesName = "Gardener's", SeriesNumber = "5", IsPrimary = true }]; }), Usenet()],
        ["no-language", (Action<Audiobook>)(b => b.Language = null), Usenet()],
        ["no-asin", (Action<Audiobook>)(b => b.Asin = null), Usenet()],
        ["no-narrator", (Action<Audiobook>)(b => b.Narrators = null), Usenet()],
        ["one-author", (Action<Audiobook>)(b => b.Authors = ["Marisol Vega"]), Usenet()],
        ["other-author-first", (Action<Audiobook>)(b => b.Authors = ["Ramon de Ocampo", "Marisol Vega"]), Usenet()],
        ["no-authors", (Action<Audiobook>)(b => b.Authors = null), Usenet()],
        ["author-evidence-release-derived", (Action<Audiobook>)(_ => { }), new SearchResult { Id = "a", Title = Rel, Artist = "Marisol Vega", IndexerId = 1, IdentityEvidence = new() { ReleaseName = Rel, Authors = [new("Marisol Vega", IdentityEvidenceOrigin.ReleaseNameDerived)] } }],
        ["author-evidence-provider-other", (Action<Audiobook>)(_ => { }), new SearchResult { Id = "a", Title = Rel, IndexerId = 1, IdentityEvidence = new() { ReleaseName = Rel, Authors = [new("Other Writer", IdentityEvidenceOrigin.ProviderSupplied)] } }],
        ["release-name-differs-from-title", (Action<Audiobook>)(_ => { }), new SearchResult { Id = "a", Title = Rel, IndexerId = 1, IdentityEvidence = new() { ReleaseName = "Marisol.Vega.-.Gardeners", Authors = [] } }],
        ["apostrophe-release", (Action<Audiobook>)(_ => { }), Usenet("Marisol.Vega.-.Gardener's.-.Wordone.2021")],
        ["one-author-release-spaced", (Action<Audiobook>)(_ => { }), Usenet("Marisol Vega - Gardeners - Wordone 2021")],
        ["no-year", (Action<Audiobook>)(_ => { }), Usenet("Marisol.Vega.-.Gardeners.-.Wordone")],
        ["lang-token", (Action<Audiobook>)(_ => { }), Usenet("Marisol.Vega.-.Gardeners.-.Wordone.2021.GERMAN")],
        ["isbn", (Action<Audiobook>)(b => b.Isbn = ["9780000000002"]), Usenet()],
        ["edition-fields", (Action<Audiobook>)(b => { b.Edition = "Unabridged"; b.Version = "1"; b.Abridged = false; }), Usenet()],
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Probe(string name, Action<Audiobook> tweak, SearchResult candidate) =>
        Assert.Fail("RESULT " + name + " => " + await Run(Real(tweak), candidate));
}
