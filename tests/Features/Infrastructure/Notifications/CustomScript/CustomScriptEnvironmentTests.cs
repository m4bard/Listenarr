/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Listenarr.Domain.Notifications;
using Listenarr.Infrastructure.Notifications.CustomScript;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Notifications.CustomScript
{
    /// <summary>
    /// Locks down the environment-variable contract a custom script receives.
    /// </summary>
    /// <remarks>
    /// These assertions are copied from what Sonarr, Radarr, Prowlarr and Readarr actually set, not
    /// from documentation. An operator's existing script switches on these exact strings, and a
    /// change here silently stops their script doing anything rather than failing loudly.
    /// </remarks>
    [Trait("Name", "CustomScriptEnvironmentTests")]
    [Trait("Category", "Notifications")]
    public class CustomScriptEnvironmentTests : BaseTests
    {
        private static NotificationEvent AnEvent(NotificationChannel channel) => new()
        {
            Channel = channel,
            Book = new NotificationEventBook
            {
                Id = 42,
                Title = "Frankenstein",
                Asin = "B002V1A0WE",
                Authors = new[] { "Mary Shelley", "Percy Shelley" },
                Narrators = new[] { "A Narrator" },
                Publisher = "A Publisher",
                Year = 1818,
            },
        };

        [Theory]
        [InlineData(NotificationChannel.Grab, "Grab")]
        [InlineData(NotificationChannel.Download, "Download")]
        [InlineData(NotificationChannel.DownloadFailed, "DownloadFailed")]
        [InlineData(NotificationChannel.BookAdded, "BookAdded")]
        [InlineData(NotificationChannel.BookAvailable, "BookAvailable")]
        [InlineData(NotificationChannel.Rename, "Rename")]
        public void Build_SetsEventTypeToTheChannelName(NotificationChannel channel, string expected)
        {
            var variables = CustomScriptEnvironment.Build(AnEvent(channel), "Listenarr", null);

            Assert.Equal(expected, variables["Listenarr_EventType"]);
        }

        [Fact]
        public void Build_UsesTheFamilyNameForACompletedImport()
        {
            // Sonarr, Radarr and Readarr all call the completed-import event "Download". A script
            // carried over from any of them switches on that literal string, so it must not become
            // "Imported" or "Completed" here.
            var variables = CustomScriptEnvironment.Build(AnEvent(NotificationChannel.Download), null, null);

            Assert.Equal("Download", variables["Listenarr_EventType"]);
        }

        [Fact]
        public void BuildTest_SetsOnlyTheThreeVariablesSonarrSets()
        {
            var variables = CustomScriptEnvironment.BuildTest("Listenarr", "https://example.invalid");

            Assert.Equal("Test", variables["Listenarr_EventType"]);
            Assert.Equal("Listenarr", variables["Listenarr_InstanceName"]);
            Assert.Equal("https://example.invalid", variables["Listenarr_ApplicationUrl"]);
            Assert.Equal(3, variables.Count);
        }

        [Fact]
        public void Build_SetsInstanceNameAndApplicationUrlOnEveryEvent()
        {
            var variables = CustomScriptEnvironment.Build(
                AnEvent(NotificationChannel.Grab),
                "Second Listenarr",
                "https://example.invalid/listenarr");

            Assert.Equal("Second Listenarr", variables["Listenarr_InstanceName"]);
            Assert.Equal("https://example.invalid/listenarr", variables["Listenarr_ApplicationUrl"]);
        }

        [Theory]
        [InlineData("/")]
        [InlineData("/listenarr")]
        [InlineData("")]
        [InlineData(null)]
        public void Build_GivesNoApplicationUrlWhenTheConfiguredValueIsNotAbsolute(string? urlBase)
        {
            // UrlBase defaults to "/" on a fresh install. A relative value is useless to a script
            // that wants to call back into the API, and handing one over produced the broken
            // thumbnail URLs reported in the Discord double-post issue.
            var variables = CustomScriptEnvironment.Build(AnEvent(NotificationChannel.Grab), "Listenarr", urlBase);

            Assert.Equal(string.Empty, variables["Listenarr_ApplicationUrl"]);
        }

        [Fact]
        public void Build_JoinsTextListsWithAPipe()
        {
            var variables = CustomScriptEnvironment.Build(AnEvent(NotificationChannel.Download), null, null);

            Assert.Equal("Mary Shelley|Percy Shelley", variables["Listenarr_Book_Authors"]);
        }

        [Fact]
        public void Build_JoinsAddedPathsWithAPipe()
        {
            var notification = new NotificationEvent
            {
                Channel = NotificationChannel.Download,
                AddedPaths = new[] { "/library/one.m4b", "/library/two.m4b" },
            };

            var variables = CustomScriptEnvironment.Build(notification, null, null);

            Assert.Equal("/library/one.m4b|/library/two.m4b", variables["Listenarr_AddedBookPaths"]);
        }

        [Fact]
        public void Build_SetsUnknownValuesToEmptyRatherThanOmittingThem()
        {
            // A script reads its variables unconditionally. An absent variable and an empty one are
            // different things in a shell, and the *arr implementations always set the key.
            var notification = new NotificationEvent { Channel = NotificationChannel.Grab };

            var variables = CustomScriptEnvironment.Build(notification, null, null);

            Assert.Equal(string.Empty, variables["Listenarr_Book_Title"]);
            Assert.Equal(string.Empty, variables["Listenarr_Book_Asin"]);
            Assert.Equal(string.Empty, variables["Listenarr_Release_Indexer"]);
            Assert.Equal(string.Empty, variables["Listenarr_Download_Id"]);
        }

        [Fact]
        public void Build_PrefixesEveryVariableWithTheApplicationName()
        {
            var variables = CustomScriptEnvironment.Build(AnEvent(NotificationChannel.Download), "Listenarr", null);

            Assert.All(variables.Keys, key => Assert.StartsWith("Listenarr_", key, StringComparison.Ordinal));
        }

        [Fact]
        public void Build_CarriesTheReleaseAndDownloadGroups()
        {
            var notification = new NotificationEvent
            {
                Channel = NotificationChannel.Grab,
                Release = new NotificationEventRelease
                {
                    Title = "Frankenstein 1818 M4B",
                    Indexer = "An Indexer",
                    Size = 1234,
                    Quality = "M4B",
                    Protocol = "Torrent",
                },
                Download = new NotificationEventDownload
                {
                    Id = "abc123",
                    Client = "A Client",
                    ClientType = "qbittorrent",
                },
            };

            var variables = CustomScriptEnvironment.Build(notification, null, null);

            Assert.Equal("Frankenstein 1818 M4B", variables["Listenarr_Release_Title"]);
            Assert.Equal("An Indexer", variables["Listenarr_Release_Indexer"]);
            Assert.Equal("1234", variables["Listenarr_Release_Size"]);
            Assert.Equal("abc123", variables["Listenarr_Download_Id"]);
            Assert.Equal("qbittorrent", variables["Listenarr_Download_Client_Type"]);
        }
    }
}
