/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.Core;

[Trait("Area", "Search")]
[Trait("Name", "AudiobookSearchQueryPlanTests")]
[Trait("Category", "AudiobookSearchQueryBuilder")]
public sealed class AudiobookSearchQueryPlanTests : BaseTests
{
    [Fact]
    [Trait("Method", "BuildPlan")]
    [Trait("Scenario", "TitleAuthorAndSeries")]
    public void BuildPlan_BookWithSeries_OrdersTitleFormsBeforeSeriesForms()
    {
        // Given: the shape the finding documented, where the series is the form that recovers the
        // book. The title has to be distinctive enough to clear the bare-title gate, or this test
        // would be asserting the order of two rungs rather than three.
        var audiobook = new AudiobookBuilder()
            .WithTitle("Twenty Thousand Leagues Under the Sea")
            .WithAuthor("Jules Verne")
            .WithSeries("Extraordinary Voyages")
            .Build();

        // When
        var plan = AudiobookSearchQueryBuilder.BuildPlan(audiobook);

        // Then: title forms first, so a book that was already being found still costs one request.
        // Control: the series-plus-author form is still produced, so the series is not simply
        // dropped from the ladder, only the bare form of it.
        Assert.Equal(
            new[]
            {
                "Twenty Thousand Leagues Under the Sea Jules Verne",
                "Twenty Thousand Leagues Under the Sea",
                "Extraordinary Voyages Jules Verne"
            },
            plan.Forms.Select(form => form.Query).ToArray());

        Assert.Equal(
            new[]
            {
                SearchQueryFormKind.TitleAuthor,
                SearchQueryFormKind.Title,
                SearchQueryFormKind.SeriesAuthor
            },
            plan.Forms.Select(form => form.Kind).ToArray());

        Assert.Equal(new[] { 1, 2, 3 }, plan.Forms.Select(form => form.Tier).ToArray());
    }

    [Fact]
    [Trait("Method", "BuildPlan")]
    [Trait("Scenario", "SeriesIsNotAppendedToTierOne")]
    public void BuildPlan_BookWithSeries_DoesNotNarrowTierOneWithTheSeries()
    {
        // Given
        var audiobook = new AudiobookBuilder()
            .WithTitle("Heaven's River")
            .WithAuthor("Dennis E. Taylor")
            .WithSeries("Bobiverse")
            .Build();

        // When
        var plan = AudiobookSearchQueryBuilder.BuildPlan(audiobook);

        // Then: the sweep used to send title, author and series as one query, which fails whenever
        // the indexer's own title does not carry the series name
        Assert.DoesNotContain("Bobiverse", plan.PrimaryQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Method", "BuildPlan")]
    [Trait("Scenario", "BareSeriesNeverStandsAlone")]
    public void BuildPlan_SeriesIsAGenericWordOrShortPhrase_NeverProducesABareSeriesForm()
    {
        // Given: the incident shape. A generic two-word series name is also a band name, and a
        // bare "series" form with no author is what an indexer matched to a music artist.
        var audiobook = new AudiobookBuilder()
            .WithTitle("The Long Way Home")
            .WithAuthor("Ann Leckie")
            .WithSeries("Radio Silence")
            .Build();

        // When
        var plan = AudiobookSearchQueryBuilder.BuildPlan(audiobook);

        // Then: the series never appears as a standalone query form, only paired with the author.
        // Control: the series-plus-author form is still present, so this is not a blanket
        // assertion that would also pass if the series were dropped from the ladder entirely.
        Assert.DoesNotContain("Radio Silence", plan.Forms.Select(form => form.Query));
        Assert.Contains("Radio Silence Ann Leckie", plan.Forms.Select(form => form.Query));
    }

    [Fact]
    [Trait("Method", "BuildPlan")]
    [Trait("Scenario", "NoSeries")]
    public void BuildPlan_BookWithoutSeries_StopsAfterTheTitleForms()
    {
        // Given: a real library has plenty of these, and they must never reach a blank series rung.
        // Distinctive enough to clear the bare-title gate, so both title rungs are present to be
        // counted; a shorter title would make this pass for the wrong reason.
        var audiobook = new AudiobookBuilder()
            .WithTitle("The Island of Doctor Moreau")
            .WithAuthor("H. G. Wells")
            .Build();

        // When
        var plan = AudiobookSearchQueryBuilder.BuildPlan(audiobook);

        // Then
        Assert.Equal(
            new[] { "The Island of Doctor Moreau H G Wells", "The Island of Doctor Moreau" },
            plan.Forms.Select(form => form.Query).ToArray());
    }

    [Fact]
    [Trait("Method", "BuildPlan")]
    [Trait("Scenario", "SeriesAlreadyInTitle")]
    public void BuildPlan_TitleAlreadyContainsTheSeries_DropsTheSeriesRungs()
    {
        // Given: corpus entry, series "Oz", which the title already says
        var audiobook = new AudiobookBuilder()
            .WithTitle("The Wonderful Wizard of Oz")
            .WithAuthor("L. Frank Baum")
            .WithSeries("Oz")
            .Build();

        // When
        var plan = AudiobookSearchQueryBuilder.BuildPlan(audiobook);

        // Then: a series rung here would be a strictly broader version of a question already asked
        Assert.Equal(
            new[] { "The Wonderful Wizard of Oz L Frank Baum", "The Wonderful Wizard of Oz" },
            plan.Forms.Select(form => form.Query).ToArray());
    }

    [Fact]
    [Trait("Method", "BuildPlan")]
    [Trait("Scenario", "SeriesNamedLikeATitleWord")]
    public void BuildPlan_SeriesMerelyResemblesATitleWord_KeepsTheSeriesRungs()
    {
        // Given: "Oz" inside "Ozymandias" is a substring, not a word
        var audiobook = new AudiobookBuilder()
            .WithTitle("Ozymandias")
            .WithAuthor("Percy Bysshe Shelley")
            .WithSeries("Oz")
            .Build();

        // When
        var plan = AudiobookSearchQueryBuilder.BuildPlan(audiobook);

        // Then
        Assert.Contains("Oz Percy Bysshe Shelley", plan.Forms.Select(form => form.Query));
    }

    [Fact]
    [Trait("Method", "BuildPlan")]
    [Trait("Scenario", "SubtitleDelimiter")]
    public void BuildPlan_TitleCarriesASubtitle_KeepsTheFullTitleFirstAndDemotesTheStem()
    {
        // Given: corpus entry whose left half alone ("She") is a near-useless query
        var audiobook = new AudiobookBuilder()
            .WithTitle("She: A History of Adventure")
            .WithAuthor("H. Rider Haggard")
            .WithSeries("Ayesha")
            .Build();

        // When
        var plan = AudiobookSearchQueryBuilder.BuildPlan(audiobook);

        // Then: the full title still leads, and the stem is an extra rung rather than a replacement
        Assert.Equal(
            new[]
            {
                "She: A History of Adventure H Rider Haggard",
                "She: A History of Adventure",
                "She H Rider Haggard",
                "Ayesha H Rider Haggard"
            },
            plan.Forms.Select(form => form.Query).ToArray());

        // The stem is never issued on its own: one word with no author anchor is noise
        Assert.DoesNotContain("She", plan.Forms.Select(form => form.Query));
    }

    [Fact]
    [Trait("Method", "BuildPlan")]
    [Trait("Scenario", "SubtitleDelimiterAndSeriesInTitle")]
    public void BuildPlan_SubtitledTitleStartingWithTheSeries_ReachesTheSeriesThroughTheStem()
    {
        // Given: the corpus entry that is the colon case and the series-in-title case at once
        var audiobook = new AudiobookBuilder()
            .WithTitle("Sherlock Holmes: A Study in Scarlet")
            .WithAuthor("Arthur Conan Doyle")
            .WithSeries("Sherlock Holmes")
            .Build();

        // When
        var plan = AudiobookSearchQueryBuilder.BuildPlan(audiobook);

        // Then: Readarr's "keep part 0" would have made the series the only query; here it is last
        Assert.Equal(
            new[]
            {
                "Sherlock Holmes: A Study in Scarlet Arthur Conan Doyle",
                "Sherlock Holmes: A Study in Scarlet",
                "Sherlock Holmes Arthur Conan Doyle"
            },
            plan.Forms.Select(form => form.Query).ToArray());
    }

    [Fact]
    [Trait("Method", "BuildPlan")]
    [Trait("Scenario", "AuthorInitials")]
    public void BuildPlan_AuthorWrittenWithInitials_DropsTheFullStops()
    {
        // Given: the sanitizer does not strip a full stop, so "E." reaches the wire as one token
        var audiobook = new AudiobookBuilder()
            .WithTitle("We Are Legion")
            .WithAuthor("Dennis E. Taylor")
            .Build();

        // When
        var plan = AudiobookSearchQueryBuilder.BuildPlan(audiobook);

        // Then
        Assert.Equal("We Are Legion Dennis E Taylor", plan.PrimaryQuery);
    }

    [Fact]
    [Trait("Method", "BuildPlan")]
    [Trait("Scenario", "AbbreviationThatIsNotAnInitial")]
    public void BuildPlan_AuthorCarryingAMultiLetterAbbreviation_LeavesItAlone()
    {
        // Given: only a lone letter before a full stop is an initial
        var audiobook = new AudiobookBuilder()
            .WithTitle("The Autocrat of the Breakfast Table")
            .WithAuthor("Oliver Wendell Holmes Jr.")
            .Build();

        // When
        var plan = AudiobookSearchQueryBuilder.BuildPlan(audiobook);

        // Then
        Assert.Equal("The Autocrat of the Breakfast Table Oliver Wendell Holmes Jr.", plan.PrimaryQuery);
    }

    [Fact]
    [Trait("Method", "BuildQueryTitle")]
    [Trait("Scenario", "ParenthesisedEditionAnnotation")]
    public void BuildQueryTitle_ParenthesisedEditionAnnotation_IsRemoved()
    {
        // Given / When / Then
        Assert.Equal("Dracula", AudiobookSearchQueryBuilder.BuildQueryTitle("Dracula (Unabridged)"));
    }

    [Fact]
    [Trait("Method", "BuildQueryTitle")]
    [Trait("Scenario", "ParenthesisedTitleWords")]
    public void BuildQueryTitle_ParenthesisedSpanThatIsPartOfTheWork_IsKept()
    {
        // Given: the corpus title a heuristic that guesses at annotations would eat
        const string title = "R.U.R. (Rossum's Universal Robots)";

        // When / Then
        Assert.Equal(title, AudiobookSearchQueryBuilder.BuildQueryTitle(title));
    }

    [Fact]
    [Trait("Method", "BuildPlan")]
    [Trait("Scenario", "NoAuthor")]
    public void BuildPlan_BookWithNoAuthor_DoesNotIssueTheSameQueryTwice()
    {
        // Given: with no author the title-and-author form collapses onto the title-alone form.
        // The title has to clear the bare-title gate, or the second form is blank and gets
        // dropped for being blank, which would leave the deduplication untested.
        var audiobook = new AudiobookBuilder()
            .WithTitle("The Arabian Nights Entertainments")
            .Build();

        // When
        var plan = AudiobookSearchQueryBuilder.BuildPlan(audiobook);

        // Then
        var form = Assert.Single(plan.Forms);
        Assert.Equal("The Arabian Nights Entertainments", form.Query);
    }

    [Fact]
    [Trait("Method", "Verbatim")]
    [Trait("Scenario", "OperatorTypedText")]
    public void Verbatim_OperatorTypedText_IsASingleForm()
    {
        // Given / When
        var plan = SearchQueryPlan.Verbatim("dracula stoker m4b");

        // Then
        var form = Assert.Single(plan.Forms);
        Assert.Equal("dracula stoker m4b", form.Query);
        Assert.Equal(SearchQueryFormKind.Verbatim, form.Kind);
        Assert.True(plan.IsVerbatim);
    }
}
