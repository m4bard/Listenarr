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
using System.Text.RegularExpressions;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Notifications;

/// <summary>
/// Pins the two notification trigger vocabularies against the code that reads and writes
/// them. The defect these guard was a settings UI offering names no dispatch site gates on,
/// which no unit test could see because each side was internally consistent.
/// </summary>
[Trait("Name", "WebhookTriggerVocabularyTests")]
[Trait("Category", "Notifications")]
public sealed class WebhookTriggerVocabularyTests : BaseTests
{
    private static readonly string RepositoryRoot = TestUtils.FindRepositoryRoot();

    private static string ReadRepositoryFile(params string[] segments) =>
        File.ReadAllText(Path.Join([RepositoryRoot, .. segments]));

    [Fact]
    public void DispatchGates_ReadExactlyTheDispatchGatedTriggerNames()
    {
        var dispatchSources = new[]
        {
            ReadRepositoryFile("listenarr.infrastructure", "Notifications", "Delivery", "NotificationService.cs"),
            ReadRepositoryFile("listenarr.infrastructure", "Library", "Moving", "MoveJobProcessor.Completion.cs")
        };

        var gatePattern = new Regex(@"Triggers\.Contains\(""(?<trigger>[^""]+)""\)", RegexOptions.Compiled);

        var gated = dispatchSources
            .SelectMany(source => gatePattern.Matches(source).Select(match => match.Groups["trigger"].Value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(trigger => trigger, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(gated);
        Assert.Equal(
            WebhookTriggerVocabulary.DispatchGated.OrderBy(trigger => trigger, StringComparer.Ordinal).ToList(),
            gated);
    }

    [Fact]
    public void SettingsUi_OffersExactlyTheTriggerNamesThatAreActuallyRaised()
    {
        var component = ReadRepositoryFile("fe", "src", "views", "settings", "NotificationsTab.vue");

        var declaration = Regex.Match(
            component,
            @"const offeredTriggers\s*=\s*\[(?<body>[^\]]*)\]",
            RegexOptions.Compiled);

        Assert.True(
            declaration.Success,
            "NotificationsTab.vue must declare the trigger options it offers as 'offeredTriggers' so this guard can compare them against dispatch.");

        var offered = Regex.Matches(declaration.Groups["body"].Value, @"'(?<trigger>[^']+)'")
            .Select(match => match.Groups["trigger"].Value)
            .ToList();

        Assert.Equal(WebhookTriggerVocabulary.Offered.ToList(), offered);
    }

    [Fact]
    public void SettingsUi_DoesNotOfferTriggerOptionsFromAnInlineLiteral()
    {
        var component = ReadRepositoryFile("fe", "src", "views", "settings", "NotificationsTab.vue");

        // The original defect shipped because the checkbox grid iterated an inline array
        // that nothing compared against dispatch. Requiring the shared constant keeps the
        // guard above meaningful.
        Assert.Contains("v-for=\"t in offeredTriggers\"", component, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyGlobalTriggers_AllHaveAProductionEmitter()
    {
        var emitterSources = new[]
        {
            ReadRepositoryFile("listenarr.application", "Downloads", "Submission", "DownloadService.cs"),
            ReadRepositoryFile("listenarr.application", "Audiobooks", "Catalog", "LibraryAddService.PostCommit.cs"),
            ReadRepositoryFile("listenarr.infrastructure", "Library", "Scanning", "ScanJobProcessor.Notifications.cs"),
            ReadRepositoryFile("listenarr.api", "Features", "Library", "LibraryManualScanWorkflow.cs"),
            ReadRepositoryFile("listenarr.api", "Features", "Library", "LibraryAddWorkflow.PostCommit.cs")
        };

        var unemitted = WebhookTriggerVocabulary.LegacyGlobal
            .Where(trigger => !emitterSources.Any(source => source.Contains($"\"{trigger}\"", StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(unemitted);
    }

    [Fact]
    public void SettingsDefault_MatchesTheLegacyTriggersThatAreActuallyRaised()
    {
        var settings = new ApplicationSettings();

        Assert.Equal(WebhookTriggerVocabulary.LegacyGlobal.ToList(), settings.EnabledNotificationTriggers);

        // book-completed shipped in the default and in the settings UI, but no production
        // call site raises it on either path, so subscribing to it promised a notification
        // that could never arrive.
        Assert.DoesNotContain(
            WebhookTriggerVocabulary.LegacyBookCompleted,
            settings.EnabledNotificationTriggers,
            StringComparer.Ordinal);
    }

    [Fact]
    public void DispatchGatedAndLegacyVocabularies_ShareNoNames()
    {
        Assert.Empty(WebhookTriggerVocabulary.DispatchGated
            .Intersect(WebhookTriggerVocabulary.LegacyGlobal, StringComparer.Ordinal));
    }

    [Fact]
    public void OfferedTriggers_AreAllDispatchGated()
    {
        Assert.All(WebhookTriggerVocabulary.Offered,
            trigger => Assert.True(WebhookTriggerVocabulary.IsDispatchGated(trigger)));
    }

    [Fact]
    public void GatedTriggersWithoutAnEmitter_AreNotOffered()
    {
        // SendSystemNotificationAsync is declared and implemented but nothing in production
        // calls it, so "System" is gated and unreachable. Offering it would repeat the
        // book-completed mistake. If a caller is added, move the name into Offered.
        Assert.All(WebhookTriggerVocabulary.GatedWithoutEmitter,
            trigger => Assert.False(WebhookTriggerVocabulary.IsOffered(trigger)));

        Assert.Equal(
            WebhookTriggerVocabulary.DispatchGated.OrderBy(t => t, StringComparer.Ordinal).ToList(),
            WebhookTriggerVocabulary.Offered
                .Concat(WebhookTriggerVocabulary.GatedWithoutEmitter)
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToList());
    }

    [Fact]
    public void SystemTrigger_StillHasNoProductionCaller()
    {
        // A tripwire, not an endorsement. When this fails, SendSystemNotificationAsync has
        // gained a caller and "System" should be offered in the settings UI.
        var productionRoots = new[] { "listenarr.domain", "listenarr.application", "listenarr.infrastructure", "listenarr.api" };

        var callers = productionRoots
            .SelectMany(root => Directory.EnumerateFiles(Path.Join(RepositoryRoot, root), "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file =>
            {
                var text = File.ReadAllText(file);
                // The declaration and the implementation are not callers.
                return text.Contains(".SendSystemNotificationAsync(", StringComparison.Ordinal);
            })
            .ToList();

        Assert.Empty(callers);
    }

    [Fact]
    public void LegacyEmitters_PostOnlyToTheSingleWebhookUrl_NotToTheWebhookList()
    {
        // The reach half of the defect. Every site that raises a legacy name passes
        // settings.WebhookUrl; none of them iterate settings.Webhooks. That is why this
        // change moves the settings UI onto the dispatch-gated names rather than trying to
        // make the legacy names deliverable to a webhook row.
        var emitters = new[]
        {
            Path.Join("listenarr.application", "Downloads", "Submission", "DownloadService.cs"),
            Path.Join("listenarr.application", "Audiobooks", "Catalog", "LibraryAddService.PostCommit.cs"),
            Path.Join("listenarr.infrastructure", "Library", "Scanning", "ScanJobProcessor.Notifications.cs"),
            Path.Join("listenarr.api", "Features", "Library", "LibraryManualScanWorkflow.cs"),
            Path.Join("listenarr.api", "Features", "Library", "LibraryAddWorkflow.PostCommit.cs")
        };

        foreach (var emitter in emitters)
        {
            var source = File.ReadAllText(Path.Join(RepositoryRoot, emitter));

            Assert.Contains("WebhookUrl", source, StringComparison.Ordinal);
            Assert.DoesNotContain("GetWebhookConfigurationsAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("settings.Webhooks", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WebhookRowDispatchSites_IterateTheWebhookList()
    {
        // The mirror of the assertion above: the names the settings UI now offers are raised
        // by sites that do read settings.Webhooks, so a row the operator saves is reachable.
        var rowDispatchSites = new[]
        {
            Path.Join("listenarr.infrastructure", "Notifications", "Delivery", "NotificationService.cs"),
            Path.Join("listenarr.infrastructure", "Library", "Moving", "MoveJobProcessor.Completion.cs")
        };

        foreach (var site in rowDispatchSites)
        {
            var source = File.ReadAllText(Path.Join(RepositoryRoot, site));
            Assert.Contains("GetWebhookConfigurationsAsync", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Canonicalize_RemapsRetiredNamesThatHaveACounterpart()
    {
        var result = WebhookTriggerVocabulary.Canonicalize(
            [WebhookTriggerVocabulary.LegacyBookAvailable]);

        Assert.True(result.Changed);
        Assert.Equal([WebhookTriggerVocabulary.ImportedTrigger], result.Triggers);
        Assert.Equal(
            [new KeyValuePair<string, string>(
                WebhookTriggerVocabulary.LegacyBookAvailable,
                WebhookTriggerVocabulary.ImportedTrigger)],
            result.Remapped);
        Assert.Empty(result.Dropped);
    }

    [Fact]
    public void Canonicalize_DropsRetiredNamesWithNoCounterpart()
    {
        var result = WebhookTriggerVocabulary.Canonicalize(
            [WebhookTriggerVocabulary.LegacyBookAdded, WebhookTriggerVocabulary.LegacyBookDownloading]);

        Assert.True(result.Changed);
        Assert.Empty(result.Triggers);
        Assert.Empty(result.Remapped);
        Assert.Equal(
            [WebhookTriggerVocabulary.LegacyBookAdded, WebhookTriggerVocabulary.LegacyBookDownloading],
            result.Dropped);
    }

    [Fact]
    public void Canonicalize_RewritesAWholeStoredRowFromTheOldSettingsUi()
    {
        // Exactly what a webhook saved by the previous settings UI holds when every box
        // was ticked. Every one of these names is inert against a webhook row.
        var stored = new List<string>
        {
            WebhookTriggerVocabulary.LegacyBookAdded,
            WebhookTriggerVocabulary.LegacyBookDownloading,
            WebhookTriggerVocabulary.LegacyBookAvailable,
            WebhookTriggerVocabulary.LegacyBookCompleted
        };

        Assert.DoesNotContain(stored, WebhookTriggerVocabulary.IsDispatchGated);

        var result = WebhookTriggerVocabulary.Canonicalize(stored);

        Assert.Equal([WebhookTriggerVocabulary.ImportedTrigger], result.Triggers);
        Assert.All(result.Triggers, trigger => Assert.True(WebhookTriggerVocabulary.IsOffered(trigger)));
    }

    [Fact]
    public void Canonicalize_LeavesAlreadyGatedNamesAlone()
    {
        var stored = WebhookTriggerVocabulary.DispatchGated.ToList();

        var result = WebhookTriggerVocabulary.Canonicalize(stored);

        Assert.False(result.Changed);
        Assert.Equal(stored, result.Triggers);
    }

    [Fact]
    public void Canonicalize_PreservesNamesItDoesNotRecognise()
    {
        var result = WebhookTriggerVocabulary.Canonicalize(["SomethingElse"]);

        Assert.False(result.Changed);
        Assert.Equal(["SomethingElse"], result.Triggers);
        Assert.Empty(result.Dropped);
    }

    [Fact]
    public void Canonicalize_DeduplicatesAndTrimsAndSkipsBlanks()
    {
        var result = WebhookTriggerVocabulary.Canonicalize(
        [
            WebhookTriggerVocabulary.LegacyBookAvailable,
            WebhookTriggerVocabulary.LegacyBookCompleted,
            $"  {WebhookTriggerVocabulary.FailedTrigger}  ",
            "   ",
            WebhookTriggerVocabulary.FailedTrigger
        ]);

        Assert.Equal(
            [WebhookTriggerVocabulary.ImportedTrigger, WebhookTriggerVocabulary.FailedTrigger],
            result.Triggers);
    }

    [Fact]
    public void Canonicalize_HandlesNullAndEmptyInput()
    {
        Assert.Empty(WebhookTriggerVocabulary.Canonicalize(null).Triggers);
        Assert.False(WebhookTriggerVocabulary.Canonicalize([]).Changed);
    }
}
