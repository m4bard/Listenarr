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
using System.Globalization;
using System.Reflection;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Search.Scoring
{
    /// <summary>
    /// The bitrate/codec quality ladder is hand-copied into four files. Three of the copies drive
    /// live behaviour (accept/reject scoring, the "Smart" sort, the "Quality" column sort) and the
    /// fourth has no caller. Nothing makes them agree, so this pins them to each other.
    ///
    /// Every copy is a private method, so every copy has to be reached by reflection.
    /// </summary>
    [Trait("Name", "QualityScoreLadderParityTests")]
    [Trait("Category", "Scoring")]
    public sealed class QualityScoreLadderParityTests : BaseTests
    {
        /// <summary>
        /// Quality strings covering every rung of the ladder, plus the casing and separator
        /// variants the VBR-preset and bitrate helpers are sensitive to.
        /// </summary>
        private static readonly string?[] QualityTokens =
        [
            null,
            "",
            "   ",
            "Unknown",
            "FLAC",
            "flac",
            "FLAC 24bit",
            "AAX",
            "aax",
            "AAX 128",
            "M4B",
            "m4b",
            "M4B 64kbps",
            "OPUS",
            "Opus VBR",
            "MP3 V0",
            "MP3-V0",
            "MP3 V1",
            "MP3-V1",
            "MP3 V2",
            "MP3-V2",
            "V0",
            "AAC",
            "AAC 256",
            "M4A",
            "m4a 192",
            "MP3 320kbps",
            "MP3 320",
            "MP3 256kbps",
            "MP3 192kbps",
            "MP3 VBR",
            "MP3 CBR",
            "MP3",
            "MP3 128kbps",
            "MP3 64kbps",
            "64",
            "128",
            "192",
            "256",
            "320",
            // Inputs carrying an uppercase I, where ToLower and ToLowerInvariant part company
            // under a Turkish or Azeri culture.
            "FLAC HI-RES",
            "AUDIBLE AAX",
            "MP3 VBR (ISO)",
            "OPUS DIGITAL",
            "AUDIOBOOK M4B",
        ];

        private static readonly string[] Cultures = ["en-US", "de-DE", "tr-TR", "az-Latn-AZ"];

        private sealed record Ladder(string Name, Func<string?, int> Score);

        private Ladder[] BuildLadders()
        {
            var sortingService = new SearchResultSortingService(
                _indexerRepository,
                NullLogger<SearchResultSortingService>.Instance);
            var resultScorer = new SearchResultScorer(_indexerRepository, NullLogger.Instance);
            var profileService = new QualityProfileService(
                _qualityProfileRepository,
                NullLogger<QualityProfileService>.Instance);

            return
            [
                new Ladder(
                    "SearchResultScorer (accept/reject scoring)",
                    Bind(typeof(SearchResultScorer), resultScorer)),
                new Ladder(
                    "CompositeScorer (Smart sort)",
                    Bind(typeof(CompositeScorer), instance: null)),
                new Ladder(
                    "SearchResultSortingService (Quality column sort)",
                    Bind(typeof(SearchResultSortingService), sortingService)),
                new Ladder(
                    "QualityProfileService (no production caller)",
                    Bind(typeof(QualityProfileService), profileService)),
            ];
        }

        private static Func<string?, int> Bind(Type owner, object? instance)
        {
            // Two of the four copies are private static and two are private instance.
            var method = owner.GetMethod(
                "GetQualityScore",
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            Assert.True(method != null, $"{owner.Name} has no private GetQualityScore method");
            if (method!.IsStatic) instance = null;

            // SearchResultScorer declares the parameter as non-nullable string and the other three
            // as string?, but nullable annotations are not enforced at run time, so a null argument
            // reaches every copy's own IsNullOrEmpty guard.
            return quality => (int)method!.Invoke(instance, [quality])!;
        }

        [Fact]
        public void EveryQualityLadderCopy_AgreesOnEveryToken()
        {
            var ladders = BuildLadders();
            var disagreements = new List<string>();

            foreach (var token in QualityTokens)
            {
                var scores = ladders.Select(ladder => (ladder.Name, Value: ladder.Score(token))).ToArray();
                var distinct = scores.Select(s => s.Value).Distinct().Count();
                if (distinct > 1)
                {
                    disagreements.Add(
                        $"\"{token ?? "<null>"}\": "
                        + string.Join(", ", scores.Select(s => $"{s.Name} = {s.Value}")));
                }
            }

            Assert.True(
                disagreements.Count == 0,
                "The four hand-copied quality ladders disagree:\n" + string.Join("\n", disagreements));
        }

        [Fact]
        public void EveryQualityLadderCopy_AgreesUnderEveryCulture()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var disagreements = new List<string>();

            try
            {
                foreach (var cultureName in Cultures)
                {
                    CultureInfo.CurrentCulture = new CultureInfo(cultureName);

                    // Control: without this the culture loop would prove nothing, because
                    // ToLower and ToLowerInvariant only part company on the dotted/dotless I.
                    if (cultureName is "tr-TR" or "az-Latn-AZ")
                    {
                        Assert.NotEqual("I".ToLowerInvariant(), "I".ToLower());
                    }

                    var ladders = BuildLadders();

                    foreach (var token in QualityTokens)
                    {
                        var scores = ladders.Select(l => (l.Name, Value: l.Score(token))).ToArray();
                        if (scores.Select(s => s.Value).Distinct().Count() > 1)
                        {
                            disagreements.Add(
                                $"[{cultureName}] \"{token ?? "<null>"}\": "
                                + string.Join(", ", scores.Select(s => $"{s.Name} = {s.Value}")));
                        }
                    }
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }

            Assert.True(
                disagreements.Count == 0,
                "The quality ladders disagree once the server's culture changes. Three copies lower "
                + "the input with ToLower (culture-sensitive) and one with ToLowerInvariant:\n"
                + string.Join("\n", disagreements));
        }

        [Fact]
        public void QualityProfileService_QualityLadder_HasNoCaller()
        {
            // The only direct unit test of the ladder, QualityScoringTests, reaches this copy by
            // reflection. The copy is private and QualityProfileService is not partial, so any
            // caller would have to appear in this one file. This is a source-text check rather
            // than IL analysis, which is enough for that question.
            var source = Path.Join(
                RepositoryRootPath(),
                "listenarr.application",
                "Audiobooks",
                "Quality",
                "QualityProfileService.cs");
            Assert.True(File.Exists(source), $"Expected to find {source}");

            var callSites = File.ReadAllLines(source)
                .Select((text, index) => (Line: index + 1, Text: text.Trim()))
                .Where(entry => entry.Text.Contains("GetQualityScore(", StringComparison.Ordinal))
                .Where(entry => !entry.Text.StartsWith("private int GetQualityScore(", StringComparison.Ordinal))
                .ToArray();

            Assert.True(
                callSites.Length == 0,
                "QualityProfileService.GetQualityScore has gained a caller, so it is no longer the "
                + "untested-by-proxy copy this test was written to record:\n"
                + string.Join("\n", callSites.Select(entry => $"  :{entry.Line} {entry.Text}")));
        }

        private static string RepositoryRootPath()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Join(directory.FullName, "listenarr.slnx")))
            {
                directory = directory.Parent;
            }

            Assert.True(directory != null, "Could not locate the repository root from the test output directory");
            return directory!.FullName;
        }
    }
}
