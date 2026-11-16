/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Application.Search.Scheduling;
using Listenarr.Tests.Builders;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search.Sweep
{
    // Before it starts a download the sweep checks that the book's target fingerprint still matches
    // the one it assessed, rebuilt from a full read of the book. These run both sides through the
    // production scoring service.
    [Trait("Name", "AutomaticSearchSeriesFingerprintTests")]
    [Trait("Category", "Infrastructure")]
    public sealed class AutomaticSearchSeriesFingerprintTests : AutomaticSearchSweepTestBase
    {
        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            ProductionFingerprints = true;
            BuildHost();
        }

        // One series is also the book's own Series field; a second one is only in the memberships.
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public async Task BookInSeries_IsGrabbedBySweep(int seriesCount)
        {
            await ConfigureAsync();
            var indexer = await AddIndexerAsync("Only");
            var book = await AddBookAsync(SheAndAllan(seriesCount), StartUtc.AddMinutes(-5));
            Indexers.Script = (_, _) => Returns(AcceptableSource);

            await RunCycleAsync();

            Downloads.Verify(d => d.StartDownloadAsync(It.IsAny<SearchResult>(), It.IsAny<string>(), book.Id), Times.Once);
            Assert.Equal(nameof(PairScheduleResult.Satisfied), (await Database.ReadPairAsync(book.Id, indexer))!.LastOutcome);
        }

        [Fact]
        public async Task BookChangedAfterAssessment_IsNotGrabbed()
        {
            await ConfigureAsync();
            var indexer = await AddIndexerAsync("Only");
            var book = await AddBookAsync(SheAndAllan(2), StartUtc.AddMinutes(-5));
            Indexers.Script = (_, _) => Returns(AcceptableSource);
            var quality = new Mock<IQualityProfileService>();
            quality
                .Setup(q => q.ScoreSearchResults(It.IsAny<List<SearchResult>>(), It.IsAny<QualityProfile>()))
                .Returns(async (List<SearchResult> results, QualityProfile _) =>
                {
                    // The book is edited while its results are being assessed.
                    await using var context = Database.CreateContext();
                    var stored = await context.Audiobooks.SingleAsync(candidate => candidate.Id == book.Id);
                    stored.Title = "She and Allan";
                    await context.SaveChangesAsync();
                    return results.Select(result => new QualityScore { SearchResult = result, TotalScore = 100 }).ToList();
                });
            BuildHost(quality.Object);

            await RunCycleAsync();

            Downloads.Verify(d => d.StartDownloadAsync(It.IsAny<SearchResult>(), It.IsAny<string>(), It.IsAny<int?>()), Times.Never);
            Assert.Equal(nameof(PairScheduleResult.GrabNotCompleted), (await Database.ReadPairAsync(book.Id, indexer))!.LastOutcome);
        }

        private static Audiobook SheAndAllan(int seriesCount)
        {
            var book = new AudiobookBuilder().WithTitle("She And Allan").WithAuthor("H. Rider Haggard").Build();
            book.Series = "Ayesha";
            book.SeriesNumber = "0";
            book.SeriesMemberships = new[] { ("Ayesha", "0"), ("Allan Quatermain", (string?)null) }
                .Take(seriesCount)
                .Select((series, index) => new AudiobookSeriesMembership
                {
                    SeriesName = series.Item1,
                    SeriesNumber = series.Item2,
                    IsPrimary = index == 0,
                    SortOrder = index
                })
                .ToList();
            return book;
        }
    }
}
