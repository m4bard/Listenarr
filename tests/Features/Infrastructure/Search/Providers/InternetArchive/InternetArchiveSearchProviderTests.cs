/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Net;
using System.Text.Json;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Search.Providers.InternetArchive;

[Trait("Name", "InternetArchiveSearchProviderTests")]
[Trait("Category", "InternetArchiveSearchProvider")]
public sealed class InternetArchiveSearchProviderTests : BaseTests
{
    [Fact]
    public async Task SearchAsync_MultiTrackItem_ReturnsCompleteRepresentations()
    {
        // Given
        var provider = CreateProvider(CreateMultiRepresentationMetadata());
        var indexer = CreateIndexer();

        // When
        var observation = await provider.SearchAsync(indexer, "Alice");
        var results = observation.Results;

        // Then
        Assert.Equal(2, results.Count);
        var m4b = Assert.Single(results, result => result.Format == "M4B");
        Assert.Equal("M4B", m4b.Quality);
        Assert.Equal(900, m4b.Size);
        Assert.Equal(1, m4b.Files);
        Assert.Equal("English", m4b.Language);

        var mp3 = Assert.Single(results, result => result.Format == "128Kbps MP3");
        Assert.Equal("MP3 128kbps", mp3.Quality);
        Assert.Equal(280, mp3.Size);
        Assert.Equal(2, mp3.Files);
        Assert.Equal("English", mp3.Language);
        var archive = Assert.Single(mp3.DirectDownloadArtifacts);
        Assert.Equal("alice_128kb_mp3.zip", archive.FileName);
        Assert.Equal(DirectDownloadArtifactPackaging.Archive, archive.Packaging);
    }

    [Fact]
    public async Task SearchAsync_TitleContainsLanguageSubstrings_UsesMetadataLanguage()
    {
        // Given
        const string metadataJson = """
{
  "metadata": {
    "language": ["English"]
  },
  "files": [
    {
      "name": "songs_01_128kb.mp3",
      "format": "128Kbps MP3",
      "size": "4338087"
    }
  ]
}
""";
        var provider = CreateProvider(
            metadataJson,
            "Songs from Alice in Wonderland and Through the Looking-Glass");

        // When
        var result = Assert.Single((await provider.SearchAsync(CreateIndexer(), "Alice")).Results);

        // Then
        Assert.Equal("English", result.Language);
    }

    [Fact]
    public async Task SearchAsync_Iso639ThreeLetterLanguageCode_MapsToDisplayName()
    {
        // Given
        const string metadataJson = """
{
  "metadata": {
    "language": "deu"
  },
  "files": [
    {
      "name": "alice.m4b",
      "format": "LibriVox Apple Audiobook",
      "size": "900"
    }
  ]
}
""";
        var provider = CreateProvider(metadataJson);

        // When
        var result = Assert.Single((await provider.SearchAsync(CreateIndexer(), "Alice")).Results);

        // Then
        Assert.Equal("German", result.Language);
    }

    [Fact]
    public async Task SearchAsync_ArchiveExtractionDisabled_UsesCompleteTrackBatch()
    {
        // Given
        var provider = CreateProvider(
            CreateMultiRepresentationMetadata(),
            allowArchives: false);

        // When
        var observation = await provider.SearchAsync(CreateIndexer(), "Alice");
        var results = observation.Results;

        // Then
        var mp3 = Assert.Single(results, result => result.Format == "128Kbps MP3");
        Assert.Equal(300, mp3.Size);
        Assert.Equal(2, mp3.Files);
        Assert.Collection(
            mp3.DirectDownloadArtifacts,
            first => Assert.Equal("alice_01_128kb.mp3", first.FileName),
            second => Assert.Equal("alice_02_128kb.mp3", second.FileName));
        Assert.All(
            mp3.DirectDownloadArtifacts,
            artifact => Assert.Equal(DirectDownloadArtifactPackaging.File, artifact.Packaging));
    }

    [Fact]
    public async Task SearchAsync_NestedArtifactPath_PreservesRemotePathSegments()
    {
        // Given
        const string metadataJson = """
{
  "metadata": {
    "language": "English"
  },
  "files": [
    {
      "name": "audio tracks/chapter 01.mp3",
      "format": "128Kbps MP3",
      "size": "100"
    }
  ]
}
""";
        var provider = CreateProvider(metadataJson);

        // When
        var result = Assert.Single((await provider.SearchAsync(CreateIndexer(), "Alice")).Results);

        // Then
        var artifact = Assert.Single(result.DirectDownloadArtifacts);
        Assert.Equal("https://archive.org/download/alice_book/audio%20tracks/chapter%2001.mp3", artifact.Url);
        Assert.Equal("chapter 01.mp3", artifact.FileName);
    }

    [Fact]
    public async Task SearchAsync_UnbundledRepresentationExceedsLimit_OmitsRepresentation()
    {
        // Given
        var metadataJson = JsonSerializer.Serialize(new
        {
            metadata = new { language = "English" },
            files = Enumerable.Range(1, 501).Select(index => new
            {
                name = $"chapter-{index:000}.mp3",
                format = "128Kbps MP3",
                size = "100"
            })
        });
        var provider = CreateProvider(metadataJson, allowArchives: false);

        // When
        var observation = await provider.SearchAsync(CreateIndexer(), "Alice");
        var results = observation.Results;

        // Then
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_SanitizedApostropheAndAuthorTerms_UsesBroadCollectionSearch()
    {
        // Given
        const string expectedBroadQuery = "collection:librivoxaudio AND (Alices Adventures in Wonderland Lewis Carroll)";
        var capturedQueries = new List<string>();
        var provider = CreateProvider(uri =>
        {
            if (IsAdvancedSearch(uri))
            {
                var archiveQuery = ReadQueryParameter(uri, "q");
                capturedQueries.Add(archiveQuery);
                return archiveQuery == expectedBroadQuery
                    ? Ok(CreateSearchResponse("Alice's Adventures in Wonderland", "alices_adventures_1003"))
                    : Ok(CreateEmptySearchResponse());
            }

            if (IsMetadataRequest(uri, "alices_adventures_1003"))
            {
                return Ok(CreateMultiRepresentationMetadata());
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        // When
        var observation = await provider.SearchAsync(CreateIndexer(), "Alices Adventures in Wonderland Lewis Carroll");
        var results = observation.Results;

        // Then
        Assert.Contains(capturedQueries, query => query == expectedBroadQuery);
        Assert.DoesNotContain(
            capturedQueries,
            query => query.Contains("title:(Alices Adventures in Wonderland Lewis Carroll)", StringComparison.OrdinalIgnoreCase)
                || query.Contains("creator:(Alices Adventures in Wonderland Lewis Carroll)", StringComparison.OrdinalIgnoreCase));
        var result = Assert.Single(results, result => result.Format == "M4B");
        Assert.Equal("Alice's Adventures in Wonderland", result.Title);
        Assert.Equal("Lewis Carroll", result.Artist);
        Assert.Equal("https://archive.org/details/alices_adventures_1003", result.ResultUrl);
        Assert.Equal("https://archive.org/details/alices_adventures_1003", result.SourceLink);
        Assert.Equal("2010-03-01", result.PublishedDate);
        Assert.Equal(DirectDownloadMetadataKeys.ClientId, result.DownloadType);
        Assert.NotEmpty(result.DirectDownloadArtifacts);
    }

    [Fact]
    public async Task SearchAsync_DuplicateIdentifiersAcrossQueryVariants_FetchesMetadataOnce()
    {
        // Given
        var metadataCalls = 0;
        var provider = CreateProvider(uri =>
        {
            if (IsAdvancedSearch(uri))
            {
                return Ok(CreateSearchResponse("Alice's Adventures in Wonderland", "alices_adventures_1003"));
            }

            if (IsMetadataRequest(uri, "alices_adventures_1003"))
            {
                metadataCalls++;
                return Ok(CreateMultiRepresentationMetadata());
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var request = new SearchRequest
        {
            Mode = SearchMode.Advanced,
            Title = "Alice's Adventures in Wonderland",
            Author = "Lewis Carroll"
        };

        // When
        var observation = await provider.SearchAsync(
            CreateIndexer(),
            "Alices Adventures in Wonderland Lewis Carroll",
            request: request);
        var results = observation.Results;

        // Then
        Assert.Equal(1, metadataCalls);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchAsync_DuplicateIdentifiersDoNotConsumeVariantMetadataBudget()
    {
        // Given
        var advancedSearchCalls = 0;
        var metadataCalls = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var duplicateDocs = Enumerable.Range(0, 19)
            .Select(_ => ("Alice's Adventures in Wonderland", "alice_book"))
            .Append(("A New Alice in the Old Wonderland", "new_book"))
            .ToArray();
        var provider = CreateProvider(uri =>
        {
            if (IsAdvancedSearch(uri))
            {
                advancedSearchCalls++;
                return Ok(advancedSearchCalls switch
                {
                    1 => CreateSearchResponse("Alice's Adventures in Wonderland", "alice_book"),
                    2 => CreateSearchResponse(duplicateDocs),
                    _ => CreateEmptySearchResponse()
                });
            }

            if (IsMetadataRequest(uri, "alice_book"))
            {
                metadataCalls["alice_book"] = metadataCalls.GetValueOrDefault("alice_book") + 1;
                return Ok(CreateMultiRepresentationMetadata());
            }

            if (IsMetadataRequest(uri, "new_book"))
            {
                metadataCalls["new_book"] = metadataCalls.GetValueOrDefault("new_book") + 1;
                return Ok(CreateMultiRepresentationMetadata());
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var request = new SearchRequest
        {
            Mode = SearchMode.Advanced,
            Title = "Alice's Adventures in Wonderland",
            Author = "Lewis Carroll"
        };

        // When
        var observation = await provider.SearchAsync(
            CreateIndexer(),
            "Alices Adventures in Wonderland Lewis Carroll",
            request: request);
        var results = observation.Results;

        // Then
        Assert.Equal(1, metadataCalls["alice_book"]);
        Assert.Equal(1, metadataCalls["new_book"]);
        Assert.Contains(results, result => result.Title == "A New Alice in the Old Wonderland");
    }

    [Fact]
    public async Task SearchAsync_InvalidCollectionSetting_FallsBackToDefaultCollection()
    {
        // Given
        var capturedQueries = new List<string>();
        var provider = CreateProvider(uri =>
        {
            if (IsAdvancedSearch(uri))
            {
                capturedQueries.Add(ReadQueryParameter(uri, "q"));
                return Ok(CreateEmptySearchResponse());
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var indexer = CreateIndexer("librivoxaudio) OR mediatype:(texts");

        // When
        await provider.SearchAsync(indexer, "Alice");

        // Then
        var query = Assert.Single(capturedQueries);
        Assert.StartsWith("collection:librivoxaudio AND", query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mediatype", query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Method", "SearchAsync")]
    [Trait("Scenario", "NonSuccessStatus")]
    public async Task SearchAsync_ServerError_ReportsUnavailableWithHttpStatus()
    {
        // Given
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        // When
        var observation = await provider.SearchAsync(CreateIndexer(), "Alice");

        // Then
        Assert.Equal(IndexerQueryOutcome.Unavailable, observation.Outcome);
        Assert.Equal(IndexerQueryReason.HttpStatus, observation.Reason);
        Assert.Empty(observation.Results);
        Assert.False(observation.ShouldEscalate);
        Assert.Equal("500", observation.Detail);
    }

    [Fact]
    [Trait("Method", "SearchAsync")]
    [Trait("Scenario", "RequestTimeout")]
    public async Task SearchAsync_RequestTimesOut_ReportsUnavailableWithTimeout()
    {
        // Given: the shape HttpClient uses for its own request timeout
        var provider = CreateProvider(_ => throw new TaskCanceledException("timed out", new TimeoutException()));

        // When
        var observation = await provider.SearchAsync(CreateIndexer(), "Alice");

        // Then
        Assert.Equal(IndexerQueryOutcome.Unavailable, observation.Outcome);
        Assert.Equal(IndexerQueryReason.Timeout, observation.Reason);
        Assert.False(observation.ShouldEscalate);
    }

    [Fact]
    [Trait("Method", "SearchAsync")]
    [Trait("Scenario", "MalformedBody")]
    public async Task SearchAsync_MalformedJson_ReportsUnreadable()
    {
        // Given
        var provider = CreateProvider(uri => IsAdvancedSearch(uri)
            ? Ok("{\"response\": {\"docs\": [")
            : new HttpResponseMessage(HttpStatusCode.NotFound));

        // When
        var observation = await provider.SearchAsync(CreateIndexer(), "Alice");

        // Then
        Assert.Equal(IndexerQueryOutcome.Unreadable, observation.Outcome);
        Assert.False(observation.Answered);
        Assert.False(observation.ShouldEscalate);
    }

    [Fact]
    [Trait("Method", "SearchAsync")]
    [Trait("Scenario", "EmptyResultSet")]
    public async Task SearchAsync_WellFormedEmptyResponse_ReportsNoMatchNotUnavailable()
    {
        // Given: the control for this group. Without it, a mapping that answered Unavailable for
        // everything would pass every other outcome test here.
        var provider = CreateProvider(uri => IsAdvancedSearch(uri)
            ? Ok(CreateEmptySearchResponse())
            : new HttpResponseMessage(HttpStatusCode.NotFound));

        // When
        var observation = await provider.SearchAsync(CreateIndexer(), "Alice");

        // Then
        Assert.Equal(IndexerQueryOutcome.NoMatch, observation.Outcome);
        Assert.Equal(IndexerQueryReason.EmptyChannel, observation.Reason);
        Assert.Empty(observation.Results);
        Assert.True(observation.Answered);
        Assert.True(observation.ShouldEscalate);
    }

    [Fact]
    [Trait("Method", "SearchAsync")]
    [Trait("Scenario", "ResultsReturned")]
    public async Task SearchAsync_ResponseWithDocs_ReportsHit()
    {
        // Given
        var provider = CreateProvider(CreateMultiRepresentationMetadata());

        // When
        var observation = await provider.SearchAsync(CreateIndexer(), "Alice");

        // Then
        Assert.Equal(IndexerQueryOutcome.Hit, observation.Outcome);
        Assert.Equal(IndexerQueryReason.None, observation.Reason);
        Assert.NotEmpty(observation.Results);
        Assert.True(observation.Answered);
    }

    private static InternetArchiveSearchProvider CreateProvider(
        string metadataJson,
        string title = "A New Alice in the Old Wonderland",
        bool allowArchives = true)
    {
        return CreateProvider(uri =>
        {
            if (IsAdvancedSearch(uri))
            {
                return Ok(CreateSearchResponse(title));
            }

            if (IsMetadataRequest(uri, "alice_book"))
            {
                return Ok(metadataJson);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }, allowArchives);
    }

    private static InternetArchiveSearchProvider CreateProvider(
        Func<Uri, HttpResponseMessage> responseFactory,
        bool allowArchives = true)
    {
        var handler = new DelegatingHandlerMock((request, _) =>
        {
            var uri = request.RequestUri ?? new Uri("about:blank");
            return Task.FromResult(responseFactory(uri));
        });

        var configurationService = new Mock<IConfigurationService>();
        configurationService
            .Setup(service => service.GetApplicationSettingsAsync())
            .ReturnsAsync(allowArchives
                ? new ApplicationSettingsBuilder().WithExtractArchive().Build()
                : new ApplicationSettingsBuilder().WithoutExtractArchive().Build());

        return new InternetArchiveSearchProvider(
            new HttpClient(handler),
            configurationService.Object,
            NullLogger<InternetArchiveSearchProvider>.Instance);
    }

    private static Indexer CreateIndexer(string? collection = null)
    {
        var builder = new IndexerBuilder()
            .WithId(42)
            .WithName("Internet Archive")
            .WithType("DirectDownload")
            .WithImplementation("InternetArchive")
            .WithUrl("https://archive.org");

        if (!string.IsNullOrWhiteSpace(collection))
        {
            builder.WithSetting("collection", collection);
        }

        return builder.Build();
    }

    private static bool IsAdvancedSearch(Uri uri) =>
        uri.AbsolutePath.Contains("/advancedsearch.php", StringComparison.OrdinalIgnoreCase);

    private static bool IsMetadataRequest(Uri uri, string identifier) =>
        uri.AbsolutePath.EndsWith($"/metadata/{identifier}", StringComparison.OrdinalIgnoreCase);

    private static string ReadQueryParameter(Uri uri, string name)
    {
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = part.IndexOf('=', StringComparison.Ordinal);
            var key = separatorIndex >= 0 ? part[..separatorIndex] : part;
            if (!string.Equals(Uri.UnescapeDataString(key), name, StringComparison.Ordinal))
            {
                continue;
            }

            var value = separatorIndex >= 0 ? part[(separatorIndex + 1)..] : string.Empty;
            return WebUtility.UrlDecode(value) ?? string.Empty;
        }

        return string.Empty;
    }

    private static HttpResponseMessage Ok(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content)
    };

    private static string CreateSearchResponse(string title, string identifier = "alice_book") =>
        CreateSearchResponse((title, identifier));

    private static string CreateSearchResponse(params (string Title, string Identifier)[] docs)
    {
        var docsJson = string.Join(
            ",\n",
            docs.Select(doc => $$"""
      {
        "identifier": {{JsonSerializer.Serialize(doc.Identifier)}},
        "title": {{JsonSerializer.Serialize(doc.Title)}},
        "creator": "Lewis Carroll",
        "date": "2010-03-01"
      }
"""));

        return $$"""
{
  "response": {
    "docs": [
{{docsJson}}
    ]
  }
}
""";
    }

    private static string CreateEmptySearchResponse() => """
{
  "response": {
    "docs": []
  }
}
""";

    private static string CreateMultiRepresentationMetadata() => """
{
  "metadata": {
    "language": "English"
  },
  "files": [
    {
      "name": "alice.m4b",
      "format": "LibriVox Apple Audiobook",
      "size": "900"
    },
    {
      "name": "alice_01_128kb.mp3",
      "format": "128Kbps MP3",
      "size": "100"
    },
    {
      "name": "alice_02_128kb.mp3",
      "format": "128Kbps MP3",
      "size": "200"
    },
    {
      "name": "alice_128kb_mp3.zip",
      "format": "128Kbps MP3 ZIP",
      "size": "280"
    },
    {
      "name": "cover.jpg",
      "format": "JPEG",
      "size": "50"
    }
  ]
}
""";
}
