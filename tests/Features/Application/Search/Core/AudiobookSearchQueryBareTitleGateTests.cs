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

/// <summary>
/// Covers the rung that issues a title with no author beside it, and the condition under which
/// it is skipped.
/// </summary>
/// <remarks>
/// Every title and author here is invented. The rung was traced to a burst of wrong grabs on a
/// live install, and the titles that caused them are not reproduced; what is reproduced is their
/// shape, which is all the gate can see: one or two words left once articles and prepositions are
/// taken out. Reading anything into the particular words below is reading into nothing.
/// </remarks>
[Trait("Area", "Search")]
[Trait("Name", "AudiobookSearchQueryBareTitleGateTests")]
[Trait("Category", "AudiobookSearchQueryBuilder")]
public sealed class AudiobookSearchQueryBareTitleGateTests : BaseTests
{
    [Fact]
    [Trait("Method", "BuildPlan")]
    [Trait("Scenario", "OneSignificantWord")]
    public void BuildPlan_TitleOfOneSignificantWord_ProducesNoBareTitleForm()
    {
        // Given: a single word. With the author taken off it is no longer a search for a book, it
        // is a search for a word, and an indexer will happily answer with a record or a film.
        var audiobook = new AudiobookBuilder()
            .WithTitle("Thornlight")
            .WithAuthor("Imre Halloway")
            .Build();

        // When
        var plan = AudiobookSearchQueryBuilder.BuildPlan(audiobook);

        // Then
        Assert.DoesNotContain(SearchQueryFormKind.Title, plan.Forms.Select(form => form.Kind));

        // Control: the book is still searched. If the gate were deleting the audiobook from the
        // ladder rather than one rung of it, this assertion would fail.
        Assert.Equal("Thornlight Imre Halloway", plan.PrimaryQuery);
        Assert.Contains(SearchQueryFormKind.TitleAuthor, plan.Forms.Select(form => form.Kind));
    }

    [Fact]
    [Trait("Method", "BuildPlan")]
    [Trait("Scenario", "TwoSignificantWords")]
    public void BuildPlan_TitleOfTwoSignificantWordsOnceArticlesAreGone_ProducesNoBareTitleForm()
    {
        // Given: three words on the page, two once the article goes. This is the band the
        // threshold argument turns on: the live install lost a grab to a title of exactly this
        // shape, so two significant words is not enough to stand without an author.
        var audiobook = new AudiobookBuilder()
            .WithTitle("The Amber Cartograph")
            .WithAuthor("Cassia Vorne")
            .Build();

        // When
        var plan = AudiobookSearchQueryBuilder.BuildPlan(audiobook);

        // Then
        Assert.DoesNotContain("The Amber Cartograph", plan.Forms.Select(form => form.Query));

        // Control: the title-plus-author form is untouched
        Assert.Contains("The Amber Cartograph Cassia Vorne", plan.Forms.Select(form => form.Query));
    }

    [Theory]
    [Trait("Method", "BuildPlan")]
    [Trait("Scenario", "DistinctiveTitle")]
    [InlineData("The Glassmaker's Quiet Rebellion")]   // exactly at the threshold, three
    [InlineData("Notes Toward a Cartography of Salt Marshes")] // comfortably past it, four
    public void BuildPlan_DistinctiveTitle_StillProducesTheBareTitleForm(string title)
    {
        // Given: a title carrying enough of its own words to be a search for a work on its own.
        // This rung is the only one that recovers a release from an indexer that spells the
        // author differently, so gating it must not turn into removing it.
        var audiobook = new AudiobookBuilder()
            .WithTitle(title)
            .WithAuthor("Rosalind Teague")
            .Build();

        // When
        var plan = AudiobookSearchQueryBuilder.BuildPlan(audiobook);

        // Then: the bare title is present, second, exactly where it was before the gate
        Assert.Equal(
            new[] { title + " Rosalind Teague", title },
            plan.Forms.Select(form => form.Query).ToArray());
        Assert.Equal(SearchQueryFormKind.Title, plan.Forms[1].Kind);
    }

    [Fact]
    [Trait("Method", "BuildPlan")]
    [Trait("Scenario", "NoAuthorDoesNotUnanchorTheOtherRungs")]
    public void BuildPlan_RecordWithNoAuthor_StillNeverIssuesABareStemOrBareSeries()
    {
        // Given: an anonymous or traditional work, or a record part way through a metadata
        // refresh. The stem and series rungs are built by pairing with the author, and pairing
        // with nothing used to yield the left side on its own, so the two rungs documented as
        // never standing alone were doing exactly that whenever the author was missing.
        var anonymous = new AudiobookBuilder()
            .WithTitle("Thornlight: A Tale of the Fen Country")
            .WithSeries("Amber Static")
            .Build();

        // When
        var plan = AudiobookSearchQueryBuilder.BuildPlan(anonymous);

        // Then: neither half reaches an indexer without something anchoring it
        Assert.DoesNotContain("Thornlight", plan.Forms.Select(form => form.Query));
        Assert.DoesNotContain("Amber Static", plan.Forms.Select(form => form.Query));

        // Control: the record is still searched, and the same record with an author does get
        // both of those rungs, so this is the author being absent rather than the rungs being
        // gone. Without it the assertions above would pass on a builder that had dropped the
        // stem and series rungs altogether.
        Assert.Contains("Thornlight: A Tale of the Fen Country", plan.Forms.Select(form => form.Query));

        var attributed = new AudiobookBuilder()
            .WithTitle("Thornlight: A Tale of the Fen Country")
            .WithAuthor("Imre Halloway")
            .WithSeries("Amber Static")
            .Build();

        var attributedQueries = AudiobookSearchQueryBuilder.BuildPlan(attributed)
            .Forms.Select(form => form.Query)
            .ToArray();

        Assert.Contains("Thornlight Imre Halloway", attributedQueries);
        Assert.Contains("Amber Static Imre Halloway", attributedQueries);
    }

    [Theory]
    [Trait("Method", "CountSignificantWords")]
    [Trait("Scenario", "StopWords")]
    // Long in words, short in the words that name anything
    [InlineData("A Wind in the Dunes", 2)]
    [InlineData("Of the One and the Other", 2)]
    [InlineData("Thornlight", 1)]
    [InlineData("The Amber Cartograph", 2)]
    [InlineData("The Glassmaker's Quiet Rebellion", 3)]
    [InlineData("Notes Toward a Cartography of Salt Marshes", 4)]
    [InlineData("A Wind Through Thornwold Dunes", 3)]
    // Prepositions are listed as whole classes, so a word and its opposite score alike
    [InlineData("Down the Amber Stair", 2)]
    [InlineData("Up the Amber Stair", 2)]
    // Demonstratives are not function words for this purpose, and neither are pronouns
    [InlineData("This Amber Shore", 3)]
    [InlineData("Her Amber Shore", 3)]
    // No word segmentation exists for a script that does not space its words, so a title in
    // one is a single token and can never clear the threshold. Pinned so it is a known
    // limitation rather than a surprise.
    [InlineData("\u6749\u6728\u306e\u68ee", 1)]
    public void CountSignificantWords_IgnoresFunctionWords(string title, int expected)
    {
        Assert.Equal(expected, AudiobookSearchQueryBuilder.CountSignificantWords(title));
    }

    [Fact]
    [Trait("Method", "BuildPlan")]
    [Trait("Scenario", "PaddedWithFunctionWords")]
    public void BuildPlan_TitleLongInWordsButShortInSignificantOnes_IsTreatedAsShort()
    {
        // Given: five words, two of which name anything. Counting words rather than significant
        // words would let this through, and it is no more a search for a book on its own than the
        // two-word case above.
        var padded = new AudiobookBuilder()
            .WithTitle("A Wind in the Dunes")
            .WithAuthor("Odile Marchetti")
            .Build();

        // When
        var paddedPlan = AudiobookSearchQueryBuilder.BuildPlan(padded);

        // Then
        Assert.DoesNotContain("A Wind in the Dunes", paddedPlan.Forms.Select(form => form.Query));

        // and the book is still searched, so the assertion above cannot pass on an empty plan
        Assert.Contains("A Wind in the Dunes Odile Marchetti", paddedPlan.Forms.Select(form => form.Query));

        // Control, and it has to come out differently: the same five words, three of them
        // significant, does produce the bare form. Without this the assertion above would also
        // pass on a gate that had simply counted every word and put the bar at six.
        var carrying = new AudiobookBuilder()
            .WithTitle("A Wind Through Thornwold Dunes")
            .WithAuthor("Odile Marchetti")
            .Build();

        var carryingPlan = AudiobookSearchQueryBuilder.BuildPlan(carrying);

        Assert.Contains("A Wind Through Thornwold Dunes", carryingPlan.Forms.Select(form => form.Query));
    }
}
