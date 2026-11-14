/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
namespace Listenarr.Tests.Features.Architecture;

/// <summary>
/// The one table behind <see cref="IndexerTrafficArchitectureTests"/>. Every production file that
/// mentions an HTTP client is classified here exactly once, so a file added anywhere fails the
/// test until someone decides which side of the request gate it sits on.
/// </summary>
internal static class IndexerTrafficClassification
{
    /// <summary>The one file that hands indexer requests to an HttpClient, one gate call per request.</summary>
    internal const string DispatchHelper = "listenarr.application/Search/Indexers/Common/IndexerHttpDispatch.cs";

    /// <summary>Hosts the send that both redirect-following overloads use.</summary>
    internal const string SharedHelper = "listenarr.application/Security/Outbound/OutboundRequestSecurity.cs";

    /// <summary>Files that build or send requests to an indexer. They may only send through the gate.</summary>
    internal static readonly IReadOnlySet<string> IndexerPathFiles = new HashSet<string>(StringComparer.Ordinal)
    {
        "listenarr.api/Features/Indexers/IndexerDebugSearchWorkflow.cs",
        "listenarr.api/Features/Indexers/IndexerTestWorkflow.cs",
        "listenarr.api/Features/Indexers/IndexersController.cs",
        "listenarr.api/Features/Prowlarr/ProwlarrIndexerImportWorkflow.cs",
        "listenarr.application/Common/MyAnonamouseHelper.cs",
        "listenarr.application/Search/Core/SearchService.cs",
        "listenarr.application/Search/Indexers/Common/IndexerSearchWorkflow.cs",
        "listenarr.application/Search/Indexers/Torznab/TorznabResponseParser.cs",
        "listenarr.infrastructure/DownloadClients/Nzbget/NzbgetNzbDownloader.cs",
        "listenarr.infrastructure/Downloads/DirectDownload/DirectDownloadProcessor.cs",
        "listenarr.infrastructure/Downloads/Submission/MyAnonamouseTorrentFetcher.cs",
        "listenarr.infrastructure/Downloads/Submission/MyAnonamouseTorrentPreparationService.cs",
        "listenarr.infrastructure/Downloads/Submission/NzbFileDownloader.cs",
        "listenarr.infrastructure/Search/Providers/InternetArchive/InternetArchiveSearchProvider.cs",
        "listenarr.infrastructure/Search/Providers/MyAnonamouse/MyAnonamouseConnectionTester.cs",
        "listenarr.infrastructure/Search/Providers/MyAnonamouse/MyAnonamouseRequestFactory.cs",
        "listenarr.infrastructure/Search/Providers/MyAnonamouse/MyAnonamouseSearchProvider.cs",
        "listenarr.infrastructure/Search/Providers/Torznab/TorznabNewznabSearchProvider.cs",
        "listenarr.infrastructure/Torrents/TorrentFileDownloader.cs"
    };

    /// <summary>Directories whose files never talk to an indexer, each with the reason.</summary>
    internal static readonly IReadOnlyList<(string Prefix, string Reason)> NonIndexerDirectories = new[]
    {
        ("listenarr.api/Features/SystemDiagnostics/", "Discord bot setup calls, not an indexer"),
        ("listenarr.application/Metadata/", "metadata providers (Audible and cover art), not an indexer"),
        ("listenarr.application/Notifications/", "notification payload attachments, not an indexer"),
        ("listenarr.infrastructure/DependencyInjection/", "client registration only, sends nothing"),
        ("listenarr.infrastructure/DownloadClients/", "adapters for the user's own download clients"),
        ("listenarr.infrastructure/Ffmpeg/", "ffmpeg installer downloads"),
        ("listenarr.infrastructure/Images/", "cover image download, caching and validation"),
        ("listenarr.infrastructure/Metadata/", "metadata providers (Audnexus, Open Library), not an indexer"),
        ("listenarr.infrastructure/Notifications/", "webhook and chat delivery to the user's own endpoints")
    };

    /// <summary>
    /// Sends in <see cref="IndexerPathFiles"/> that deliberately bypass the gate: (file, exact matched
    /// text, how many times it occurs, why). A count that no longer matches fails the liveness check.
    /// </summary>
    internal static readonly IReadOnlyList<(string File, string Text, int Count, string Reason)> AllowedSends = new[]
    {
        (
            "listenarr.api/Features/Indexers/IndexerDebugSearchWorkflow.cs",
            "localClient.GetAsync(",
            1,
            "loopback call to the app's own search endpoint; the search inside it goes through the gate"),
        (
            "listenarr.api/Features/Indexers/IndexersController.cs",
            "_indexerRepository.DeleteAsync(",
            1,
            "repository call, not an HTTP request"),
        (
            "listenarr.api/Features/Prowlarr/ProwlarrIndexerImportWorkflow.cs",
            "OutboundRequestSecurity.SendWithValidatedRedirectsAsync(",
            1,
            "the user's Prowlarr management API, which has no indexer id to gate on"),
        (
            "listenarr.application/Search/Indexers/Common/IndexerSearchWorkflow.cs",
            "_httpClient.GetAsync(",
            1,
            "tests an API source configuration, which is keyed by a GUID rather than an indexer id"),
        (
            "listenarr.infrastructure/Downloads/DirectDownload/DirectDownloadProcessor.cs",
            "client.GetAsync(",
            1,
            "content transfer by the DirectDownload client, which has no indexer id"),
        (
            "listenarr.infrastructure/DownloadClients/Nzbget/NzbgetNzbDownloader.cs",
            "httpClient.SendAsync(",
            1,
            "dead code with no caller; deleting it is out of scope")
    };
}
