/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using System.Text.RegularExpressions;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Architecture;

/// <summary>
/// Every request to an indexer goes through the request gate. A text scan cannot prove a delegate
/// is gated, so the rule is structural: indexer-path files never call an HTTP client or the
/// redirect wrapper themselves, they call the gate or <c>IndexerHttpDispatch</c>.
/// </summary>
[Trait("Name", "IndexerTrafficArchitectureTests")]
[Trait("Category", "Architecture")]
public sealed class IndexerTrafficArchitectureTests : BaseTests
{
    private static readonly string RepositoryRoot = TestUtils.FindRepositoryRoot();

    private static readonly string[] ProjectRoots =
    {
        "listenarr.domain",
        "listenarr.application",
        "listenarr.infrastructure",
        "listenarr.api"
    };

    private static readonly Regex HttpMention = new(
        @"HttpClient|HttpRequestMessage|IHttpClientFactory|SendWithValidatedRedirectsAsync",
        RegexOptions.Compiled);

    private static readonly Regex RawSend = new(
        @"(?<receiver>@?\w*)\s*\.\s*(?:Send|SendAsync|GetAsync|PostAsync|PutAsync|PatchAsync|DeleteAsync|GetStringAsync|GetByteArrayAsync|GetStreamAsync|GetFromJsonAsync|PostAsJsonAsync|PutAsJsonAsync|PatchAsJsonAsync|DeleteFromJsonAsync)(?:<[^>()]*>)?\(",
        RegexOptions.Compiled);

    private static readonly Regex RedirectSend = new(
        @"(?:\w+\s*\.\s*)?SendWithValidatedRedirectsAsync\(",
        RegexOptions.Compiled);

    private static readonly Regex GateDeclaration = new(
        @"IIndexerRequestGate\??\s+(?<name>@?\w+)",
        RegexOptions.Compiled);

    [Fact]
    public void EveryFileThatMentionsAnHttpClient_IsClassified()
    {
        var unclassified = SelectedFiles()
            .Where(file => Classify(file) == null)
            .ToList();

        Assert.True(
            unclassified.Count == 0,
            "Classify these files in IndexerTrafficClassification:\n" + string.Join("\n", unclassified));
    }

    [Fact]
    public void IndexerPathFiles_SendOnlyThroughTheGate()
    {
        var violations = new List<string>();
        foreach (var file in IndexerTrafficClassification.IndexerPathFiles)
        {
            var allowed = IndexerTrafficClassification.AllowedSends
                .Where(entry => entry.File == file)
                .Select(entry => entry.Text)
                .ToHashSet(StringComparer.Ordinal);
            var path = Path.Join(RepositoryRoot, file);
            if (!File.Exists(path))
            {
                continue;
            }

            violations.AddRange(
                FindForbiddenSends(File.ReadAllText(path))
                    .Where(text => !allowed.Contains(text))
                    .Select(text => $"{file}: {text}"));
        }

        Assert.True(
            violations.Count == 0,
            "Send through IIndexerRequestGate or IndexerHttpDispatch instead:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Scanner_FlagsPlantedSends()
    {
        var planted = new[]
        {
            "var r = await _httpClient.SendAsync(request, token);",
            "var r = client.Send(request);",
            "var r = await factory.CreateClient(\"x\")\n    .SendAsync(request, token);",
            "var r = await http.GetFromJsonAsync<Foo>(url);",
            "var (r, _) = await OutboundRequestSecurity.SendWithValidatedRedirectsAsync(factory, uri, send, log);",
            "var (r, _) = await SendWithValidatedRedirectsAsync(factory, uri, send, log);"
        };

        foreach (var line in planted)
        {
            Assert.NotEmpty(FindForbiddenSends(line));
        }
    }

    [Fact]
    public void Scanner_ExemptsGateAndDispatchReceivers()
    {
        const string gated = @"
            private readonly IIndexerRequestGate _requestGate;
            var a = await _requestGate.SendAsync(context, send, token);
            var b = await IndexerHttpDispatch.SendAsync(gate, context, client, request, null, token);";

        Assert.Empty(FindForbiddenSends(gated));
        Assert.NotEmpty(FindForbiddenSends(gated + "\nvar c = await _httpClient.SendAsync(request);"));
    }

    [Fact]
    public void Classification_IsLive()
    {
        var selected = SelectedFiles().ToHashSet(StringComparer.Ordinal);

        var missing = IndexerTrafficClassification.IndexerPathFiles
            .Append(IndexerTrafficClassification.DispatchHelper)
            .Append(IndexerTrafficClassification.SharedHelper)
            .Where(file => !selected.Contains(file))
            .ToList();
        Assert.True(missing.Count == 0, "Classified files that are gone or no longer mention an HTTP client:\n" + string.Join("\n", missing));

        var emptyDirectories = IndexerTrafficClassification.NonIndexerDirectories
            .Where(directory => !selected.Any(file => file.StartsWith(directory.Prefix, StringComparison.Ordinal)))
            .Select(directory => directory.Prefix)
            .ToList();
        Assert.True(emptyDirectories.Count == 0, "Directories that match no file:\n" + string.Join("\n", emptyDirectories));

        var overlapping = selected
            .Where(file => NamedClassesOf(file).Count > 1)
            .ToList();
        Assert.True(overlapping.Count == 0, "Files classified more than once:\n" + string.Join("\n", overlapping));
    }

    [Fact]
    public void AllowedSends_MatchALiveOccurrence()
    {
        var stale = new List<string>();
        foreach (var (file, text, count, _) in IndexerTrafficClassification.AllowedSends)
        {
            if (!IndexerTrafficClassification.IndexerPathFiles.Contains(file))
            {
                stale.Add($"{file}: not an indexer-path file");
                continue;
            }

            var path = Path.Join(RepositoryRoot, file);
            var actual = File.Exists(path)
                ? FindForbiddenSends(File.ReadAllText(path)).Count(found => found == text)
                : 0;
            if (actual != count)
            {
                stale.Add($"{file}: {text} expected {count}, found {actual}");
            }
        }

        Assert.True(stale.Count == 0, "Stale allowed sends:\n" + string.Join("\n", stale));
    }

    [Fact]
    public void ScanSelectsIndexerPathFiles()
    {
        var indexerFiles = SelectedFiles()
            .Where(file => Classify(file) == TrafficClass.IndexerPath)
            .ToList();

        Assert.NotEmpty(indexerFiles);
    }

    private enum TrafficClass
    {
        IndexerPath,
        DispatchHelper,
        SharedHelper,
        NonIndexer
    }

    // A file named in the table wins over the directory it sits in, so one indexer-facing file
    // can live inside an otherwise unrelated directory.
    private static TrafficClass? Classify(string file)
    {
        var named = NamedClassesOf(file);
        if (named.Count > 0)
        {
            return named[0];
        }

        return IndexerTrafficClassification.NonIndexerDirectories
            .Any(directory => file.StartsWith(directory.Prefix, StringComparison.Ordinal))
            ? TrafficClass.NonIndexer
            : null;
    }

    private static List<TrafficClass> NamedClassesOf(string file)
    {
        var classes = new List<TrafficClass>();
        if (IndexerTrafficClassification.IndexerPathFiles.Contains(file))
        {
            classes.Add(TrafficClass.IndexerPath);
        }

        if (file == IndexerTrafficClassification.DispatchHelper)
        {
            classes.Add(TrafficClass.DispatchHelper);
        }

        if (file == IndexerTrafficClassification.SharedHelper)
        {
            classes.Add(TrafficClass.SharedHelper);
        }

        return classes;
    }

    /// <summary>
    /// The sends in <paramref name="source"/> that are neither on a gate-typed receiver nor on
    /// IndexerHttpDispatch, as receiver and method text with whitespace removed.
    /// </summary>
    private static List<string> FindForbiddenSends(string source)
    {
        var gates = GateDeclaration.Matches(source)
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);
        gates.Add("IndexerHttpDispatch");

        var found = RawSend.Matches(source)
            .Where(match => !gates.Contains(match.Groups["receiver"].Value))
            .Select(match => Regex.Replace(match.Value, @"\s+", string.Empty))
            .ToList();
        found.AddRange(RedirectSend.Matches(source).Select(match => Regex.Replace(match.Value, @"\s+", string.Empty)));
        return found;
    }

    private static IEnumerable<string> SelectedFiles() =>
        ProjectRoots
            .SelectMany(root => Directory.EnumerateFiles(Path.Join(RepositoryRoot, root), "*.cs", SearchOption.AllDirectories))
            .Where(file => !IsExcluded(file))
            .Where(file => HttpMention.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(RepositoryRoot, file).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(file => file, StringComparer.Ordinal);

    private static bool IsExcluded(string file) =>
        new[] { "bin", "obj", "Migrations" }.Any(segment =>
            file.Contains($"{Path.DirectorySeparatorChar}{segment}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
}
