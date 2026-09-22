/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Listenarr.Domain.Notifications;
using Listenarr.Infrastructure.Notifications.Email;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Notifications.Email
{
    /// <summary>
    /// What an operator actually receives is decided here and nowhere else, which is why this has
    /// its own fixture rather than being covered incidentally by the provider's tests. The Custom
    /// Script provider's equivalent mapper is tested the same way, in CustomScriptEnvironmentTests.
    /// </summary>
    [Trait("Name", "EmailMessageBuilderTests")]
    [Trait("Category", "Notifications")]
    public class EmailMessageBuilderTests : BaseTests
    {
        [Theory]
        [InlineData(NotificationChannel.Grab, "Listenarr - Book Grabbed")]
        [InlineData(NotificationChannel.Download, "Listenarr - Book Downloaded")]
        [InlineData(NotificationChannel.DownloadFailed, "Listenarr - Download Failed")]
        [InlineData(NotificationChannel.BookAdded, "Listenarr - Book Added")]
        [InlineData(NotificationChannel.BookAvailable, "Listenarr - Book Available")]
        [InlineData(NotificationChannel.Rename, "Listenarr - Book Renamed")]
        [InlineData(NotificationChannel.Test, "Listenarr - Test Notification")]
        public void Subject_NamesTheEventInTheFamilysBrandedForm(NotificationChannel channel, string expected)
        {
            Assert.Equal(expected, EmailMessageBuilder.Subject(channel));
        }

        [Fact]
        public void Subject_CoversEveryChannelWithoutFallingBackToTheEnumName()
        {
            // Subject ends in a default arm that returns the enum name. If a channel is added and
            // nobody writes wording for it, an operator receives "Listenarr - SomeNewChannel".
            // Every channel that has wording differs from that, so this catches the gap.
            foreach (var channel in Enum.GetValues<NotificationChannel>())
            {
                Assert.NotEqual(
                    EmailMessageBuilder.SubjectPrefix + channel,
                    EmailMessageBuilder.Subject(channel));
            }
        }

        [Theory]
        [InlineData(NotificationChannel.Grab, "was sent to a download client")]
        [InlineData(NotificationChannel.Download, "finished downloading and was imported")]
        [InlineData(NotificationChannel.DownloadFailed, "failed to download and was not imported")]
        [InlineData(NotificationChannel.BookAdded, "was added to the library")]
        [InlineData(NotificationChannel.BookAvailable, "were found but not imported")]
        [InlineData(NotificationChannel.Rename, "were moved on disk")]
        public void Body_OpensWithASentenceThatSaysWhatHappened(NotificationChannel channel, string expected)
        {
            var body = EmailMessageBuilder.Body(new NotificationEvent
            {
                Channel = channel,
                Book = new NotificationEventBook { Id = 1, Title = "Frankenstein" },
            });

            Assert.Contains(expected, body, StringComparison.Ordinal);
            Assert.Contains("Frankenstein", body, StringComparison.Ordinal);
        }

        [Fact]
        public void Body_OpensWithRealWordingForEveryChannel()
        {
            // Summary ends in a default arm that prints "Frankenstein: SomeNewChannel." A channel
            // added without wording would otherwise reach the operator looking like that.
            foreach (var channel in Enum.GetValues<NotificationChannel>().Where(c => c != NotificationChannel.Test))
            {
                var body = EmailMessageBuilder.Body(new NotificationEvent
                {
                    Channel = channel,
                    Book = new NotificationEventBook { Id = 1, Title = "Frankenstein" },
                });

                Assert.DoesNotContain($"Frankenstein: {channel}.", body, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void Body_ListsEveryAddedPathAndNotJustTheFirst()
        {
            var body = EmailMessageBuilder.Body(new NotificationEvent
            {
                Channel = NotificationChannel.Download,
                Book = new NotificationEventBook { Id = 1, Title = "Frankenstein" },
                AddedPaths = ["/library/one.m4b", "/library/two.m4b", "/library/three.m4b"],
            });

            Assert.Contains("/library/one.m4b", body, StringComparison.Ordinal);
            Assert.Contains("/library/two.m4b", body, StringComparison.Ordinal);
            Assert.Contains("/library/three.m4b", body, StringComparison.Ordinal);
        }

        [Fact]
        public void Body_CarriesEveryFieldTheEventPopulated()
        {
            var body = EmailMessageBuilder.Body(new NotificationEvent
            {
                Channel = NotificationChannel.Download,
                Book = new NotificationEventBook
                {
                    Id = 7,
                    Title = "Frankenstein",
                    Asin = "B002V1A0WE",
                    Authors = ["Mary Shelley"],
                    Narrators = ["Cori Samuel", "Peter Yearsley"],
                    Publisher = "LibriVox",
                    Year = 1818,
                },
                Release = new NotificationEventRelease
                {
                    Title = "Frankenstein.1818.M4B",
                    Indexer = "ExampleIndexer",
                    Quality = "M4B 64kbps",
                    Protocol = "torrent",
                },
                Download = new NotificationEventDownload { ErrorMessage = "disk full" },
                AddedPaths = ["/library/Mary Shelley/Frankenstein/frankenstein.m4b"],
                SourcePath = "/downloads/frankenstein",
                DestinationPath = "/library/Mary Shelley/Frankenstein",
                Message = "Imported by the manual import screen.",
            });

            Assert.Contains("Title: Frankenstein", body, StringComparison.Ordinal);
            Assert.Contains("Author: Mary Shelley", body, StringComparison.Ordinal);
            Assert.Contains("Narrator: Cori Samuel, Peter Yearsley", body, StringComparison.Ordinal);
            Assert.Contains("ASIN: B002V1A0WE", body, StringComparison.Ordinal);
            Assert.Contains("Publisher: LibriVox", body, StringComparison.Ordinal);
            Assert.Contains("Year: 1818", body, StringComparison.Ordinal);
            Assert.Contains("Release: Frankenstein.1818.M4B", body, StringComparison.Ordinal);
            Assert.Contains("Indexer: ExampleIndexer", body, StringComparison.Ordinal);
            Assert.Contains("Quality: M4B 64kbps", body, StringComparison.Ordinal);
            Assert.Contains("Protocol: torrent", body, StringComparison.Ordinal);
            Assert.Contains("Error: disk full", body, StringComparison.Ordinal);
            Assert.Contains("Moved from: /downloads/frankenstein", body, StringComparison.Ordinal);
            Assert.Contains("Moved to: /library/Mary Shelley/Frankenstein", body, StringComparison.Ordinal);
            Assert.Contains("Files added:", body, StringComparison.Ordinal);
            Assert.Contains("/library/Mary Shelley/Frankenstein/frankenstein.m4b", body, StringComparison.Ordinal);
            Assert.Contains("Imported by the manual import screen.", body, StringComparison.Ordinal);
        }

        [Fact]
        public void Body_OmitsTheLabelForEveryFieldTheEventLeftEmpty()
        {
            // The control for the test above. Without this, a builder that printed every label
            // with an empty value would pass every Contains assertion up there.
            var body = EmailMessageBuilder.Body(new NotificationEvent
            {
                Channel = NotificationChannel.Download,
                Book = new NotificationEventBook { Id = 1, Title = "Frankenstein" },
            });

            foreach (var label in new[] { "Author:", "Narrator:", "ASIN:", "Publisher:", "Year:", "Release:", "Indexer:", "Quality:", "Protocol:", "Error:", "Moved from:", "Moved to:", "Files added:" })
            {
                Assert.DoesNotContain(label, body, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void Body_SurvivesAnEventWithNoBookAtAll()
        {
            var body = EmailMessageBuilder.Body(new NotificationEvent { Channel = NotificationChannel.DownloadFailed });

            Assert.Contains("An audiobook", body, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(body));
        }

        [Fact]
        public void Body_DoesNotEndInBlankLines()
        {
            var body = EmailMessageBuilder.Body(new NotificationEvent
            {
                Channel = NotificationChannel.Download,
                Book = new NotificationEventBook { Id = 1, Title = "Frankenstein" },
            });

            Assert.Equal(body.TrimEnd(), body);
        }

        [Fact]
        public void TestBody_SaysPlainlyThatNothingHappened()
        {
            // A test message that reads like a delivery is the one an operator later mistakes for
            // one.
            var body = EmailMessageBuilder.TestBody();

            Assert.Contains("check the settings", body, StringComparison.Ordinal);
            Assert.Contains("Nothing was downloaded, imported or renamed", body, StringComparison.Ordinal);
        }
    }
}
