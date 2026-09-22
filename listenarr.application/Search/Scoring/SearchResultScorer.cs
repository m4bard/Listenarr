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
using Listenarr.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Search.Scoring
{
    public partial class SearchResultScorer
    {
        private readonly IIndexerRepository? _indexerRepository;
        private readonly ILogger _logger;

        // Configurable weights (tune as needed)
        public int BaseScore { get; set; } = 100;
        public int FormatMatchBonus { get; set; } = 5;
        public int FormatMissingPenalty { get; set; } = -8;
        public int QualityMissingPenalty { get; set; } = -10;
        public int LanguageMissingPenalty { get; set; } = -10;
        public int LanguageMismatchPenalty { get; set; } = -15;
        public int QualityNotAllowedPenalty { get; set; } = -20;
        // Mirrors FormatMatchBonus / QualityNotAllowedPenalty on purpose. Against a base of
        // 100 the penalty cannot on its own reach the "computed score <= 0" reject below, so
        // a preference stays a preference instead of quietly becoming a filter.
        public int ReleaseShapeMatchBonus { get; set; } = 5;
        public int ReleaseShapeMismatchPenalty { get; set; } = -20;
        public int ForbiddenWordRejectionFlag { get; set; } = -1; // sentinel for rejection

        private readonly IReadOnlyDictionary<int, Indexer>? _resolvedIndexers;

        public SearchResultScorer(IIndexerRepository? indexerRepository, ILogger logger)
            : this(indexerRepository, logger, resolvedIndexers: null)
        {
        }

        // resolvedIndexers lets a caller scoring a whole batch resolve each indexer once up front
        // and pass the results in. The repository is scoped, and so is the DbContext behind it, so
        // results scored in parallel must not each run their own lookup.
        public SearchResultScorer(
            IIndexerRepository? indexerRepository,
            ILogger logger,
            IReadOnlyDictionary<int, Indexer>? resolvedIndexers)
        {
            _indexerRepository = indexerRepository;
            _logger = logger;
            _resolvedIndexers = resolvedIndexers;
        }

        /// <summary>
        /// Score one candidate release against a quality profile.
        /// </summary>
        /// <param name="searchResult">The candidate release.</param>
        /// <param name="profile">The quality profile the audiobook is monitored under.</param>
        /// <param name="targetIsBundle">
        /// Whether the audiobook record being searched for is itself a bundle, which the
        /// caller knows and the release does not carry. The right release for a six-book
        /// omnibus record is a six-book omnibus, so when this is set the profile's preference
        /// is overridden for that book rather than penalising every candidate it can match.
        /// </param>
        public async Task<QualityScore> Score(SearchResult searchResult, QualityProfile profile, bool targetIsBundle = false)
        {
            // Mirror existing QualityProfileService semantics, but organized and configurable
            var score = new QualityScore
            {
                SearchResult = searchResult,
                TotalScore = BaseScore,
                ScoreBreakdown = new Dictionary<string, int>(),
                RejectionReasons = new List<string>()
            };

            // Helper normalizers
            static string? NormalizeToken(string? s)
            {
                if (string.IsNullOrWhiteSpace(s)) return null;
                var t = s.Trim();
                if (string.Equals(t, "unknown", StringComparison.OrdinalIgnoreCase)) return null;
                return t;
            }

            string? normalizedLanguage = NormalizeToken(searchResult.Language);
            string? normalizedFormat = NormalizeToken(searchResult.Format);
            string? normalizedQuality = NormalizeToken(searchResult.Quality);

            // Instant rejects: forbidden words
            var forbidden = profile.MustNotContain.FirstOrDefault(word => TitleTermMatcher.TitleContainsTerm(searchResult.Title, word));
            if (forbidden != null)
            {
                score.RejectionReasons.Add($"Contains forbidden word: '{forbidden}'");
                score.TotalScore = -1;
                return score;
            }

            // Required words: the title has to match at least one of them, not all of them
            var requiredWords = profile.MustContain.Where(required => !string.IsNullOrWhiteSpace(required)).ToList();
            if (requiredWords.Count > 0 && !requiredWords.Any(required => TitleTermMatcher.TitleContainsTerm(searchResult.Title, required)))
            {
                var wordList = string.Join("', '", requiredWords.Select(required => required.Trim()));
                score.RejectionReasons.Add($"Missing required word: title matches none of '{wordList}'");
                score.TotalScore = -1;
                return score;
            }

            // Every gate that can reject a release outright lives in SearchResultScorer.Gates.cs:
            // the indexer lookup all three size and age gates depend on, the profile's size
            // bounds, the seeders minimum and the age and retention ceilings. It settles isNzb
            // once, before anything reads it, and hands back the age the penalties below need.
            var gates = await ApplyRejectionGates(searchResult, profile, score);
            if (gates.Rejected)
            {
                return score;
            }

            var isNzb = gates.IsNzb;
            var ageDays = gates.AgeDays;

            // Title lower for detection
            var titleLower = (searchResult.Title ?? string.Empty).ToLower();

            // Language detection for NZB
            if (isNzb && string.IsNullOrEmpty(normalizedLanguage) && HasPreferredLanguages(profile))
            {
                var detected = DetectLanguageFromTitle(titleLower, profile.PreferredLanguages);
                if (!string.IsNullOrEmpty(detected))
                {
                    normalizedLanguage = detected;
                    _logger.LogDebug("Detected language from title: {Language}", detected);
                }
            }

            // Language scoring
            if (HasPreferredLanguages(profile))
            {
                if (isNzb && string.IsNullOrEmpty(normalizedLanguage))
                {
                    _logger.LogDebug("NZB/Usenet missing language: no penalty applied for title '{Title}'", searchResult.Title);
                }
                else if (string.IsNullOrEmpty(normalizedLanguage))
                {
                    score.TotalScore += LanguageMissingPenalty;
                    score.ScoreBreakdown["Language"] = LanguageMissingPenalty;
                }
                else
                {
                    var matches = profile.PreferredLanguages.Any(l => normalizedLanguage.Equals(l, StringComparison.OrdinalIgnoreCase));
                    if (!matches)
                    {
                        score.TotalScore += LanguageMismatchPenalty;
                        score.ScoreBreakdown["LanguageMismatch"] = LanguageMismatchPenalty;
                    }
                }
            }

            // Format detection for NZB
            if (isNzb && string.IsNullOrEmpty(normalizedFormat) && HasPreferredFormats(profile))
            {
                var detected = DetectFormatFromTitle(titleLower, profile.PreferredFormats);
                if (!string.IsNullOrEmpty(detected))
                {
                    normalizedFormat = detected;
                    score.TotalScore += FormatMatchBonus;
                    score.ScoreBreakdown["FormatMatchedInTitle"] = FormatMatchBonus;
                }
            }

            // Format scoring
            if (HasPreferredFormats(profile))
            {
                if (isNzb && string.IsNullOrEmpty(normalizedFormat))
                {
                    _logger.LogDebug("NZB/Usenet missing format: no penalty applied for title '{Title}'", searchResult.Title);
                }
                else if (string.IsNullOrEmpty(normalizedFormat))
                {
                    score.TotalScore += FormatMissingPenalty;
                    score.ScoreBreakdown["Format"] = FormatMissingPenalty;
                }
                else
                {
                    var fmtLower = normalizedFormat.ToLower();
                    var qualityLower = (normalizedQuality ?? string.Empty).ToLower();
                    var urlLower = (searchResult.TorrentUrl ?? searchResult.Source ?? string.Empty).ToLower();

                    if (profile.PreferredFormats!
                        .Where(format => !string.IsNullOrWhiteSpace(format))
                        .Select(format => format.ToLower().Trim())
                        .Any(token => fmtLower.Contains(token) || qualityLower.Contains(token) || urlLower.Contains("." + token) || urlLower.Contains(token) || titleLower.Contains(token)))
                    {
                        score.ScoreBreakdown["FormatMatchedInFormat"] = 1;
                        score.TotalScore += 1;
                    }
                    else
                    {
                        score.TotalScore += -12;
                        score.ScoreBreakdown["FormatMismatch"] = -12;
                    }
                }
            }

            // Quality: missing -> penalty only when no format inferred and not NZB.
            //
            // This exemption stays, alongside the missing-language and missing-format ones above.
            // All three charge a release a fixed penalty for metadata a Usenet indexer often does
            // not report, rather than for what the release contains, and none of them is an
            // operator setting. The gates hoisted out of this condition are MinimumSize,
            // MaximumSize and the profile's quality ordering and Allowed flags, all of which the
            // operator sets and all of which describe the release rather than the protocol.
            if (string.IsNullOrEmpty(normalizedQuality))
            {
                if (!isNzb)
                {
                    // A release with no quality label has still made a claim if it declared a
                    // format, and the profile gates that claim the same way it gates a quality.
                    // Only the release that declared neither is genuinely unclassified, and that
                    // one stays in the pool carrying the missing-quality penalty below.
                    //
                    // Both gates are only as good as whatever classified the release. Today that
                    // is TorznabResponseParser, which overwrites an explicit filetype attribute
                    // with a substring scan of title plus description (:349-379), so a book called
                    // "Magnum Opus" arrives here with Format "OPUS" and one called "The Year 1864"
                    // with Quality "MP3 64kbps". Neither gate reads a title itself, and neither can
                    // tell a parsed title apart from a declared attribute. Fixing that precedence
                    // belongs in the parser.
                    //
                    // DetectFormatFromTitle can also write into normalizedFormat, but only under
                    // isNzb, and this branch is torrent-only. Anyone hoisting these gates out of
                    // the isNzb branch inherits that second title-to-veto path.
                    if (!string.IsNullOrEmpty(normalizedFormat) && QualityGate.Refuses(normalizedFormat, profile))
                    {
                        score.TotalScore += QualityNotAllowedPenalty;
                        score.ScoreBreakdown["QualityNotAllowed"] = QualityNotAllowedPenalty;
                        score.RejectionReasons.Add($"Format '{normalizedFormat}' not allowed by profile");
                    }

                    var formatDetected = !string.IsNullOrEmpty(normalizedFormat) || !string.IsNullOrEmpty(DetectFormatFromTitle(titleLower, profile.PreferredFormats)) || (!string.IsNullOrEmpty(searchResult.TorrentUrl) && (searchResult.TorrentUrl.ToLowerInvariant().Contains(".m4b") || searchResult.TorrentUrl.ToLowerInvariant().Contains(".mp3") || searchResult.TorrentUrl.ToLowerInvariant().Contains(".m4a")));
                    if (!formatDetected)
                    {
                        score.TotalScore += QualityMissingPenalty;
                        score.ScoreBreakdown["QualityMissing"] = QualityMissingPenalty;
                    }
                }
            }
            else
            {
                // The result told us its quality, so the profile decides what that is worth and
                // whether it is wanted at all, whatever protocol carried it. Exempting NZB here
                // left every NZB on the base score, so an NZB outranked any torrent regardless of
                // what it contained, and a quality the operator had switched off was grabbed over
                // Usenet with no rejection reason.
                int qualityScore = GetQualityScore(normalizedQuality);
                var qualityDeduction = 100 - qualityScore;
                score.TotalScore -= qualityDeduction;
                score.ScoreBreakdown["Quality"] = qualityScore;

                // The profile's Allowed flags are the gate. PreferredFormats is a preference
                // and was already applied above as a score adjustment; letting it also widen
                // the allowed set made the flag inert, because every rung name in the ladder
                // contains one of the default preferred tokens.
                if (QualityGate.Refuses(normalizedQuality, profile))
                {
                    score.TotalScore += QualityNotAllowedPenalty;
                    score.ScoreBreakdown["QualityNotAllowed"] = QualityNotAllowedPenalty;
                    score.RejectionReasons.Add($"Quality '{normalizedQuality}' not allowed by profile");
                }
            }

            // Preferred words bonus
            if (profile.PreferredWords != null && profile.PreferredWords.Count > 0)
            {
                var bonus = profile.PreferredWords
                    .Where(word => !string.IsNullOrWhiteSpace(word))
                    .Count(word => TitleTermMatcher.TitleContainsTerm(searchResult.Title, word)) * 5;
                if (bonus != 0)
                {
                    score.TotalScore += bonus;
                    score.ScoreBreakdown["PreferredWords"] = bonus;
                }
            }

            // Release shape preference: bundle/omnibus versus a single book.
            if (profile.PreferredReleaseShape != ReleaseShapePreference.NoPreference)
            {
                var releaseLooksLikeBundle = ReleaseShapeDetector.LooksLikeBundle(searchResult.Title);
                var wantBundle = targetIsBundle
                    || profile.PreferredReleaseShape == ReleaseShapePreference.PreferBundle;

                if (releaseLooksLikeBundle == wantBundle)
                {
                    score.TotalScore += ReleaseShapeMatchBonus;
                    score.ScoreBreakdown["ReleaseShapeMatch"] = ReleaseShapeMatchBonus;
                }
                else
                {
                    // Deliberately not a rejection. Detection is a title heuristic, and a book
                    // whose only candidate is on the wrong side of the preference should still
                    // be filled; the breakdown key is how an operator sees why it lost.
                    score.TotalScore += ReleaseShapeMismatchPenalty;
                    score.ScoreBreakdown["ReleaseShapeMismatch"] = ReleaseShapeMismatchPenalty;
                }
            }

            // Seeders bonus
            if ((searchResult.Seeders ?? 0) > 0)
            {
                var seedersBonus = Math.Min(10, searchResult.Seeders ?? 0);
                if (seedersBonus > 0)
                {
                    score.TotalScore += seedersBonus;
                    score.ScoreBreakdown["Seeders"] = seedersBonus;
                }
            }

            // Age penalty scaling up to -60 over 10 years
            if (ageDays > 0)
            {
                var agePenalty = (int)Math.Floor((ageDays / 3650.0) * 60.0);
                agePenalty = Math.Min(agePenalty, 60);
                if (agePenalty > 0)
                {
                    score.TotalScore -= agePenalty;
                    score.ScoreBreakdown["Age"] = -agePenalty;
                }
            }

            // Seeder-based offset for very old torrents
            if (!isNzb && ageDays >= 3650 && (searchResult.Seeders ?? 0) > 0)
            {
                var seeders = searchResult.Seeders ?? 0;
                var seedersAgeBonus = Math.Min(60, (int)Math.Floor((seeders / 20.0) * 60.0));
                if (seedersAgeBonus > 0)
                {
                    score.TotalScore += seedersAgeBonus;
                    score.ScoreBreakdown["SeedersAgeBonus"] = seedersAgeBonus;
                }
            }

            // Check minimum score threshold
            if (profile.MinimumScore > 0 && score.TotalScore < profile.MinimumScore)
            {
                score.RejectionReasons.Add($"Score {score.TotalScore} below profile minimum {profile.MinimumScore}");
                score.TotalScore = -1;
                return score;
            }

            // Final rejection check
            if (score.TotalScore <= 0)
            {
                score.RejectionReasons.Add("Computed score <= 0 (rejected)");
                return score;
            }

            // No ceiling on an accepted release. BaseScore is 100 and every preference above is
            // added to it, so a ceiling of 100 discarded the operator's preferred words, the
            // seeder bonus and the format bonus for any release whose accumulated score reached
            // it, and two releases the profile ranks differently came back identical.
            //
            // The floor was already unreachable: the <= 0 check above returns first. MinimumScore
            // is compared before that, so it has always been read against the accumulated score
            // and its meaning does not change here.
            //
            // Readarr does not cap a preference score either. CalculateCustomFormatScore
            // (src/NzbDrone.Core/Profiles/Qualities/QualityProfile.cs:90-93) is a plain Sum with
            // no bound, DownloadDecisionComparer compares that sum directly
            // (src/NzbDrone.Core/DecisionEngine/DownloadDecisionComparer.cs:79-82), and
            // MinFormatScore and CutoffFormatScore (QualityProfile.cs:19-20) are thresholds over
            // the unbounded sum.
            return score;
        }

        // Helpers (copied/adapted from old service)
        private static bool HasPreferredLanguages(QualityProfile profile) => profile.PreferredLanguages != null && profile.PreferredLanguages.Count > 0;
        private static bool HasPreferredFormats(QualityProfile profile) => profile.PreferredFormats != null && profile.PreferredFormats.Count > 0;

        private static string? DetectFormatFromTitle(string titleLower, List<string>? preferredFormats)
        {
            if (preferredFormats == null || preferredFormats.Count == 0 || string.IsNullOrEmpty(titleLower)) return null;
            return preferredFormats
                .Where(format => !string.IsNullOrWhiteSpace(format))
                .Select(format => format.ToLower().Trim())
                .FirstOrDefault(token => titleLower.Contains(token) || titleLower.Contains("[" + token + "]") || titleLower.Contains("(" + token + ")") || titleLower.Contains("." + token));
        }

        private static string? DetectLanguageFromTitle(string titleLower, List<string>? preferredLanguages)
        {
            if (preferredLanguages == null || preferredLanguages.Count == 0 || string.IsNullOrEmpty(titleLower)) return null;
            foreach (var lang in preferredLanguages.Where(language => !string.IsNullOrWhiteSpace(language)))
            {
                var token = lang.ToLower().Trim();
                if (titleLower.Contains(token) || titleLower.Contains("[" + token + "]") || titleLower.Contains("(" + token + ")") || titleLower.Contains(" " + token + " "))
                {
                    return lang;
                }
            }
            var common = new Dictionary<string, string>
            {
                { "eng", "English" }, { "english", "English" }, { "es", "Spanish" }, { "spanish", "Spanish" },
                { "de", "German" }, { "german", "German" }, { "fr", "French" }, { "french", "French" }
            };
            foreach (var (token, name) in common) if (titleLower.Contains(token)) return name;
            return null;
        }

        private int GetQualityScore(string quality)
        {
            if (string.IsNullOrEmpty(quality)) return 0;
            var lowerQuality = quality.ToLower();
            if (lowerQuality.Contains("flac")) return 100;
            if (lowerQuality.Contains("aax")) return 95;
            if (lowerQuality.Contains("m4b")) return 90;
            if (lowerQuality.Contains("opus")) return 85;
            if (ContainsVbrPreset(lowerQuality, "v0")) return 82;
            if (ContainsVbrPreset(lowerQuality, "v1")) return 76;
            if (ContainsVbrPreset(lowerQuality, "v2")) return 70;
            if (lowerQuality.Contains("aac") || lowerQuality.Contains("m4a")) return 78;
            if (lowerQuality.Contains("320")) return 80;
            if (lowerQuality.Contains("256")) return 74;
            if (lowerQuality.Contains("192")) return 60;
            if (lowerQuality.Contains("vbr") || lowerQuality.Contains("cbr")) return 65;
            if (lowerQuality.Contains("mp3") && !ContainsAnyBitrate(lowerQuality, "64", "128", "192", "256", "320")) return 65;
            if (lowerQuality.Contains("128")) return 50;
            if (lowerQuality.Contains("64")) return 40;
            return 0;
        }

        private static bool ContainsVbrPreset(string qualityLower, string preset) => qualityLower.Contains(preset) || qualityLower.Contains($"-{preset}") || qualityLower.Contains($" {preset}");
        private static bool ContainsAnyBitrate(string qualityLower, params string[] bitrates) => bitrates.Any(b => qualityLower.Contains(b));

        private static bool IsNzbResult(SearchResult r)
        {
            bool hasNzbUrl = !string.IsNullOrEmpty(r.NzbUrl);
            bool isNzbType = string.Equals(r.DownloadType, "nzb", StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.DownloadType, "usenet", StringComparison.OrdinalIgnoreCase);
            bool indexerIndicatesNzb = !string.IsNullOrEmpty(r.IndexerImplementation)
                && (r.IndexerImplementation.IndexOf("nzb", StringComparison.OrdinalIgnoreCase) >= 0
                    || r.IndexerImplementation.IndexOf("usenet", StringComparison.OrdinalIgnoreCase) >= 0);
            bool sourceIndicatesNzb = !string.IsNullOrEmpty(r.Source)
                && r.Source.IndexOf("usenet", StringComparison.OrdinalIgnoreCase) >= 0;
            bool urlIndicatesNzb = !string.IsNullOrEmpty(r.ResultUrl)
                && (r.ResultUrl.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase)
                    || r.ResultUrl.IndexOf("/nzb", StringComparison.OrdinalIgnoreCase) >= 0);
            bool torrentIndicatesNzb = !string.IsNullOrEmpty(r.TorrentUrl)
                && r.TorrentUrl.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase);
            return hasNzbUrl || isNzbType || indexerIndicatesNzb || sourceIndicatesNzb || urlIndicatesNzb || torrentIndicatesNzb;
        }
    }
}
