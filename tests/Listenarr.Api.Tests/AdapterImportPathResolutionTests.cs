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
using Listenarr.Domain.Utils;
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
            using var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"arguments\":{\"torrents\":[{\"id\":1,\"name\":\"Book.m4b\",\"downloadDir\":\"/downloads\"}]}}")
            };
            var handler = new DelegatingHandlerMock((req, ct) =>
            {
                return Task.FromResult(response);
            });

            var pathMapMock = new Mock<IRemotePathMappingService>();
            pathMapMock
                .Setup(m => m.TranslatePathAsync(
                    "trans-client",
                    It.Is<string>(p => string.Equals((p ?? string.Empty).Replace('\\', '/'), "/downloads/Book.m4b", StringComparison.Ordinal))))
                .ReturnsAsync(FileUtils.GetAbsolutePath("import", "Book.m4b"));

            using var httpClient = new HttpClient(handler);
            var adapter = new TransmissionAdapter(
                new TestHttpClientFactory(httpClient),
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

            Assert.Equal(FileUtils.GetAbsolutePath("import", "Book.m4b"), resolved.OutputPath);
        }

        [Fact]
        public async Task Transmission_GetImportItemAsync_MultiFile_ResolvesDirectoryPath()
        {
            using var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"arguments\":{\"torrents\":[{\"id\":2,\"name\":\"Book Folder\",\"downloadDir\":\"/downloads\"}]}}")
            };
            var handler = new DelegatingHandlerMock((req, ct) =>
            {
                return Task.FromResult(response);
            });

            var pathMapMock = new Mock<IRemotePathMappingService>();
            pathMapMock
                .Setup(m => m.TranslatePathAsync(
                    "trans-client",
                    It.Is<string>(p => string.Equals((p ?? string.Empty).Replace('\\', '/'), "/downloads/Book Folder", StringComparison.Ordinal))))
                .ReturnsAsync(FileUtils.GetAbsolutePath("import", "Book Folder"));

            using var httpClient = new HttpClient(handler);
            var adapter = new TransmissionAdapter(
                new TestHttpClientFactory(httpClient),
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

            Assert.Equal(FileUtils.GetAbsolutePath("import", "Book Folder"), resolved.OutputPath);
        }

        [Fact]
        public async Task Transmission_LegacyGetImportItemAsync_PopulatesClientReportedSourceFiles()
        {
            using var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"arguments\":{\"torrents\":[{\"id\":2,\"name\":\"Book Folder\",\"downloadDir\":\"/downloads\",\"files\":[{\"name\":\"Book Folder/chapter1.m4b\"},{\"name\":\"Book Folder/book.txt\"}]}]}}")
            };
            var handler = new DelegatingHandlerMock((req, ct) =>
            {
                return Task.FromResult(response);
            });

            var pathMapMock = new Mock<IRemotePathMappingService>();
            pathMapMock
                .Setup(m => m.TranslatePathAsync(
                    "trans-client",
                    It.Is<string>(p => string.Equals((p ?? string.Empty).Replace('\\', '/'), "/downloads/Book Folder", StringComparison.Ordinal))))
                .ReturnsAsync(FileUtils.GetAbsolutePath("import", "Book Folder"));
            pathMapMock
                .Setup(m => m.TranslatePathAsync(
                    "trans-client",
                    It.Is<string>(p => string.Equals((p ?? string.Empty).Replace('\\', '/'), "/downloads/Book Folder/chapter1.m4b", StringComparison.Ordinal))))
                .ReturnsAsync(FileUtils.GetAbsolutePath("import", "Book Folder", "chapter1.m4b"));
            pathMapMock
                .Setup(m => m.TranslatePathAsync(
                    "trans-client",
                    It.Is<string>(p => string.Equals((p ?? string.Empty).Replace('\\', '/'), "/downloads/Book Folder/book.txt", StringComparison.Ordinal))))
                .ReturnsAsync(FileUtils.GetAbsolutePath("import", "Book Folder", "book.txt"));

            using var httpClient = new HttpClient(handler);
            var adapter = new TransmissionAdapter(
                new TestHttpClientFactory(httpClient),
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

            var resolved = await adapter.GetImportItemAsync(
                client,
                new Download { Id = "download-1" },
                new QueueItem { Id = "2", ContentPath = string.Empty });

            Assert.Equal(FileUtils.GetAbsolutePath("import", "Book Folder"), resolved.ContentPath);
            Assert.Equal(
                new[]
                {
                    FileUtils.GetAbsolutePath("import", "Book Folder", "chapter1.m4b"),
                    FileUtils.GetAbsolutePath("import", "Book Folder", "book.txt")
                },
                resolved.SourceFiles);
        }

        [Fact]
        public async Task Sabnzbd_GetImportItemAsync_SingleFile_ResolvesFilePath()
        {
            using var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"history\":{\"slots\":[{\"nzo_id\":\"SABnzbd_nzo_1\",\"storage\":\"/completed/Book.m4b\"}]}}")
            };
            var handler = new DelegatingHandlerMock((req, ct) =>
            {
                return Task.FromResult(response);
            });

            var pathMapMock = new Mock<IRemotePathMappingService>();
            pathMapMock
                .Setup(m => m.TranslatePathAsync("sab-client", "/completed/Book.m4b"))
                .ReturnsAsync("E:/imports/Book.m4b");

            using var httpClient = new HttpClient(handler);
            var adapter = new SabnzbdAdapter(
                new TestHttpClientFactory(httpClient),
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
            using var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"history\":{\"slots\":[{\"nzo_id\":\"SABnzbd_nzo_2\",\"storage\":\"/completed/Book Folder\"}]}}")
            };
            var handler = new DelegatingHandlerMock((req, ct) =>
            {
                return Task.FromResult(response);
            });

            var pathMapMock = new Mock<IRemotePathMappingService>();
            pathMapMock
                .Setup(m => m.TranslatePathAsync("sab-client", "/completed/Book Folder"))
                .ReturnsAsync("E:/imports/Book Folder");

            using var httpClient = new HttpClient(handler);
            var adapter = new SabnzbdAdapter(
                new TestHttpClientFactory(httpClient),
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
            using var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildNzbGetHistoryResponse("101", "/nzbget/completed/Book.m4b"), Encoding.UTF8, "text/xml")
            };
            var handler = new DelegatingHandlerMock((req, ct) =>
            {
                return Task.FromResult(response);
            });

            var pathMapMock = new Mock<IRemotePathMappingService>();
            pathMapMock
                .Setup(m => m.TranslatePathAsync("nzb-client", "/nzbget/completed/Book.m4b"))
                .ReturnsAsync("F:/imports/Book.m4b");

            using var httpClient = new HttpClient(handler);
            var adapter = new NzbgetAdapter(
                new TestHttpClientFactory(httpClient),
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
            using var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BuildNzbGetHistoryResponse("202", "/nzbget/completed/Book Folder"), Encoding.UTF8, "text/xml")
            };
            var handler = new DelegatingHandlerMock((req, ct) =>
            {
                return Task.FromResult(response);
            });

            var pathMapMock = new Mock<IRemotePathMappingService>();
            pathMapMock
                .Setup(m => m.TranslatePathAsync("nzb-client", "/nzbget/completed/Book Folder"))
                .ReturnsAsync("F:/imports/Book Folder");

            using var httpClient = new HttpClient(handler);
            var adapter = new NzbgetAdapter(
                new TestHttpClientFactory(httpClient),
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
