using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Listenarr.Api.Services;
using Listenarr.Api.Services.Adapters;
using Listenarr.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class AdapterImportPathResolutionTests
    {
        private sealed class TestHttpClientFactory : IHttpClientFactory
        {
            private readonly HttpClient _httpClient;

            public TestHttpClientFactory(HttpClient httpClient)
            {
                _httpClient = httpClient;
            }

            public HttpClient CreateClient(string name)
            {
                return _httpClient;
            }
        }

        [Fact]
        public async Task Transmission_GetImportItemAsync_SingleFile_ResolvesFilePath()
        {
            var handler = new DelegatingHandlerMock((req, ct) =>
            {
                var body = "{\"arguments\":{\"torrents\":[{\"id\":1,\"name\":\"Book.m4b\",\"downloadDir\":\"/downloads\"}]}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
            });

            var pathMapMock = new Mock<IRemotePathMappingService>();
            pathMapMock
                .Setup(m => m.TranslatePathAsync(
                    "trans-client",
                    It.Is<string>(p => string.Equals((p ?? string.Empty).Replace('\\', '/'), "/downloads/Book.m4b", StringComparison.Ordinal))))
                .ReturnsAsync("D:/import/Book.m4b");

            var adapter = new TransmissionAdapter(
                new TestHttpClientFactory(new HttpClient(handler)),
                pathMapMock.Object,
                Mock.Of<ITorrentFileDownloader>(),
                NullLogger<TransmissionAdapter>.Instance);

            var client = new DownloadClientConfiguration
            {
                Id = "trans-client",
                Type = "transmission",
                Host = "localhost",
                Port = 9091
            };

            var item = new DownloadClientItem { DownloadId = "1", OutputPath = string.Empty };
            var resolved = await adapter.GetImportItemAsync(client, item);

            Assert.Equal("D:/import/Book.m4b", resolved.OutputPath);
        }

        [Fact]
        public async Task Transmission_GetImportItemAsync_MultiFile_ResolvesDirectoryPath()
        {
            var handler = new DelegatingHandlerMock((req, ct) =>
            {
                var body = "{\"arguments\":{\"torrents\":[{\"id\":2,\"name\":\"Book Folder\",\"downloadDir\":\"/downloads\"}]}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
            });

            var pathMapMock = new Mock<IRemotePathMappingService>();
            pathMapMock
                .Setup(m => m.TranslatePathAsync(
                    "trans-client",
                    It.Is<string>(p => string.Equals((p ?? string.Empty).Replace('\\', '/'), "/downloads/Book Folder", StringComparison.Ordinal))))
                .ReturnsAsync("D:/import/Book Folder");

            var adapter = new TransmissionAdapter(
                new TestHttpClientFactory(new HttpClient(handler)),
                pathMapMock.Object,
                Mock.Of<ITorrentFileDownloader>(),
                NullLogger<TransmissionAdapter>.Instance);

            var client = new DownloadClientConfiguration
            {
                Id = "trans-client",
                Type = "transmission",
                Host = "localhost",
                Port = 9091
            };

            var item = new DownloadClientItem { DownloadId = "2", OutputPath = string.Empty };
            var resolved = await adapter.GetImportItemAsync(client, item);

            Assert.Equal("D:/import/Book Folder", resolved.OutputPath);
        }

        [Fact]
        public async Task Sabnzbd_GetImportItemAsync_SingleFile_ResolvesFilePath()
        {
            var handler = new DelegatingHandlerMock((req, ct) =>
            {
                var body = "{\"history\":{\"slots\":[{\"nzo_id\":\"SABnzbd_nzo_1\",\"storage\":\"/completed/Book.m4b\"}]}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
            });

            var pathMapMock = new Mock<IRemotePathMappingService>();
            pathMapMock
                .Setup(m => m.TranslatePathAsync("sab-client", "/completed/Book.m4b"))
                .ReturnsAsync("E:/imports/Book.m4b");

            var adapter = new SabnzbdAdapter(
                new TestHttpClientFactory(new HttpClient(handler)),
                pathMapMock.Object,
                Mock.Of<INzbUrlResolver>(),
                NullLogger<SabnzbdAdapter>.Instance);

            var client = new DownloadClientConfiguration
            {
                Id = "sab-client",
                Type = "sabnzbd",
                Host = "localhost",
                Port = 8080,
                Settings = new Dictionary<string, object> { ["apiKey"] = "apikey" }
            };

            var item = new DownloadClientItem { DownloadId = "SABnzbd_nzo_1", OutputPath = string.Empty };
            var resolved = await adapter.GetImportItemAsync(client, item);

            Assert.Equal("E:/imports/Book.m4b", resolved.OutputPath);
        }

        [Fact]
        public async Task Sabnzbd_GetImportItemAsync_MultiFile_ResolvesDirectoryPath()
        {
            var handler = new DelegatingHandlerMock((req, ct) =>
            {
                var body = "{\"history\":{\"slots\":[{\"nzo_id\":\"SABnzbd_nzo_2\",\"storage\":\"/completed/Book Folder\"}]}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
            });

            var pathMapMock = new Mock<IRemotePathMappingService>();
            pathMapMock
                .Setup(m => m.TranslatePathAsync("sab-client", "/completed/Book Folder"))
                .ReturnsAsync("E:/imports/Book Folder");

            var adapter = new SabnzbdAdapter(
                new TestHttpClientFactory(new HttpClient(handler)),
                pathMapMock.Object,
                Mock.Of<INzbUrlResolver>(),
                NullLogger<SabnzbdAdapter>.Instance);

            var client = new DownloadClientConfiguration
            {
                Id = "sab-client",
                Type = "sabnzbd",
                Host = "localhost",
                Port = 8080,
                Settings = new Dictionary<string, object> { ["apiKey"] = "apikey" }
            };

            var item = new DownloadClientItem { DownloadId = "SABnzbd_nzo_2", OutputPath = string.Empty };
            var resolved = await adapter.GetImportItemAsync(client, item);

            Assert.Equal("E:/imports/Book Folder", resolved.OutputPath);
        }

        [Fact]
        public async Task Nzbget_GetImportItemAsync_SingleFile_ResolvesFilePath()
        {
            var handler = new DelegatingHandlerMock((req, ct) =>
            {
                var xml = BuildNzbGetHistoryResponse("101", "/nzbget/completed/Book.m4b");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(xml, Encoding.UTF8, "text/xml")
                });
            });

            var pathMapMock = new Mock<IRemotePathMappingService>();
            pathMapMock
                .Setup(m => m.TranslatePathAsync("nzb-client", "/nzbget/completed/Book.m4b"))
                .ReturnsAsync("F:/imports/Book.m4b");

            var adapter = new NzbgetAdapter(
                new TestHttpClientFactory(new HttpClient(handler)),
                Mock.Of<INzbUrlResolver>(),
                pathMapMock.Object,
                NullLogger<NzbgetAdapter>.Instance);

            var client = new DownloadClientConfiguration
            {
                Id = "nzb-client",
                Type = "nzbget",
                Host = "localhost",
                Port = 6789
            };

            var item = new DownloadClientItem { DownloadId = "101", OutputPath = string.Empty };
            var resolved = await adapter.GetImportItemAsync(client, item);

            Assert.Equal("F:/imports/Book.m4b", resolved.OutputPath);
        }

        [Fact]
        public async Task Nzbget_GetImportItemAsync_MultiFile_ResolvesDirectoryPath()
        {
            var handler = new DelegatingHandlerMock((req, ct) =>
            {
                var xml = BuildNzbGetHistoryResponse("202", "/nzbget/completed/Book Folder");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(xml, Encoding.UTF8, "text/xml")
                });
            });

            var pathMapMock = new Mock<IRemotePathMappingService>();
            pathMapMock
                .Setup(m => m.TranslatePathAsync("nzb-client", "/nzbget/completed/Book Folder"))
                .ReturnsAsync("F:/imports/Book Folder");

            var adapter = new NzbgetAdapter(
                new TestHttpClientFactory(new HttpClient(handler)),
                Mock.Of<INzbUrlResolver>(),
                pathMapMock.Object,
                NullLogger<NzbgetAdapter>.Instance);

            var client = new DownloadClientConfiguration
            {
                Id = "nzb-client",
                Type = "nzbget",
                Host = "localhost",
                Port = 6789
            };

            var item = new DownloadClientItem { DownloadId = "202", OutputPath = string.Empty };
            var resolved = await adapter.GetImportItemAsync(client, item);

            Assert.Equal("F:/imports/Book Folder", resolved.OutputPath);
        }

        private static string BuildNzbGetHistoryResponse(string id, string destDir)
        {
            return $"<?xml version=\"1.0\"?>" +
                   "<methodResponse><params><param><value><array><data>" +
                   "<value><struct>" +
                   $"<member><name>ID</name><value><string>{WebUtility.HtmlEncode(id)}</string></value></member>" +
                   $"<member><name>DestDir</name><value><string>{WebUtility.HtmlEncode(destDir)}</string></value></member>" +
                   "</struct></value>" +
                   "</data></array></value></param></params></methodResponse>";
        }
    }
}
