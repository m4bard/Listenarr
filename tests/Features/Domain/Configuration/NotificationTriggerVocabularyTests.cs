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

namespace Listenarr.Tests.Features.Domain.Configuration;

[Trait("Name", "NotificationTriggerVocabularyTests")]
[Trait("Category", "NotificationTriggers")]
public sealed class NotificationTriggerVocabularyTests : BaseTests
{
    [Fact]
    public void EmittedAndUnemittedNames_TogetherAccountForTheWholeVocabulary()
    {
        var accountedFor = NotificationTriggers.Emitted
            .Concat(NotificationTriggers.DeclaredWithoutEmitter)
            .Append(NotificationTriggers.Test)
            .OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(
            NotificationTriggers.All.OrderBy(name => name, StringComparer.Ordinal),
            accountedFor);
        Assert.Empty(NotificationTriggers.Emitted
            .Intersect(NotificationTriggers.DeclaredWithoutEmitter, StringComparer.Ordinal));
    }

    [Fact]
    public void CanonicalNames_CarryNoLegacySpelling()
    {
        Assert.All(NotificationTriggers.All, trigger =>
        {
            Assert.DoesNotContain('-', trigger);
            Assert.False(
                NotificationTriggers.LegacyNames.ContainsKey(trigger),
                $"'{trigger}' is both a canonical name and a legacy name.");
        });
    }

    [Fact]
    public void EveryLegacyName_RenamesOntoACanonicalName()
    {
        Assert.All(NotificationTriggers.LegacyNames, pair => Assert.True(
            NotificationTriggers.IsCanonical(pair.Value),
            $"Legacy name '{pair.Key}' renames onto '{pair.Value}', which is not canonical."));
    }

    [Fact]
    public void ApplicationSettingsDefault_UsesTheCanonicalVocabulary()
    {
        var settings = new ApplicationSettings();

        Assert.Equal(
            new List<string>
            {
                NotificationTriggers.Added,
                NotificationTriggers.Downloading,
                NotificationTriggers.Available,
                NotificationTriggers.Completed
            },
            settings.EnabledNotificationTriggers);
        Assert.All(settings.EnabledNotificationTriggers, trigger =>
            Assert.True(NotificationTriggers.IsCanonical(trigger)));
    }

    [Theory]
    [InlineData("book-added", "Added")]
    [InlineData("book-downloading", "Downloading")]
    [InlineData("book-available", "Available")]
    [InlineData("book-completed", "Completed")]
    public void Normalize_RenamesALegacyName(string legacy, string canonical)
    {
        Assert.Equal(canonical, NotificationTriggers.Normalize(legacy));
    }

    [Theory]
    [InlineData("Added")]
    [InlineData("Downloading")]
    [InlineData("Available")]
    [InlineData("Completed")]
    [InlineData("Imported")]
    [InlineData("Failed")]
    [InlineData("Moved")]
    [InlineData("System")]
    public void Normalize_LeavesACanonicalNameAlone(string canonical)
    {
        Assert.Equal(canonical, NotificationTriggers.Normalize(canonical));
    }

    [Theory]
    [InlineData("something-a-user-typed")]
    [InlineData("BOOK-ADDED")]
    [InlineData("Book-Added")]
    [InlineData("book_added")]
    [InlineData("")]
    public void Normalize_LeavesAnUnrecognisedNameAlone(string unknown)
    {
        Assert.Equal(unknown, NotificationTriggers.Normalize(unknown));
    }

    [Fact]
    public void NormalizeAll_PreservesOrderAndDropsDuplicates()
    {
        var normalized = NotificationTriggers.NormalizeAll(
            ["book-added", "Added", "book-downloading", "not-a-trigger"]);

        Assert.Equal(
            new List<string>
            {
                NotificationTriggers.Added,
                NotificationTriggers.Downloading,
                "not-a-trigger"
            },
            normalized);
    }

    [Fact]
    public void NormalizeAll_IsIdempotent()
    {
        var once = NotificationTriggers.NormalizeAll(
            ["book-added", "book-downloading", "book-available", "book-completed", "kept-verbatim"]);
        var twice = NotificationTriggers.NormalizeAll(once);

        Assert.Equal(once, twice);
        Assert.Contains("kept-verbatim", twice);
    }

    /// <summary>
    /// Recorded rather than fixed. The settings UI has always offered "Processing Complete" and
    /// the default settings list has always contained it, but no production call site emits it.
    /// <c>System</c> is emitted only by <c>SendSystemNotificationAsync</c>, which has no caller
    /// anywhere. Giving either an emitter is a product decision; this test is the place that says
    /// they have none.
    /// </summary>
    [Fact]
    public void CompletedAndSystem_AreDeclaredButEmittedByNoCallSite()
    {
        Assert.Equal(
            new List<string> { NotificationTriggers.Completed, NotificationTriggers.System },
            NotificationTriggers.DeclaredWithoutEmitter);
        Assert.All(NotificationTriggers.DeclaredWithoutEmitter, trigger =>
        {
            Assert.True(NotificationTriggers.IsCanonical(trigger));
            Assert.DoesNotContain(trigger, NotificationTriggers.Emitted);
        });
    }
}
