/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Configuration
{
    /// <summary>
    /// Case for case, this is Readarr's EmailSettingsValidatorFixture
    /// (src/NzbDrone.Core.Test/NotificationTests/EmailTests/EmailSettingsValidatorFixture.cs),
    /// including its two rejected address spellings. It exists so that a later change to either
    /// side shows up as a disagreement rather than as drift nobody notices.
    /// </summary>
    [Trait("Name", "EmailConfigurationValidatorTests")]
    [Trait("Category", "Notifications")]
    public class EmailConfigurationValidatorTests : BaseTests
    {
        private static EmailConfiguration AnEmail() =>
            new()
            {
                Server = "someserver",
                Port = 567,
                From = "listenarr@example.invalid",
                To = ["listenarr@example.invalid"],
            };

        [Fact]
        public void Validate_AcceptsASettingsObjectWithEverythingFilledIn()
        {
            Assert.Empty(EmailConfigurationValidator.Validate(AnEmail()));
        }

        [Fact]
        public void Validate_RejectsAPortOutOfRange()
        {
            var configuration = AnEmail();
            configuration.Port = 900000;

            Assert.NotEmpty(EmailConfigurationValidator.Validate(configuration));
        }

        [Fact]
        public void Validate_RejectsAnEmptyServer()
        {
            var configuration = AnEmail();
            configuration.Server = string.Empty;

            Assert.Contains("Server is required", EmailConfigurationValidator.Validate(configuration));
        }

        [Fact]
        public void Validate_RejectsAnEmptyFromAddress()
        {
            var configuration = AnEmail();
            configuration.From = string.Empty;

            Assert.Contains("From address is required", EmailConfigurationValidator.Validate(configuration));
        }

        [Theory]
        [InlineData("listenarr")]
        [InlineData("listenarr.example")]
        public void Validate_RejectsAMalformedRecipientAddress(string address)
        {
            var configuration = AnEmail();
            configuration.To = [address];

            Assert.NotEmpty(EmailConfigurationValidator.Validate(configuration));
        }

        [Theory]
        [InlineData("listenarr")]
        [InlineData("listenarr.example")]
        public void Validate_RejectsAMalformedCcAddress(string address)
        {
            var configuration = AnEmail();
            configuration.Cc = [address];

            Assert.NotEmpty(EmailConfigurationValidator.Validate(configuration));
        }

        [Theory]
        [InlineData("listenarr")]
        [InlineData("listenarr.example")]
        public void Validate_RejectsAMalformedBccAddress(string address)
        {
            var configuration = AnEmail();
            configuration.Bcc = [address];

            Assert.NotEmpty(EmailConfigurationValidator.Validate(configuration));
        }

        [Fact]
        public void Validate_RejectsATargetWithNoRecipientAtAll()
        {
            var configuration = AnEmail();
            configuration.To = [];
            configuration.Cc = [];
            configuration.Bcc = [];

            Assert.Contains(
                "At least one recipient, CC or BCC address is required",
                EmailConfigurationValidator.Validate(configuration));
        }

        [Fact]
        public void Validate_AcceptsATargetAddressedOnlyByBcc()
        {
            // Readarr's rule is "one of the three", not "To specifically"; a household that only
            // wants Bcc is a configuration it accepts and so is this.
            var configuration = AnEmail();
            configuration.To = [];
            configuration.Bcc = ["household@example.invalid"];

            Assert.Empty(EmailConfigurationValidator.Validate(configuration));
        }

        [Theory]
        [InlineData("listenarr@example.invalid", true)]
        [InlineData("listenarr", false)]
        [InlineData("listenarr.example", false)]
        [InlineData("@example.invalid", false)]
        [InlineData("listenarr@", false)]
        [InlineData("two@at@example.invalid", false)]
        [InlineData("has space@example.invalid", false)]
        [InlineData("", false)]
        public void IsWellFormedAddress_MatchesTheOneAtSignRuleTheFamilyUses(string address, bool expected)
        {
            Assert.Equal(expected, EmailConfigurationValidator.IsWellFormedAddress(address));
        }

        [Fact]
        public void Recipients_GathersEveryAddressAMessageWouldReach()
        {
            var configuration = AnEmail();
            configuration.Cc = ["cc@example.invalid"];
            configuration.Bcc = ["bcc@example.invalid", "   "];

            Assert.Equal(
                ["listenarr@example.invalid", "cc@example.invalid", "bcc@example.invalid"],
                EmailConfigurationValidator.Recipients(configuration));
        }
    }
}
