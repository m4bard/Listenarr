/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Security.Cryptography;
using System.Text;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Downloads.Submission;

/// <summary>
/// Covers the check that a release chosen without a person looking at it is for the book that
/// was searched for.
/// </summary>
/// <remarks>
/// <para>
/// The defect: nothing on the automatic grab paths compared a release with the book. The scorer
/// sees only the release and the quality profile, so the top-scored release was grabbed even when
/// it was plainly another work. On a live install that grabbed a different author's book whose
/// title merely contained the requested book's one-word title, and on another occasion copied a
/// different book's content into the requested book's folder.
/// </para>
/// <para>
/// Every book and author here is public domain. The decoys are real public-domain works picked
/// for the same shape as the incidents: a short title that another work's title contains, by a
/// different author. The real titles involved are not reproduced.
/// </para>
/// </remarks>
[Trait("Area", "Search")]
[Trait("Name", "RequestedBookReleaseFilterTests")]
[Trait("Category", "Application")]
public sealed class RequestedBookReleaseFilterTests : BaseTests
{
    // ------------------------------------------------------------------
    // Known good: a release for the book is accepted in the shapes indexers actually use.
    // ------------------------------------------------------------------

    [Theory]
    [Trait("Method", "Evaluate")]
    [Trait("Scenario", "KnownGood")]
    // Torznab style, "Author - Title", with edition and format noise
    [InlineData("Arthur Conan Doyle - The Hound of the Baskervilles [Unabridged] (2012) M4B")]
    // Scene style, dots for spaces
    [InlineData("Arthur.Conan.Doyle-The.Hound.of.the.Baskervilles.Unabridged.2012.MP3")]
    // Series prefix and the author at the end
    [InlineData("Sherlock Holmes 05 - The Hound of the Baskervilles - Arthur Conan Doyle")]
    // Case and a dropped article
    [InlineData("ARTHUR CONAN DOYLE - HOUND OF THE BASKERVILLES")]
    public void Evaluate_ReleaseForTheRequestedBook_IsAccepted(string releaseTitle)
    {
        // Given
        var book = Book("The Hound of the Baskervilles", "Arthur Conan Doyle");

        // When
        var verdict = RequestedBookReleaseFilter.Evaluate(book, Release(releaseTitle));

        // Then
        Assert.Equal(RequestedBookMatch.Accepted, verdict);
    }

    [Fact]
    [Trait("Method", "Evaluate")]
    [Trait("Scenario", "KnownGood")]
    public void Evaluate_AuthorOnlyInTheReleaseAuthorField_IsAccepted()
    {
        // Given: an indexer that carries the author as its own field and the bare book title as
        // the release title, written last-name-first.
        var book = Book("Emma", "Jane Austen");
        var release = Release("Emma", artist: "Austen, Jane");

        // When / Then
        Assert.Equal(RequestedBookMatch.Accepted, RequestedBookReleaseFilter.Evaluate(book, release));
    }

    // ------------------------------------------------------------------
    // Known bad, shaped like the incidents.
    // ------------------------------------------------------------------

    [Fact]
    [Trait("Method", "Evaluate")]
    [Trait("Scenario", "IncidentShape")]
    public void Evaluate_DifferentAuthorsBookWhoseTitleContainsTheShortTitle_IsRejected()
    {
        // Given: the requested book's title is one word, and another author's book has that word
        // in its own title. A title-containment check alone accepts this, which is the incident.
        var book = Book("Emma", "Jane Austen");
        var decoy = Release("Edna Ferber - Emma McChesney and Co [Unabridged] M4B");

        // When
        var verdict = RequestedBookReleaseFilter.Evaluate(book, decoy);

        // Then
        Assert.NotEqual(RequestedBookMatch.Accepted, verdict);

        // Control: the same shape of release for the requested book passes, so this is the author
        // deciding and not the title being unmatchable.
        Assert.Equal(
            RequestedBookMatch.Accepted,
            RequestedBookReleaseFilter.Evaluate(book, Release("Jane Austen - Emma [Unabridged] M4B")));
    }

    [Fact]
    [Trait("Method", "Evaluate")]
    [Trait("Scenario", "IncidentShape")]
    public void Evaluate_ReleaseAuthorFieldNamesSomebodyElse_IsRejectedAsAuthorMismatch()
    {
        // Given: author information on both sides, and they disagree. That is the clearest
        // non-match there is, whatever the title says.
        var book = Book("Emma", "Jane Austen");
        var decoy = Release("Emma McChesney and Co", artist: "Edna Ferber");

        // When / Then
        Assert.Equal(RequestedBookMatch.AuthorMismatch, RequestedBookReleaseFilter.Evaluate(book, decoy));
    }

    [Fact]
    [Trait("Method", "Evaluate")]
    [Trait("Scenario", "IncidentShape")]
    public void Evaluate_DistinctiveTitleButAuthorFieldNamesSomebodyElse_IsRejected()
    {
        // Given: a title long enough to be accepted on its own, but the release carries its own
        // author and it is not this one. The length of the title does not outvote that.
        var book = Book("The Picture of Dorian Gray", "Oscar Wilde");
        var release = Release("The Picture of Dorian Gray", artist: "Mark Twain");

        // When / Then
        Assert.Equal(RequestedBookMatch.AuthorMismatch, RequestedBookReleaseFilter.Evaluate(book, release));
    }

    [Theory]
    [Trait("Method", "Evaluate")]
    [Trait("Scenario", "UnrelatedTitle")]
    // Shares one of the two title words, different book
    [InlineData("R. M. Ballantyne - The Coral Island [Unabridged]")]
    // Same author, different book: the author alone does not make it the right release
    [InlineData("Robert Louis Stevenson - Kidnapped [Unabridged] MP3")]
    public void Evaluate_ReleaseForAnotherTitle_IsRejectedAsTitleMismatch(string releaseTitle)
    {
        // Given
        var book = Book("Treasure Island", "Robert Louis Stevenson");

        // When / Then
        Assert.Equal(RequestedBookMatch.TitleMismatch, RequestedBookReleaseFilter.Evaluate(book, Release(releaseTitle)));
    }

    // ------------------------------------------------------------------
    // Edge cases. Each says which way it was decided.
    // ------------------------------------------------------------------

    [Fact]
    [Trait("Method", "Evaluate")]
    [Trait("Scenario", "NoAuthorOnRelease")]
    public void Evaluate_DistinctiveTitleWithNoAuthorAnywhereOnTheRelease_IsAccepted()
    {
        // Given: three significant words, the same bar item 261 set for issuing a title on its
        // own. A release that matches all of them and names no author is accepted, because a
        // release name that omits the author is common and a title this specific is not shared.
        var book = Book("The Picture of Dorian Gray", "Oscar Wilde");

        // When / Then
        Assert.Equal(
            RequestedBookMatch.Accepted,
            RequestedBookReleaseFilter.Evaluate(book, Release("The Picture of Dorian Gray [Unabridged] MP3")));
    }

    [Theory]
    [Trait("Method", "Evaluate")]
    [Trait("Scenario", "ShortTitleNoAuthorOnRelease")]
    // The title and nothing else, with packaging noise
    [InlineData("Kim", "Kim [Unabridged] [M4B]")]
    [InlineData("Pride and Prejudice", "Pride & Prejudice [Unabridged]")]
    // A year beside the title is not another work
    [InlineData("Kim", "Kim (1901) MP3")]
    // Whatever follows the dash is as likely a narrator as an author
    [InlineData("Kim", "Kim - Alder Penrose")]
    public void Evaluate_ShortTitleAndAReleaseSegmentSayingExactlyThatTitle_IsAccepted(string title, string releaseTitle)
    {
        // Given: a title under three significant words, and a release that names no author but has
        // a segment that says the title and stops. What made the incident an incident was the
        // extra words: the decoy's title carried the book's title and then more of its own.
        var book = Book(title, "Rudyard Kipling");

        // When / Then
        Assert.Equal(RequestedBookMatch.Accepted, RequestedBookReleaseFilter.Evaluate(book, TorznabRelease(releaseTitle)));
    }

    [Theory]
    [Trait("Method", "Evaluate")]
    [Trait("Scenario", "ShortTitleNoAuthorOnRelease")]
    [InlineData("Emma McChesney and Co [Unabridged] M4B")]
    [InlineData("Emma.McChesney.and.Co.2014.MP3")]
    public void Evaluate_ShortTitleInsideALongerTitleWithNoAuthor_IsRejectedAsNotCorroborated(string releaseTitle)
    {
        // Given: the incident with the author left off the release entirely. No segment stops at
        // "Emma", so nothing says this is the requested book rather than one whose title starts
        // with the same word.
        var book = Book("Emma", "Jane Austen");

        // When / Then
        Assert.Equal(RequestedBookMatch.AuthorNotCorroborated, RequestedBookReleaseFilter.Evaluate(book, TorznabRelease(releaseTitle)));
    }

    [Fact]
    [Trait("Method", "Evaluate")]
    [Trait("Scenario", "TorznabDerivedAuthor")]
    public void Evaluate_AuthorFieldThatIsOnlyTheFrontOfTheTitle_IsNotTreatedAsAuthorInformation()
    {
        // Given: the Torznab parser fills the author field with whatever precedes " - " in the
        // title, so for a release named "Title - Narrator" the field holds the title. Reading that
        // as the release's author would reject every such release for a mismatch it never made.
        var book = Book("The Picture of Dorian Gray", "Oscar Wilde");
        var release = Release("The Picture of Dorian Gray - Unabridged", artist: "The Picture of Dorian Gray");

        // When / Then
        Assert.Equal(RequestedBookMatch.Accepted, RequestedBookReleaseFilter.Evaluate(book, release));
    }

    [Theory]
    [Trait("Method", "Evaluate")]
    [Trait("Scenario", "PlaceholderAuthor")]
    [InlineData("Unknown Author")]
    [InlineData("Unknown")]
    [InlineData("Various Authors")]
    public void Evaluate_PlaceholderAuthorOnRelease_IsNotTreatedAsAMismatch(string placeholder)
    {
        // Given: parsers write a placeholder when they found no author. It says nothing about who
        // wrote the release, so it must not count as disagreeing with the book.
        var book = Book("The Picture of Dorian Gray", "Oscar Wilde");
        var release = Release("The Picture of Dorian Gray", artist: placeholder);

        // When / Then
        Assert.Equal(RequestedBookMatch.Accepted, RequestedBookReleaseFilter.Evaluate(book, release));
    }

    [Fact]
    [Trait("Method", "Evaluate")]
    [Trait("Scenario", "NoAuthorOnBook")]
    public void Evaluate_BookWithNoAuthor_IsDecidedOnTheTitleAlone()
    {
        // Given: an anonymous work. There is nothing to corroborate with, so a short title is
        // accepted on the title, and a release carrying some author is not a mismatch.
        var book = Book("Beowulf", author: null);

        // When / Then
        Assert.Equal(RequestedBookMatch.Accepted, RequestedBookReleaseFilter.Evaluate(book, Release("Beowulf [Unabridged] MP3")));
        Assert.Equal(RequestedBookMatch.Accepted, RequestedBookReleaseFilter.Evaluate(book, Release("Beowulf", artist: "Francis Gummere")));

        // Control: the title is still checked
        Assert.Equal(RequestedBookMatch.TitleMismatch, RequestedBookReleaseFilter.Evaluate(book, Release("The Song of Roland")));
    }

    [Fact]
    [Trait("Method", "Evaluate")]
    [Trait("Scenario", "ReleaseTitleSuperset")]
    public void Evaluate_ReleaseTitleIsAStrictSupersetOfTheBookTitle_IsAccepted()
    {
        // Given: the release carries the subtitle, an edition note and a year the book record does not
        var book = Book("Frankenstein", "Mary Shelley");
        var release = Release("Mary Shelley - Frankenstein; or, The Modern Prometheus (1818 Text) [Unabridged]");

        // When / Then
        Assert.Equal(RequestedBookMatch.Accepted, RequestedBookReleaseFilter.Evaluate(book, release));
    }

    [Fact]
    [Trait("Method", "Evaluate")]
    [Trait("Scenario", "BookSubtitleReleaseWithout")]
    public void Evaluate_BookTitleHasASubtitleTheReleaseLeavesOut_IsAccepted()
    {
        // Given: the record carries the subtitle and the release only the main title
        var book = Book("Frankenstein: or, The Modern Prometheus", "Mary Wollstonecraft Shelley");

        // When / Then
        Assert.Equal(
            RequestedBookMatch.Accepted,
            RequestedBookReleaseFilter.Evaluate(book, Release("Mary Shelley - Frankenstein [m4b]")));

        // Control: the main title on its own is one word, so without the author it is held to
        // the short-title rule rather than borrowing the full title's length.
        Assert.Equal(
            RequestedBookMatch.AuthorNotCorroborated,
            RequestedBookReleaseFilter.Evaluate(book, Release("Frankenstein [m4b]")));
    }

    [Fact]
    [Trait("Method", "Evaluate")]
    [Trait("Scenario", "Normalisation")]
    public void Evaluate_DiacriticsInitialsAndPunctuationDiffer_IsAccepted()
    {
        // Given: accents on one side only, and initials written three different ways
        Assert.Equal(
            RequestedBookMatch.Accepted,
            RequestedBookReleaseFilter.Evaluate(
                Book("Les Misérables", "Victor Hugo"),
                Release("Victor Hugo - Les Miserables (Unabridged)")));

        Assert.Equal(
            RequestedBookMatch.Accepted,
            RequestedBookReleaseFilter.Evaluate(
                Book("The Time Machine", "H. G. Wells"),
                Release("HG Wells - The Time Machine")));

        Assert.Equal(
            RequestedBookMatch.Accepted,
            RequestedBookReleaseFilter.Evaluate(
                Book("The Time Machine", "H.G. Wells"),
                Release("The Time Machine", artist: "Wells, H. G.")));
    }

    [Fact]
    [Trait("Method", "Evaluate")]
    [Trait("Scenario", "BookRecordWithoutTitle")]
    public void Evaluate_BookRecordWithNoTitle_AcceptsRatherThanGuessing()
    {
        // Given: a record part way through a metadata refresh. There is nothing to compare, and
        // this check exists to reject clear non-matches, not to refuse whatever it cannot judge.
        var book = new AudiobookBuilder().WithAuthor("Jane Austen").Build();

        // When / Then
        Assert.Equal(RequestedBookMatch.Accepted, RequestedBookReleaseFilter.Evaluate(book, Release("Anything At All")));
    }

    // ------------------------------------------------------------------
    // Torznab-shaped releases: the parser sets the author field to whatever precedes the first
    // " - " of the title, or "Unknown Author" when there is no " - " at all.
    // ------------------------------------------------------------------

    [Fact]
    [Trait("Method", "Evaluate")]
    [Trait("Scenario", "TorznabAuthorPrefix")]
    public void Evaluate_TorznabPrefixNamesAnotherAuthorBeforeADistinctiveTitle_IsRejectedAsAuthorMismatch()
    {
        // Given: the release carries every word of a long title, and its prefix names a different
        // author. Comparing that prefix with the release's own title would always find it there,
        // since the parser cut it from that title.
        var book = Book("The Picture of Dorian Gray", "Oscar Wilde");

        // When / Then
        Assert.Equal(
            RequestedBookMatch.AuthorMismatch,
            RequestedBookReleaseFilter.Evaluate(book, TorznabRelease("Mark Twain - The Picture of Dorian Gray Parody")));
    }

    [Theory]
    [Trait("Method", "Evaluate")]
    [Trait("Scenario", "TorznabAuthorPrefix")]
    [InlineData("Edna Ferber - Emma McChesney and Co [Unabridged] M4B")]
    [InlineData("Emma McChesney and Co - Edna Ferber")]
    public void Evaluate_TorznabShapedIncident_IsRejected(string releaseTitle)
    {
        // Given: the incident as the parser actually delivers it, author first and author last
        var book = Book("Emma", "Jane Austen");

        // When / Then
        Assert.NotEqual(RequestedBookMatch.Accepted, RequestedBookReleaseFilter.Evaluate(book, TorznabRelease(releaseTitle)));
    }

    [Fact]
    [Trait("Method", "Evaluate")]
    [Trait("Scenario", "TorznabSeriesPrefix")]
    public void Evaluate_TorznabPrefixIsTheSeriesAndPosition_IsNotAnotherAuthor()
    {
        // Given: "Series 01 - Title" puts the series and its number in the author field. Those
        // are the book's own words, not somebody else's name.
        var book = new AudiobookBuilder()
            .WithTitle("A Princess of Mars")
            .WithAuthor("Edgar Rice Burroughs")
            .WithSeries("Barsoom")
            .Build();

        // When / Then
        Assert.Equal(
            RequestedBookMatch.Accepted,
            RequestedBookReleaseFilter.Evaluate(book, TorznabRelease("Barsoom 01 - A Princess of Mars")));
    }

    // ------------------------------------------------------------------
    // Titles stored with a series prefix or a volume tail.
    // ------------------------------------------------------------------

    [Fact]
    [Trait("Method", "Evaluate")]
    [Trait("Scenario", "SeriesPrefixOnBookTitle")]
    public void Evaluate_BookTitleHasASeriesPrefixTheReleaseLeavesOut_IsAcceptedOnlyWithTheAuthor()
    {
        // Given: "Series: Title" on the record and just the title on the release
        var book = Book("Sherlock Holmes: A Study in Scarlet", "Arthur Conan Doyle");

        // When / Then
        Assert.Equal(
            RequestedBookMatch.Accepted,
            RequestedBookReleaseFilter.Evaluate(book, TorznabRelease("Arthur Conan Doyle - A Study in Scarlet")));

        // Control: the part after the colon is not enough by itself. Without the author nothing
        // says it is this book rather than another with that title.
        Assert.Equal(
            RequestedBookMatch.AuthorNotCorroborated,
            RequestedBookReleaseFilter.Evaluate(book, TorznabRelease("A Study in Scarlet [Unabridged]")));
    }

    [Theory]
    [Trait("Method", "Evaluate")]
    [Trait("Scenario", "VolumeTailOnBookTitle")]
    [InlineData("Miguel de Cervantes - Don Quixote Vol 1")]
    [InlineData("Miguel de Cervantes - Don Quixote")]
    [InlineData("Don Quixote Vol. 01 [Unabridged]")]
    public void Evaluate_BookTitleEndsWithAVolumeNumber_AcceptsTheReleaseWithOrWithoutIt(string releaseTitle)
    {
        // Given: ", Volume 1" is the series position, spelled several ways and often left off
        var book = Book("Don Quixote, Volume 1", "Miguel de Cervantes");

        // When / Then
        Assert.Equal(RequestedBookMatch.Accepted, RequestedBookReleaseFilter.Evaluate(book, TorznabRelease(releaseTitle)));
    }

    [Fact]
    [Trait("Method", "Evaluate")]
    [Trait("Scenario", "VolumeTailOnBookTitle")]
    public void Evaluate_ReleaseNamesADifferentVolume_IsRejectedAsTitleMismatch()
    {
        // Given: dropping the volume from the title must not make every volume match
        var book = Book("Don Quixote, Volume 1", "Miguel de Cervantes");

        // When / Then
        Assert.Equal(
            RequestedBookMatch.TitleMismatch,
            RequestedBookReleaseFilter.Evaluate(book, TorznabRelease("Miguel de Cervantes - Don Quixote Vol 2")));
    }

    // ------------------------------------------------------------------
    // Exclude: order, and an empty answer rather than an exception.
    // ------------------------------------------------------------------

    [Fact]
    [Trait("Method", "Exclude")]
    [Trait("Scenario", "KeepsOrder")]
    public void Exclude_DropsTheNonMatchAndKeepsTheRestInTheOrderGiven()
    {
        // Given
        var book = Book("Emma", "Jane Austen");
        var scored = new List<QualityScore>
        {
            Scored(Release("Edna Ferber - Emma McChesney and Co [M4B]", id: "decoy"), 95),
            Scored(Release("Jane Austen - Emma [MP3]", id: "second"), 70),
            Scored(Release("Jane Austen - Emma [M4B]", id: "third"), 60)
        };

        // When
        var kept = RequestedBookReleaseFilter.Exclude(book, scored, NullLogger.Instance);

        // Then
        Assert.Equal(new[] { "second", "third" }, kept.Select(s => s.SearchResult.Id).ToArray());
    }

    [Fact]
    [Trait("Method", "Exclude")]
    [Trait("Scenario", "AllRejected")]
    public void Exclude_EveryCandidateIsForAnotherBook_ReturnsAnEmptyList()
    {
        // Given
        var book = Book("Treasure Island", "Robert Louis Stevenson");
        var scored = new List<QualityScore>
        {
            Scored(Release("R. M. Ballantyne - The Coral Island", id: "a"), 90),
            Scored(Release("Robert Louis Stevenson - Kidnapped", id: "b"), 80)
        };

        // When
        var kept = RequestedBookReleaseFilter.Exclude(book, scored, NullLogger.Instance);

        // Then: empty, which both callers already answer with "no acceptable results"
        Assert.Empty(kept);
    }

    // ------------------------------------------------------------------
    // Wiring: the two paths that grab without a person choosing the release. The unit tests above
    // pass whether or not either caller runs the check, so these drive the real selection sites.
    // ------------------------------------------------------------------

    [Fact]
    [Trait("Method", "RunCycleAsync")]
    [Trait("Scenario", "Wiring")]
    public async Task AutomaticSearch_TopScoredReleaseIsForAnotherBook_GrabsTheNextMatchingOne()
    {
        // Given: the decoy outscores the right release, so without the check it is the grab
        var decoy = Torrent("decoy", "Edna Ferber - Emma McChesney and Co [M4B]");
        var match = Torrent("match", "Jane Austen - Emma [M4B]");
        await ArrangeHarnessAsync([(decoy, 95), (match, 60)]);
        var audiobook = await ArrangeMonitoredBookAsync("Emma", "Jane Austen");

        // When
        await RunAutomaticSearchCycleAsync();

        // Then
        var queued = await _downloadRepository.GetByAudiobookIdAsync(audiobook.Id);
        var download = Assert.Single(queued);
        Assert.Equal(match.Title, download.Title);
    }

    [Fact]
    [Trait("Method", "RunCycleAsync")]
    [Trait("Scenario", "WiringAllRejected")]
    public async Task AutomaticSearch_OnlyReleaseIsForAnotherBook_QueuesNothingAndDoesNotThrow()
    {
        // Given
        await ArrangeHarnessAsync([(Torrent("decoy", "Edna Ferber - Emma McChesney and Co [M4B]"), 95)]);
        var audiobook = await ArrangeMonitoredBookAsync("Emma", "Jane Austen");

        // When
        await RunAutomaticSearchCycleAsync();

        // Then: the same outcome as a search whose results the quality filter emptied
        Assert.Empty(await _downloadRepository.GetByAudiobookIdAsync(audiobook.Id));

        // Control: the book was processed to the end rather than the cycle failing on it. The
        // processor catches a per-book exception and moves on without stamping, so an exception
        // thrown by the check would leave this unset. Read through a fresh scope, since the test's
        // own repository still tracks the instance it saved.
        using var scope = _provider.CreateScope();
        var reloaded = await scope.ServiceProvider.GetRequiredService<IAudiobookRepository>().GetByIdAsync(audiobook.Id);
        Assert.NotNull(reloaded!.LastSearchTime);
    }

    [Fact]
    [Trait("Method", "SearchAndDownloadAsync")]
    [Trait("Scenario", "Wiring")]
    public async Task SearchAndDownload_TopScoredReleaseIsForAnotherBook_GrabsTheNextMatchingOne()
    {
        // Given
        var decoy = Torrent("decoy", "Edna Ferber - Emma McChesney and Co [M4B]");
        var match = Torrent("match", "Jane Austen - Emma [M4B]");
        await ArrangeHarnessAsync([(decoy, 95), (match, 60)]);
        var audiobook = await ArrangeMonitoredBookAsync("Emma", "Jane Austen");

        // When
        var result = await _provider.GetRequiredService<DownloadService>().SearchAndDownloadAsync(audiobook.Id);

        // Then
        Assert.True(result.Success, result.Message);
        Assert.Equal("match", result.SearchResult!.Id);
    }

    [Fact]
    [Trait("Method", "SearchAndDownloadAsync")]
    [Trait("Scenario", "WiringAllRejected")]
    public async Task SearchAndDownload_OnlyReleaseIsForAnotherBook_ReportsNoAcceptableResult()
    {
        // Given
        await ArrangeHarnessAsync([(Torrent("decoy", "Edna Ferber - Emma McChesney and Co [M4B]"), 95)]);
        var audiobook = await ArrangeMonitoredBookAsync("Emma", "Jane Austen");

        // When
        var result = await _provider.GetRequiredService<DownloadService>().SearchAndDownloadAsync(audiobook.Id);

        // Then
        Assert.False(result.Success);
        Assert.Equal("No acceptable search results found", result.Message);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Audiobook Book(string title, string? author)
    {
        var builder = new AudiobookBuilder().WithTitle(title);
        if (author != null)
        {
            builder.WithAuthor(author);
        }

        return builder.Build();
    }

    /// <summary>
    /// A release as the Torznab parser builds it: the author field is the text before the first
    /// " - " of the title, or "Unknown Author" when the title has none.
    /// </summary>
    private static SearchResult TorznabRelease(string title)
    {
        var separator = title.IndexOf(" - ", StringComparison.Ordinal);
        return Release(title, artist: separator > 0 ? title[..separator].Trim() : "Unknown Author");
    }

    private static SearchResult Release(string title, string artist = "", string id = "release")
    {
        return new SearchResult { Id = id, Title = title, Artist = artist };
    }

    private static QualityScore Scored(SearchResult release, int score)
    {
        return new QualityScore { SearchResult = release, TotalScore = score };
    }

    private static SearchResult Torrent(string id, string title)
    {
        var infoHash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(id)));
        return new SearchResult
        {
            Id = id,
            Title = title,
            DownloadType = "Torrent",
            MagnetLink = $"magnet:?xt=urn:btih:{infoHash}&dn={Uri.EscapeDataString(title)}",
            Format = "m4b",
            Quality = "M4B",
            Language = "English",
            Seeders = 20,
            Size = 500L * 1024 * 1024
        };
    }

    /// <summary>
    /// Registers the search, scoring and client doubles and seeds what the submission path needs.
    /// Scoring is a double that hands back fixed scores in the order given, so the decoy really is
    /// ranked first and the only thing that can move the grab off it is the check under test.
    /// </summary>
    private async Task ArrangeHarnessAsync(List<(SearchResult Release, int Score)> candidates)
    {
        var searchService = new Mock<ISearchService>();
        searchService
            .Setup(service => service.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<SearchSortBy>(),
                It.IsAny<SearchSortDirection>(),
                true,
                It.IsAny<SearchQueryPlan?>()))
            .ReturnsAsync(candidates.Select(candidate => candidate.Release).ToList());

        var scoring = new Mock<IQualityProfileService>();
        scoring
            .Setup(service => service.ScoreSearchResults(
                It.IsAny<List<SearchResult>>(),
                It.IsAny<QualityProfile>(),
                It.IsAny<bool>()))
            .ReturnsAsync(() => candidates.Select(candidate => Scored(candidate.Release, candidate.Score)).ToList());

        var gateway = new Mock<IDownloadClientGateway>();
        gateway
            .Setup(client => client.AddAsync(
                It.IsAny<DownloadClientConfiguration>(),
                It.IsAny<PreparedDownloadSubmission>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadClientSubmissionResult("ABCDEF1234567890ABCDEF1234567890ABCDEF12"));

        _services.AddSingleton(searchService.Object);
        _services.AddSingleton(scoring.Object);
        _services.AddSingleton(gateway.Object);
        Init();

        await _applicationSettingsRepository.SaveAsync(new ApplicationSettingsBuilder()
            .WithOutputPath(Path.GetTempPath())
            .Build());

        await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfiguration
        {
            Id = "qb-requested-book",
            Name = "local qbit",
            Type = "qbittorrent",
            Host = "localhost",
            Port = 8080,
            IsEnabled = true
        });
    }

    private async Task<Audiobook> ArrangeMonitoredBookAsync(string title, string author)
    {
        var profile = await _qualityProfileRepository.AddAsync(new QualityProfileBuilder()
            .WithName("Requested book profile")
            .Build());

        var audiobook = await _audiobookRepository.AddAsync(new AudiobookBuilder()
            .WithBasePath(FileService.GetTempPath())
            .WithTitle(title)
            .WithAuthor(author)
            .Build());
        audiobook.QualityProfileId = profile.Id;
        audiobook.Monitored = true;
        audiobook.LastSearchTime = null;
        await _audiobookRepository.UpdateAsync(audiobook);
        return audiobook;
    }

    private async Task RunAutomaticSearchCycleAsync()
    {
        var processor = new AutomaticSearchProcessor(
            _provider.GetRequiredService<ILogger<AutomaticSearchProcessor>>(),
            _provider.GetRequiredService<IServiceScopeFactory>());

        await processor.RunCycleAsync(CancellationToken.None);
    }
}
