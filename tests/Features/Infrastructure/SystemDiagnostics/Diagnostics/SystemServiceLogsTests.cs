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

using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.SystemDiagnostics.Diagnostics
{
    /// <summary>
    /// GetRecentLogs feeds two diagnostic surfaces: the System page's Recent Logs panel
    /// (GET /system/logs) and the file a user downloads to attach to a bug report
    /// (GET /system/logs/download, which builds its export from this method when no log
    /// file exists). Both are read as a record of what the application actually did, so
    /// nothing this method returns may describe work that was never performed.
    /// </summary>
    [Trait("Area", "Infrastructure")]
    [Trait("Name", "SystemServiceLogsTests")]
    [Trait("Category", "SystemService")]
    public class SystemServiceLogsTests : BaseTests
    {
        /// <summary>
        /// Claims about application activity that GetRecentLogs is not in a position to make.
        /// These are the entries it used to invent whenever no log file existed.
        /// </summary>
        private static readonly string[] UnsupportableClaims =
        {
            "Listenarr application started",
            "Database connection established",
            "System health check completed successfully",
            "Ready to accept requests"
        };

        [Fact]
        [Trait("Method", "GetRecentLogs")]
        [Trait("Scenario", "MissingLogFileAssertsNoActivity")]
        public void GetRecentLogs_WhenLogFileMissing_DoesNotClaimWorkThatNeverRan()
        {
            var systemService = CreateSystemService();

            var logs = systemService.GetRecentLogs();

            foreach (var claim in UnsupportableClaims)
            {
                Assert.DoesNotContain(logs, entry => entry.Message.Contains(claim, StringComparison.OrdinalIgnoreCase));
            }
        }

        [Fact]
        [Trait("Method", "GetRecentLogs")]
        [Trait("Scenario", "MissingLogFileReportsAbsenceOnce")]
        public void GetRecentLogs_WhenLogFileMissing_ReportsTheAbsenceAsASingleSystemEntry()
        {
            var systemService = CreateSystemService();

            var logs = systemService.GetRecentLogs();

            var entry = Assert.Single(logs);
            Assert.Equal("System", entry.Source);
            Assert.Equal("Info", entry.Level);
            Assert.Contains("log file", entry.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Method", "GetRecentLogs")]
        [Trait("Scenario", "MissingLogFileDoesNotBackdate")]
        public void GetRecentLogs_WhenLogFileMissing_DoesNotBackdateItsOwnEntry()
        {
            var before = DateTime.UtcNow;
            var systemService = CreateSystemService();

            var logs = systemService.GetRecentLogs();

            // The invented entries were stamped one to five minutes in the past, which
            // manufactured a startup timeline nobody had observed. Anything this method
            // reports about itself happened now.
            foreach (var entry in logs)
            {
                Assert.True(
                    entry.Timestamp >= before.AddSeconds(-5),
                    $"Entry '{entry.Message}' is stamped {entry.Timestamp:O}, before the call at {before:O}.");
            }
        }

        [Fact]
        [Trait("Method", "GetRecentLogs")]
        [Trait("Scenario", "MissingLogFileExportCarriesNoFabrication")]
        public void GetRecentLogs_WhenLogFileMissing_ProducesNoFabricatedExportContent()
        {
            var systemService = CreateSystemService();

            // Mirrors SystemController.DownloadLogs, which renders these entries into the
            // file the user downloads when no log file exists.
            var exported = string.Join(
                Environment.NewLine,
                systemService.GetRecentLogs(1000).Select(entry => $"[{entry.Level}] {entry.Message}"));

            foreach (var claim in UnsupportableClaims)
            {
                Assert.DoesNotContain(claim, exported, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        [Trait("Method", "GetRecentLogs")]
        [Trait("Scenario", "RealEntriesAreReturned")]
        public void GetRecentLogs_WhenLogFileHasEntries_ReturnsThemAndNothingElse()
        {
            WriteTodaysLog(
                "2026-09-04 10:00:00.000 +00:00 [INF] Scan of the library root completed",
                "2026-09-04 10:00:01.000 +00:00 [ERR] Indexer request failed");

            var systemService = CreateSystemService();

            var logs = systemService.GetRecentLogs();

            Assert.Equal(2, logs.Count);
            Assert.Equal("Scan of the library root completed", logs[0].Message);
            Assert.Equal("Info", logs[0].Level);
            Assert.Equal("Indexer request failed", logs[1].Message);
            Assert.Equal("Error", logs[1].Level);
        }

        [Fact]
        [Trait("Method", "GetRecentLogs")]
        [Trait("Scenario", "EmptyLogFileKeepsExistingReport")]
        public void GetRecentLogs_WhenLogFileHasNoParseableEntries_ReportsThat()
        {
            WriteTodaysLog("   ", string.Empty);

            var systemService = CreateSystemService();

            var logs = systemService.GetRecentLogs();

            var entry = Assert.Single(logs);
            Assert.Equal("System", entry.Source);
            Assert.Equal("Log file exists but contains no parseable entries", entry.Message);
        }

        [Fact]
        [Trait("Method", "GetRecentLogs")]
        [Trait("Scenario", "LimitIsHonoured")]
        public void GetRecentLogs_WhenLogFileHasMoreLinesThanTheLimit_ReturnsTheMostRecent()
        {
            WriteTodaysLog(
                "2026-09-04 10:00:00.000 +00:00 [INF] first",
                "2026-09-04 10:00:01.000 +00:00 [INF] second",
                "2026-09-04 10:00:02.000 +00:00 [INF] third");

            var systemService = CreateSystemService();

            var logs = systemService.GetRecentLogs(2);

            Assert.Equal(2, logs.Count);
            Assert.Equal("second", logs[0].Message);
            Assert.Equal("third", logs[1].Message);
        }

        private string LogsRoot => Path.Join(FileService.GetTempPath(), "logs");

        private void WriteTodaysLog(params string[] lines)
        {
            Directory.CreateDirectory(LogsRoot);
            File.WriteAllLines(
                Path.Join(LogsRoot, $"listenarr-{DateTime.UtcNow:yyyyMMdd}.log"),
                lines);
        }

        private SystemService CreateSystemService()
        {
            var configurationService = new Mock<IConfigurationService>();
            configurationService
                .Setup(service => service.GetApiConfigurationsAsync())
                .ReturnsAsync(new List<ApiConfiguration>());
            configurationService
                .Setup(service => service.GetDownloadClientConfigurationsAsync())
                .ReturnsAsync(new List<DownloadClientConfiguration>());

            var applicationPathService = new Mock<IApplicationPathService>();
            applicationPathService
                .Setup(service => service.LogsRootPath)
                .Returns(LogsRoot);

            var applicationVersionService = new Mock<IApplicationVersionService>();
            applicationVersionService
                .Setup(service => service.Resolve())
                .Returns("1.0.0");

            var rootFolderService = new Mock<IRootFolderService>();
            rootFolderService
                .Setup(service => service.GetAllAsync())
                .ReturnsAsync(new List<RootFolder>());

            return new SystemService(
                configurationService.Object,
                NullLogger<SystemService>.Instance,
                applicationPathService.Object,
                applicationVersionService.Object,
                rootFolderService.Object,
                new DiskSpaceProbe(NullLogger<DiskSpaceProbe>.Instance));
        }
    }
}
