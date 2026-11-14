/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Listenarr.Application.Search.Discovery;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Search.Providers.Torznab;

public partial class TorznabNewznabSearchProvider : IIndexerFeedProvider
{
    private static readonly XNamespace TorznabFeedNamespace = "http://torznab.com/schemas/2015/feed";
    private static readonly XNamespace NewznabFeedNamespace = "http://www.newznab.com/DTD/2010/feeds/attributes/";

    public bool Supports(Indexer indexer)
    {
        ArgumentNullException.ThrowIfNull(indexer);

        return indexer.Implementation.Equals("Torznab", StringComparison.OrdinalIgnoreCase)
            || indexer.Implementation.Equals("Newznab", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IndexerFeedPage> FetchPageAsync(
        Indexer indexer,
        int offset,
        IndexerSearchContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(indexer);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        // A caller that forgot to set the kind would send the feed request as a Search.
        if (context.QueryKind is not (IndexerRequestKind.FeedPoll or IndexerRequestKind.FeedPage))
        {
            throw new ArgumentException(
                $"A feed page is sent as {IndexerRequestKind.FeedPoll} or {IndexerRequestKind.FeedPage}, not {context.QueryKind}.",
                nameof(context));
        }

        try
        {
            var url = TorznabNewznabFeedRequestBuilder.BuildUrl(indexer, offset);
            var redactedUrl = LogRedaction.RedactText(url, LogRedaction.GetSensitiveValuesFromEnvironment().Concat(new[] { indexer.ApiKey ?? string.Empty }));
            _logger.LogDebug("Indexer feed URL: {Url}", redactedUrl);

            using var requestMessage = CreateFeedRequest(url);
            var requestContext = new IndexerRequestContext(indexer.Id, context.Origin, context.QueryKind, context.CycleAllowance);
            var sendResult = await _requestGate.SendAsync(
                requestContext,
                token => _httpClient.SendAsync(requestMessage, token),
                cancellationToken);
            if (sendResult is IndexerRequestResult.Deferred deferred)
            {
                _logger.LogInformation(
                    "Indexer {Name} feed page at offset {Offset} deferred until {Until} ({Reason})",
                    indexer.Name, offset, deferred.Deferral.UntilUtc, deferred.Deferral.Reason);
                return new IndexerFeedPage(
                    indexer.Id, IndexerSearchStatus.Deferred, Array.Empty<DiscoveredFeedItem>(), 0, null, deferred.Deferral);
            }

            using var response = ((IndexerRequestResult.Sent)sendResult).Response;
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Indexer {Name} feed returned status {Status}", indexer.Name, response.StatusCode);
                return FeedFailed(indexer, $"status {(int)response.StatusCode}");
            }

            var xmlContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var failureReason = ReadFeedChannel(xmlContent, out var itemsReceived, out var totalAvailable);
            if (failureReason != null)
            {
                _logger.LogWarning("Indexer {Name} feed response was not a feed: {Reason}", indexer.Name, failureReason);
                return FeedFailed(indexer, failureReason);
            }

            // Detail-page enrichment is off: it would send one more request per item, and a feed page is one request.
            var results = await ParseTorznabResponseAsync(xmlContent, indexer, fetchDetailPages: false);
            var items = ToFeedItems(results, out var withoutIdentity);
            if (withoutIdentity > 0)
            {
                _logger.LogWarning(
                    "Indexer {Name} feed page at offset {Offset}: skipped {Count} items with no release identity",
                    indexer.Name, offset, withoutIdentity);
            }

            _logger.LogInformation(
                "Indexer {Name} feed page at offset {Offset}: {Received} items received, {Kept} kept",
                indexer.Name, offset, itemsReceived, items.Count);
            return new IndexerFeedPage(indexer.Id, IndexerSearchStatus.Completed, items, itemsReceived, totalAvailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new IndexerFeedPage(indexer.Id, IndexerSearchStatus.Cancelled, Array.Empty<DiscoveredFeedItem>(), 0, null);
        }
        catch (OperationCanceledException ex)
        {
            // The caller's token is not cancelled, so this is the HttpClient's per-request timeout.
            _logger.LogWarning(ex, "Timed out reading the feed of Torznab/Newznab indexer {Name}", indexer.Name);
            return FeedFailed(indexer, "timeout");
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            _logger.LogError(ex, "Error reading the feed of Torznab/Newznab indexer {Name}", indexer.Name);
            return FeedFailed(indexer, "error");
        }
    }

    /// <summary>
    /// Keeps the results that have a release identity. The identity comes from
    /// <see cref="ReleaseIdentity"/>, never from the result's id, which the parser invents
    /// afresh for an item without a guid.
    /// </summary>
    internal static IReadOnlyList<DiscoveredFeedItem> ToFeedItems(
        IEnumerable<IndexerSearchResult> results,
        out int withoutIdentity)
    {
        var items = new List<DiscoveredFeedItem>();
        withoutIdentity = 0;
        foreach (var result in results)
        {
            var releaseId = ReleaseIdentity.For(SearchResultConverters.ToSearchResult(result));
            if (releaseId is null)
            {
                withoutIdentity++;
                continue;
            }

            items.Add(new DiscoveredFeedItem(result, releaseId, ParsePublishedUtc(result.PublishedDate)));
        }

        return items;
    }

    /// <summary>
    /// The parser writes a parsed publish time as a round-trip string, or an empty string when
    /// the item had none it could read. A value without a zone is taken as UTC.
    /// </summary>
    internal static DateTime? ParsePublishedUtc(string? publishedDate)
    {
        if (string.IsNullOrWhiteSpace(publishedDate))
        {
            return null;
        }

        return DateTimeOffset.TryParse(publishedDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var published)
            ? published.UtcDateTime
            : null;
    }

    /// <summary>
    /// Checks the response is a feed and reads its item count and stated total. Returns null
    /// for a feed, otherwise a short reason. An indexer reports errors such as a bad API key
    /// as an error document with status 200, which must not read as an empty page.
    /// </summary>
    private static string? ReadFeedChannel(string xmlContent, out int itemsReceived, out int? totalAvailable)
    {
        itemsReceived = 0;
        totalAvailable = null;

        XDocument document;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                IgnoreWhitespace = true,
                IgnoreComments = true
            };
            using var reader = XmlReader.Create(new StringReader(xmlContent), settings);
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return "unparseable response";
        }

        if (document.Root?.Name.LocalName == "error")
        {
            return $"indexer error {document.Root.Attribute("code")?.Value}".TrimEnd();
        }

        var channel = document.Root?.Element("channel");
        if (channel == null)
        {
            return "no channel element";
        }

        itemsReceived = channel.Elements("item").Count();
        var total = (channel.Element(NewznabFeedNamespace + "response") ?? channel.Element(TorznabFeedNamespace + "response"))
            ?.Attribute("total")?.Value;
        if (int.TryParse(total, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedTotal))
        {
            totalAvailable = parsedTotal;
        }

        return null;
    }

    private static IndexerFeedPage FeedFailed(Indexer indexer, string reason)
    {
        return new IndexerFeedPage(
            indexer.Id, IndexerSearchStatus.Failed, Array.Empty<DiscoveredFeedItem>(), 0, null, FailureReason: reason);
    }

    private static HttpRequestMessage CreateFeedRequest(string url)
    {
        var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
        var version = typeof(TorznabNewznabSearchProvider).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        requestMessage.Headers.UserAgent.ParseAdd($"Listenarr/{version} (+https://github.com/Listenarrs/listenarr)");
        return requestMessage;
    }
}
