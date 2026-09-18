/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using System.Reflection;
using Listenarr.Application.Search.Indexers.Torznab;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Search.Indexers.Torznab;

[Trait("Name", "TorznabIndexerFlagSurfacingTests")]
[Trait("Category", "TorznabIndexerFlagSurfacing")]
public sealed class TorznabIndexerFlagSurfacingTests : BaseTests
{
    private const string ParseMethodName = "ParseTorznabResponseAsync";

    private const string FeedWithFlags = """
    <?xml version="1.0" encoding="UTF-8"?>
    <rss version="2.0" xmlns:torznab="http://torznab.com/schemas/2015/feed">
      <channel>
        <item>
          <title>A Free Book</title>
          <guid>https://tracker.example/details/1</guid>
          <enclosure url="https://tracker.example/download/1.torrent" length="1048576" type="application/x-bittorrent" />
          <torznab:attr name="size" value="1048576" />
          <torznab:attr name="seeders" value="9" />
          <torznab:attr name="downloadvolumefactor" value="0" />
          <torznab:attr name="uploadvolumefactor" value="2" />
          <torznab:attr name="tag" value="internal" />
        </item>
        <item>
          <title>A Paid Book</title>
          <guid>https://tracker.example/details/2</guid>
          <enclosure url="https://tracker.example/download/2.torrent" length="1048576" type="application/x-bittorrent" />
          <torznab:attr name="size" value="1048576" />
          <torznab:attr name="seeders" value="4" />
          <torznab:attr name="downloadvolumefactor" value="1" />
          <torznab:attr name="uploadvolumefactor" value="1" />
        </item>
      </channel>
    </rss>
    """;

    [Fact]
    public async Task ParseAsync_ReadsFlagsFromTheStandardTorznabAttributes()
    {
        using var httpClient = new HttpClient();
        var parser = new TorznabResponseParser(httpClient, NullLogger.Instance);

        var results = await parser.ParseAsync(FeedWithFlags, CreateIndexer());

        Assert.Equal(2, results.Count);
        Assert.Equal(["doubleupload", "freeleech", "internal"], results[0].IndexerFlags.Order());
        Assert.Empty(results[1].IndexerFlags);
    }

    [Fact]
    public async Task ParsedFlagsReachThePublicIndexerResultDto()
    {
        using var httpClient = new HttpClient();
        var parser = new TorznabResponseParser(httpClient, NullLogger.Instance);

        var results = await parser.ParseAsync(FeedWithFlags, CreateIndexer());
        var dto = SearchResultConverters.ToIndexerResultDto(results[0]);

        Assert.Equal(["doubleupload", "freeleech", "internal"], dto.IndexerFlags.Order());
    }

    [Fact]
    public async Task ParsedFlagsSurviveTheLegacySearchResultRoundTrip()
    {
        using var httpClient = new HttpClient();
        var parser = new TorznabResponseParser(httpClient, NullLogger.Instance);

        var results = await parser.ParseAsync(FeedWithFlags, CreateIndexer());
        var legacy = SearchResultConverters.ToSearchResult(results[0]);
        var roundTripped = SearchResultConverters.ToIndexerSearchResult(legacy);

        Assert.Equal(["doubleupload", "freeleech", "internal"], legacy.IndexerFlags.Order());
        Assert.Equal(["doubleupload", "freeleech", "internal"], roundTripped.IndexerFlags.Order());
    }

    [Fact]
    public async Task TheInfrastructureProviderReadsTheSameFlags()
    {
        // Torznab attributes are parsed in two places. Both have to learn about the flags, or a
        // result carries them only on whichever code path happened to handle it.
        using var httpClient = new HttpClient();
        var provider = new TorznabNewznabSearchProvider(httpClient, NullLogger<TorznabNewznabSearchProvider>.Instance);

        var results = await ParseWithTheInfrastructureProviderAsync(provider, FeedWithFlags, CreateIndexer());

        Assert.Equal(2, results.Count);
        Assert.Equal(["doubleupload", "freeleech", "internal"], results[0].IndexerFlags.Order());
        Assert.Empty(results[1].IndexerFlags);
    }

    /// <summary>
    /// Invokes the provider's private Torznab parse. It sits behind no interface, so reflection is
    /// the only way in, and both its parameter list and its return type are free to move: it is the
    /// entry point the search pipeline gets reworked through.
    ///
    /// Binding to one fixed signature makes that movement surface as a
    /// <see cref="TargetParameterCountException"/> or a cast failure carrying no message, which says
    /// nothing about the flags this test is about. So the shape is discovered instead: the single
    /// non-public overload taking (string, Indexer, ...), any further parameters filled from their
    /// types, and the results taken either directly or from a Results property on whatever comes
    /// back. A method that moves further than that fails an assertion naming what was found.
    /// </summary>
    private static async Task<IReadOnlyList<IndexerSearchResult>> ParseWithTheInfrastructureProviderAsync(
        TorznabNewznabSearchProvider provider,
        string xmlContent,
        Indexer indexer)
    {
        var candidates = typeof(TorznabNewznabSearchProvider)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(method => method.Name == ParseMethodName && TakesTheFeedAndTheIndexer(method))
            .ToArray();

        Assert.True(
            candidates.Length == 1,
            $"Expected exactly one non-public {ParseMethodName}(string, Indexer, ...) on "
                + $"{nameof(TorznabNewznabSearchProvider)}; found {candidates.Length}. The parse entry "
                + "point moved. Follow it here rather than dropping the assertions below.");

        var parse = candidates[0];
        var parameters = parse.GetParameters();
        var arguments = new object?[parameters.Length];
        arguments[0] = xmlContent;
        arguments[1] = indexer;
        for (var index = 2; index < parameters.Length; index++)
        {
            arguments[index] = ArgumentFor(parameters[index]);
        }

        var task = Assert.IsAssignableFrom<Task>(parse.Invoke(provider, arguments));
        await task;

        return ResultsFrom(parse.ReturnType.GetProperty("Result")?.GetValue(task));
    }

    private static bool TakesTheFeedAndTheIndexer(MethodInfo method)
    {
        var parameters = method.GetParameters();
        return parameters.Length >= 2
            && parameters[0].ParameterType == typeof(string)
            && parameters[1].ParameterType == typeof(Indexer);
    }

    private static object? ArgumentFor(ParameterInfo parameter)
    {
        // Nothing here is called while the parse takes only the feed and the indexer. It exists so
        // that a parameter added later is filled with something harmless rather than stopping the
        // flag assertions: a string is the likely shape and anything else falls back to its default.
        if (parameter.ParameterType == typeof(string))
        {
            return "a free book";
        }

        if (parameter.HasDefaultValue)
        {
            return parameter.DefaultValue;
        }

        Assert.True(
            parameter.ParameterType.IsValueType,
            $"{ParseMethodName} grew a required '{parameter.Name}' of type "
                + $"{parameter.ParameterType.Name}, which this test has no sensible value for. Supply "
                + "a real one here.");

        return Activator.CreateInstance(parameter.ParameterType);
    }

    private static IReadOnlyList<IndexerSearchResult> ResultsFrom(object? parsed)
    {
        if (parsed is IReadOnlyList<IndexerSearchResult> bare)
        {
            return bare;
        }

        var carried = parsed?.GetType().GetProperty("Results")?.GetValue(parsed);
        Assert.True(
            carried is not null,
            $"{ParseMethodName} returned {parsed?.GetType().Name ?? "nothing"}, which is neither a "
                + "result list nor something carrying a Results property.");

        return Assert.IsAssignableFrom<IReadOnlyList<IndexerSearchResult>>(carried);
    }

    private static Indexer CreateIndexer() => new()
    {
        Id = 3,
        Name = "Torznab",
        Url = "https://tracker.example/api",
        Implementation = "Torznab",
        Type = "Torrent"
    };
}
