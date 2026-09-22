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

namespace Listenarr.Tests.Features.Application.Downloads.Submission;

[Trait("Name", "SeedCriteriaResolverTests")]
[Trait("Category", "SeedCriteria")]
public sealed class SeedCriteriaResolverTests : BaseTests
{
    [Fact]
    public async Task ResolveAsync_WhenIndexerIdIsNull_ReturnsNull()
    {
        var resolver = _provider.GetRequiredService<ISeedCriteriaResolver>();

        var result = await resolver.ResolveAsync(null);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_WhenIndexerDoesNotExist_ReturnsNull()
    {
        var resolver = _provider.GetRequiredService<ISeedCriteriaResolver>();

        var result = await resolver.ResolveAsync(999999);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_WhenIndexerHasNoSeedCriteria_ReturnsNull()
    {
        var indexer = await _indexerRepository.AddAsync(new IndexerBuilder()
            .WithName("No Seed Criteria")
            .WithType("Torrent")
            .Build());
        var resolver = _provider.GetRequiredService<ISeedCriteriaResolver>();

        var result = await resolver.ResolveAsync(indexer.Id);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_WhenIndexerHasSeedRatioAndSeedTime_ReturnsBoth()
    {
        var indexer = await _indexerRepository.AddAsync(new IndexerBuilder()
            .WithName("Hit and Run Tracker")
            .WithType("Torrent")
            .WithSeedRatio(1.5)
            .WithSeedTime(120)
            .Build());
        var resolver = _provider.GetRequiredService<ISeedCriteriaResolver>();

        var result = await resolver.ResolveAsync(indexer.Id);

        Assert.NotNull(result);
        Assert.True(result!.HasAnyValue);
        Assert.Equal(1.5, result.Ratio);
        Assert.Equal(TimeSpan.FromMinutes(120), result.SeedTime);
    }

    [Fact]
    public async Task ResolveAsync_WhenIndexerHasSeedRatioOnly_LeavesSeedTimeNull()
    {
        var indexer = await _indexerRepository.AddAsync(new IndexerBuilder()
            .WithName("Ratio Only Tracker")
            .WithType("Torrent")
            .WithSeedRatio(2.0)
            .Build());
        var resolver = _provider.GetRequiredService<ISeedCriteriaResolver>();

        var result = await resolver.ResolveAsync(indexer.Id);

        Assert.NotNull(result);
        Assert.Equal(2.0, result!.Ratio);
        Assert.Null(result.SeedTime);
    }
}
