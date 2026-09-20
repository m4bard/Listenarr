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
    // Exactly at the threshold, so the boundary is pinned from the passing side too
    [InlineData("The Glassmaker's Quiet Rebellion")]
    [InlineData("Notes Toward a Cartography of Salt Marshes")]
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
