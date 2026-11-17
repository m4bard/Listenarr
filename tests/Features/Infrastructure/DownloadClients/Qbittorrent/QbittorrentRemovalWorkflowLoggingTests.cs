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
using System.Net;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks.Api;

namespace Listenarr.Tests.Features.Infrastructure.DownloadClients.Qbittorrent
{
    /// <summary>
    /// Removal asks qBittorrent whether the torrent is present before deleting it, and logs the
    /// client's body when that presence check fails. Redaction masks known secret values only, so
    /// these pin that the body also goes through the sanitizer: no newline can start a forged log
    /// line, and an oversized body cannot flood the log.
    /// </summary>
    [Trait("Area", "DownloadClients")]
    [Trait("Name", "QbittorrentRemovalWorkflowLoggingTests")]
    [Trait("Category", "Qbittorrent")]
    public sealed class QbittorrentRemovalWorkflowLoggingTests : BaseTests
    {
        private const string PresenceCheckLogFragment = "presence check returned";

        private async Task<IReadOnlyList<string>> LoggedPresenceCheckLinesForBody(string body)
        {
            var logs = new RemovalLogRecorder();
            Init(builder => builder
                .WithSingleton<ILoggerProvider>(logs)
                .WithMocks(RemovalLogRecorder.CaptureEveryLevel));

            var apiMock = _provider.GetRequiredService<QbittorrentApiMock>();
            apiMock.InfoStatusCode = HttpStatusCode.InternalServerError;
            apiMock.InfoErrorResponseBody = body;

            var client = await _downloadClientConfigurationRepository.SaveAsync(new DownloadClientConfigurationBuilder()
                .WithHost("localhost")
                .WithPort(8080)
                .WithUsername("admin")
                .WithPassword("admin")
                .WithType("qbittorrent")
                .Build());

            var gateway = _provider.GetRequiredService<IDownloadClientGateway>();

            Assert.False(await gateway.RemoveAsync(client, "ABCDEF123456", deleteFiles: false));

            return logs.EntriesContaining(PresenceCheckLogFragment);
        }

        [Fact]
        [Trait("Scenario", "A presence-check error body cannot forge a log line")]
        public async Task RemoveAsync_WhenThePresenceCheckFails_SanitizesTheLoggedResponseBody()
        {
            // A newline, then a line that reads like a log entry of its own.
            var lines = await LoggedPresenceCheckLinesForBody(
                "Internal error\r\nfail: Listenarr.Api[0] Download client removed every audiobook file");

            var line = Assert.Single(lines);
            Assert.DoesNotContain('\n', line);
            Assert.DoesNotContain('\r', line);
            Assert.Contains("Download client removed every audiobook file", line, StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Scenario", "An oversized presence-check error body is truncated")]
        public async Task RemoveAsync_WhenThePresenceCheckFailsWithAHugeBody_TruncatesTheLoggedResponseBody()
        {
            var lines = await LoggedPresenceCheckLinesForBody(new string('x', 4000));

            var line = Assert.Single(lines);
            Assert.Contains(new string('x', 500) + "...", line, StringComparison.Ordinal);
            Assert.DoesNotContain(new string('x', 501), line, StringComparison.Ordinal);
        }

        private sealed class RemovalLogRecorder : ILoggerProvider
        {
            private readonly object _gate = new();
            private readonly List<string> _messages = [];

            // AddLogging() floors the factory at Information. The Warning line under test is above
            // that, but pinning the level here keeps the recorder usable if it is ever lowered.
            public static ServiceDescriptor CaptureEveryLevel { get; } =
                ServiceDescriptor.Singleton<Microsoft.Extensions.Options.IConfigureOptions<LoggerFilterOptions>>(
                    new Microsoft.Extensions.Options.ConfigureOptions<LoggerFilterOptions>(
                        options => options.MinLevel = LogLevel.Trace));

            public ILogger CreateLogger(string categoryName) => new RecordingLogger(this);

            public IReadOnlyList<string> EntriesContaining(string fragment)
            {
                lock (_gate)
                {
                    return [.. _messages.Where(message => message.Contains(fragment, StringComparison.Ordinal))];
                }
            }

            public void Dispose()
            {
            }

            private void Record(string message)
            {
                lock (_gate)
                {
                    _messages.Add(message);
                }
            }

            private sealed class RecordingLogger(RemovalLogRecorder owner) : ILogger
            {
                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

                public bool IsEnabled(LogLevel logLevel) => true;

                public void Log<TState>(
                    LogLevel logLevel,
                    EventId eventId,
                    TState state,
                    Exception? exception,
                    Func<TState, Exception?, string> formatter)
                {
                    ArgumentNullException.ThrowIfNull(formatter);
                    owner.Record(formatter(state, exception));
                }
            }
        }
    }
}
