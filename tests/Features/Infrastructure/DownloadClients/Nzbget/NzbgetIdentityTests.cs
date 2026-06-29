/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Xml.Linq;

using Listenarr.Tests.Mocks.Api;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.DownloadClients.Nzbget
{
    public class NzbgetIdentityTests
    {
        private sealed class TestHttpClientFactory : IHttpClientFactory
        {
            private readonly HttpClient _client;

            public TestHttpClientFactory(HttpClient client)
            {
                _client = client;
            }

            public HttpClient CreateClient(string name) => _client;
        }

        [Fact]
        public async Task AddAsync_ReturnsNumericExternalIdAndNoDroneContentId()
        {
            using var apiMock = new NzbgetApiMock();
            apiMock.QueueXmlRpcResponse("append", XmlRpcValueResponse("<i4>123</i4>"));
            using var http = new HttpClient(apiMock);
            var workflow = CreateAddWorkflow(http);

            var result = await workflow.AddAsync(CreateClient(), CreateSubmission());

            Assert.Equal("123", result.ExternalId);
            Assert.Null(result.ContentId);
            var appendCall = Assert.Single(apiMock.XmlRpcCalls);
            Assert.Equal("append", appendCall.MethodName);
            Assert.DoesNotContain(
                appendCall.Parameters,
                parameter => parameter.ToString(SaveOptions.DisableFormatting).Contains("drone", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task RemoveAsync_NonNumericId_ReturnsFalseWithoutSearchingHistory()
        {
            using var apiMock = new NzbgetApiMock();
            using var http = new HttpClient(apiMock);
            var workflow = CreateRemovalWorkflow(http);

            var removed = await workflow.RemoveAsync(CreateClient(), "legacy-drone-id");

            Assert.False(removed);
            Assert.Empty(apiMock.XmlRpcCalls);
        }

        [Fact]
        public async Task RemoveAsync_NumericId_RemovesFromQueueWhenHistoryDoesNotContainItem()
        {
            using var apiMock = new NzbgetApiMock();
            apiMock.QueueXmlRpcResponse("editqueue", XmlRpcValueResponse("<boolean>0</boolean>"));
            apiMock.QueueXmlRpcResponse("editqueue", XmlRpcValueResponse("<boolean>1</boolean>"));
            using var http = new HttpClient(apiMock);
            var workflow = CreateRemovalWorkflow(http);

            var removed = await workflow.RemoveAsync(CreateClient(), "321");

            Assert.True(removed);
            Assert.Collection(
                apiMock.XmlRpcCalls,
                historyCall => AssertEditQueueCall(historyCall, "HistoryDelete", 321),
                queueCall => AssertEditQueueCall(queueCall, "GroupDelete", 321));
        }

        private static NzbgetAddWorkflow CreateAddWorkflow(HttpClient http)
        {
            return new NzbgetAddWorkflow(
                new NzbgetXmlRpcClient(new TestHttpClientFactory(http), "nzbget"),
                NullLogger<NzbgetAddWorkflow>.Instance);
        }

        private static NzbgetRemovalWorkflow CreateRemovalWorkflow(HttpClient http)
        {
            return new NzbgetRemovalWorkflow(
                new NzbgetXmlRpcClient(new TestHttpClientFactory(http), "nzbget"),
                NullLogger<NzbgetRemovalWorkflow>.Instance);
        }

        private static DownloadClientConfiguration CreateClient()
        {
            return new DownloadClientConfiguration
            {
                Host = "localhost",
                Port = 6789
            };
        }

        private static PreparedUsenetSubmission CreateSubmission()
        {
            return new PreparedUsenetSubmission(
                Title: "Book",
                Artist: "Author",
                Album: "Album",
                Source: "indexer",
                Quality: null,
                Language: null,
                Size: 100,
                OriginalLocator: "release-id",
                NzbBytes: [1, 2, 3],
                FileName: "book.nzb");
        }

        private static void AssertEditQueueCall(
            NzbgetApiMock.XmlRpcCall call,
            string expectedCommand,
            int expectedId)
        {
            Assert.Equal("editqueue", call.MethodName);
            Assert.Equal(expectedCommand, call.Parameters[0].Element("string")?.Value);
            Assert.Equal(expectedId.ToString(), call.Parameters[3].Descendants("i4").Single().Value);
        }

        private static string XmlRpcValueResponse(string value)
        {
            return $$"""
            <?xml version="1.0"?>
            <methodResponse>
              <params>
                <param>
                  <value>{{value}}</value>
                </param>
              </params>
            </methodResponse>
            """;
        }
    }
}
