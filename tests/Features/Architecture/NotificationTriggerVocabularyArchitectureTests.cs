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

namespace Listenarr.Tests.Features.Architecture;

/// <summary>
/// Reads both ends of the webhook trigger contract out of the source that defines them, rather
/// than restating either in a fixture. A fixture that names the vocabulary cannot notice that the
/// two ends disagree, because it is a third opinion that neither end consults.
/// <para>
/// Two independent faults made a webhook configured in the settings UI inert. The settings form
/// wrote trigger names no dispatcher filtered on, and the call sites that emit the book lifecycle
/// events addressed only the legacy single webhook URL, never the webhook list the form writes to.
/// Fixing either alone leaves the feature broken, so each has its own test here.
/// </para>
/// </summary>
[Trait("Name", "NotificationTriggerVocabularyArchitectureTests")]
[Trait("Category", "Architecture")]
public sealed class NotificationTriggerVocabularyArchitectureTests : BaseTests
{
    private static readonly string RepositoryRoot = TestUtils.FindRepositoryRoot();

    private const string SettingsFormSource = "fe/src/views/settings/NotificationsTab.vue";

    private static readonly string[] BackendProjects =
    [
        "listenarr.domain",
        "listenarr.application",
        "listenarr.infrastructure",
        "listenarr.api"
    ];

    /// <summary>
    /// The call sites that emit the book lifecycle events. Each of these named a single
    /// destination itself, and the destination it named was the legacy single webhook URL.
    /// </summary>
    private static readonly string[] BookLifecycleEmitters =
    [
        "listenarr.application/Audiobooks/Catalog/LibraryAddService.PostCommit.cs",
        "listenarr.api/Features/Library/LibraryAddWorkflow.PostCommit.cs",
        "listenarr.application/Downloads/Submission/DownloadService.cs",
        "listenarr.infrastructure/Library/Scanning/ScanJobProcessor.Notifications.cs",
        "listenarr.api/Features/Library/LibraryManualScanWorkflow.cs"
    ];

    /// <summary>
    /// Reach. A trigger the settings form can subscribe to is worthless if the call site that
    /// emits it only ever posts to <c>settings.WebhookUrl</c>.
    /// </summary>
    [Fact]
    public void BookLifecycleEmitters_DoNotAddressTheLegacyWebhookUrlDirectly()
    {
        var addressingTheLegacyUrl = BookLifecycleEmitters
            .Where(relativePath => File.ReadAllText(Path.Join(RepositoryRoot, relativePath))
                .Contains("WebhookUrl", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            addressingTheLegacyUrl.Count == 0,
            "These call sites name the legacy single webhook URL as their destination, so the "
            + "triggers they emit never reach the webhook list the settings form writes to. They "
            + "must dispatch to every subscribed channel instead:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, addressingTheLegacyUrl));
    }

    /// <summary>
    /// Vocabulary. Every name the settings form offers must be one that something emits.
    /// </summary>
    [Fact]
    public void SettingsForm_OffersOnlyTriggersSomeCallSiteEmits()
    {
        var offered = ReadTriggersOfferedByTheSettingsForm();
        var emitted = ReadTriggersTheBackendEmits();

        Assert.NotEmpty(offered);
        Assert.NotEmpty(emitted);

        var offeredButNeverEmitted = offered
            .Except(emitted, StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offeredButNeverEmitted.Count == 0,
            $"{SettingsFormSource} offers trigger names that no backend call site emits, so "
            + "ticking them can never produce a notification: "
            + $"[{string.Join(", ", offeredButNeverEmitted)}]"
            + Environment.NewLine
            + $"Emitted by the backend: [{string.Join(", ", emitted.OrderBy(name => name, StringComparer.Ordinal))}]");
    }

    [Fact]
    public void SettingsForm_UsesNoLegacyKebabCaseTriggerName()
    {
        var source = File.ReadAllText(Path.Join(RepositoryRoot, SettingsFormSource));
        var legacyNames = Regex
            .Matches(source, "book-(?:added|downloading|available|completed)")
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            legacyNames.Count == 0,
            $"{SettingsFormSource} still writes the pre-canonical trigger vocabulary onto a "
            + $"webhook's trigger list: [{string.Join(", ", legacyNames)}]");
    }

    private static HashSet<string> ReadTriggersOfferedByTheSettingsForm()
    {
        var source = File.ReadAllText(Path.Join(RepositoryRoot, SettingsFormSource));

        // The form renders one checkbox per entry of the array it iterates. Accept the named
        // constant or an array literal written inline on the v-for, so this reads whichever shape
        // the component happens to be in.
        var declaration = Regex.Match(
            source,
            @"const\s+perWebhookTriggerList\s*=\s*\[(?<items>[^\]]*)\]");
        if (!declaration.Success)
        {
            declaration = Regex.Match(source, @"v-for=""t in \[(?<items>[^\]]*)\]""");
        }

        Assert.True(
            declaration.Success,
            $"Could not find the trigger list the webhook form iterates in {SettingsFormSource}.");

        return ExtractQuotedStrings(declaration.Groups["items"].Value);
    }

    /// <summary>
    /// Every trigger name passed as the first argument of a notification dispatch anywhere in the
    /// backend, whether written as a literal or as a reference to the canonical vocabulary.
    /// </summary>
    private static HashSet<string> ReadTriggersTheBackendEmits()
    {
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var callPattern = new Regex(
            @"(?:DispatchAsync|SendNotificationAsync)\(\s*(?<trigger>""[^""]*""|[A-Za-z_][\w.]*)\s*,",
            RegexOptions.Singleline);

        foreach (var file in BackendProjects
            .Select(project => Path.Join(RepositoryRoot, project))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(file => !IsBuildArtifact(file)))
        {
            foreach (var match in callPattern.Matches(File.ReadAllText(file)).Cast<System.Text.RegularExpressions.Match>())
            {
                var argument = match.Groups["trigger"].Value.Trim();
                var quoted = ExtractQuotedStrings(argument);
                if (quoted.Count > 0)
                {
                    emitted.UnionWith(quoted);
                    continue;
                }

                // A reference to the canonical vocabulary. The member name is the trigger name;
                // the vocabulary tests pin the constants to their values. Anything else is a
                // caller-supplied variable and names no fixed trigger.
                const string vocabulary = nameof(NotificationTriggers) + ".";
                if (argument.StartsWith(vocabulary, StringComparison.Ordinal))
                {
                    emitted.Add(argument[vocabulary.Length..]);
                }
            }
        }

        return emitted;
    }

    private static bool IsBuildArtifact(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static HashSet<string> ExtractQuotedStrings(string fragment) =>
        Regex.Matches(fragment, @"['""](?<value>[^'""]*)['""]")
            .Select(match => match.Groups["value"].Value)
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
}
