using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Listenarr.Api.Services.Adapters;
using Listenarr.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class QbittorrentAdapterTests
    {
        private class TestHttpClientFactory : IHttpClientFactory
        {
            private readonly HttpClient _client;
            public TestHttpClientFactory(HttpClient client) => _client = client;
            public HttpClient CreateClient(string name) => _client;
        }

        [Fact]
        public async Task TestConnection_When_VersionForbidden_Then_LoginSucceeds_ReturnsSuccess()
        {
            var loggedIn = false;

            var handler = new DelegatingHandlerMock((req, ct) =>
            {
                if (req.Method == HttpMethod.Get && req.RequestUri.PathAndQuery.StartsWith("/api/v2/app/version", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(!loggedIn ? HttpStatusCode.Forbidden : HttpStatusCode.OK)
                    {
                        Content = new StringContent(!loggedIn ? "Forbidden" : "v5.0.2")
                    });
                }

                if (req.Method == HttpMethod.Post && req.RequestUri.PathAndQuery.StartsWith("/api/v2/auth/login", StringComparison.OrdinalIgnoreCase))
                {
                    loggedIn = true;
                    var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Ok") };
                    resp.Headers.Add("Set-Cookie", "SID=1; HttpOnly; Path=/");
                    return Task.FromResult(resp);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            });

            var http = new HttpClient(handler);
            var factory = new TestHttpClientFactory(http);
            var pathMapMock = new Mock<Listenarr.Api.Services.IRemotePathMappingService>();
            var adapter = new QbittorrentAdapter(factory, pathMapMock.Object, Mock.Of<ITorrentFileDownloader>(), NullLogger<QbittorrentAdapter>.Instance);

            var cfg = new DownloadClientConfiguration
            {
                Host = "localhost",
                Port = 443,
                UseSSL = true,
                Username = "admin",
                Password = "admin"
            };

            var (success, message) = await adapter.TestConnectionAsync(cfg);

            Assert.True(success);
            Assert.Contains("connected", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task TestConnection_When_VersionForbidden_And_NoCredentials_ReturnsForbidden()
        {
            var handler = new DelegatingHandlerMock((req, ct) =>
            {
                if (req.Method == HttpMethod.Get && req.RequestUri.PathAndQuery.StartsWith("/api/v2/app/version", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("Forbidden") });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            });

            var http = new HttpClient(handler);
            var factory = new TestHttpClientFactory(http);
            var pathMapMock = new Mock<Listenarr.Api.Services.IRemotePathMappingService>();
            var adapter = new QbittorrentAdapter(factory, pathMapMock.Object, Mock.Of<ITorrentFileDownloader>(), NullLogger<QbittorrentAdapter>.Instance);

            var cfg = new DownloadClientConfiguration
            {
                Host = "localhost",
                Port = 443,
                UseSSL = true,
                Username = null,
                Password = null
            };

            var (success, message) = await adapter.TestConnectionAsync(cfg);

            Assert.False(success);
            Assert.Contains("Forbidden", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Area", "QbittorrentImportPathResolution")]
        [Trait("Scenario", "SingleFileResolvesContentFilePath")]
        public async Task GetImportItemAsync_SingleFileTorrent_ResolvesSpecificFilePath()
        {
            var files = ParseFiles("[{\"name\":\"Book.m4b\"}]");
            var resolvedPath = QbittorrentAdapter.ResolveTorrentContentPath("/downloads/audiobooks", files);
            Assert.Equal("/downloads/audiobooks/Book.m4b", NormalizePath(resolvedPath));
            await Task.CompletedTask;
        }

        [Fact]
        [Trait("Area", "QbittorrentImportPathResolution")]
        [Trait("Scenario", "MultiFileResolvesTopLevelDirectory")]
        public async Task GetImportItemAsync_MultiFileTorrent_ResolvesTopLevelFolderPath()
        {
            var files = ParseFiles("[{\"name\":\"Series Book/file1.m4b\"},{\"name\":\"Series Book/file2.m4b\"}]");
            var resolvedPath = QbittorrentAdapter.ResolveTorrentContentPath("/downloads/audiobooks", files);
            Assert.Equal("/downloads/audiobooks/Series Book", NormalizePath(resolvedPath));
            await Task.CompletedTask;
        }

        private static List<Dictionary<string, JsonElement>> ParseFiles(string json)
        {
            var root = JsonDocument.Parse(json).RootElement;
            var files = new List<Dictionary<string, JsonElement>>();

            foreach (var element in root.EnumerateArray())
            {
                var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in element.EnumerateObject())
                {
                    map[property.Name] = property.Value;
                }
                files.Add(map);
            }

            return files;
        }

        private static string NormalizePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/');
        }
    }
}
