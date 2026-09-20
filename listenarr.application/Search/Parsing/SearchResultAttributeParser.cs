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

namespace Listenarr.Application.Search.Parsing;

public static class SearchResultAttributeParser
{
    // Unambiguous tokens — 3-letter ISO codes, full/native names, and short codes that are
    // not common words — are trusted anywhere in a release title. Deliberately broad so a
    // non-English release (e.g. a Danish "...-DK-...") is flagged rather than grabbed as if
    // it were English.
    private static readonly IReadOnlyDictionary<string, string> LanguageCodes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ENG", "English" }, { "EN", "English" }, { "English", "English" },
            { "DA", "Danish" }, { "DK", "Danish" }, { "Danish", "Danish" }, { "Dansk", "Danish" },
            { "DUT", "Dutch" }, { "NLD", "Dutch" }, { "NL", "Dutch" }, { "Dutch", "Dutch" }, { "Nederlands", "Dutch" },
            { "GER", "German" }, { "DEU", "German" }, { "DE", "German" }, { "German", "German" }, { "Deutsch", "German" },
            { "FRE", "French" }, { "FRA", "French" }, { "FR", "French" }, { "French", "French" }, { "Francais", "French" }, { "Français", "French" },
            { "SPA", "Spanish" }, { "ES", "Spanish" }, { "Spanish", "Spanish" }, { "Espanol", "Spanish" }, { "Español", "Spanish" }, { "Castellano", "Spanish" },
            { "ITA", "Italian" }, { "Italian", "Italian" }, { "Italiano", "Italian" },
            { "SWE", "Swedish" }, { "SV", "Swedish" }, { "Swedish", "Swedish" }, { "Svenska", "Swedish" },
            { "NB", "Norwegian" }, { "Norwegian", "Norwegian" }, { "Norsk", "Norwegian" },
            { "FIN", "Finnish" }, { "FI", "Finnish" }, { "Finnish", "Finnish" }, { "Suomi", "Finnish" },
            { "POR", "Portuguese" }, { "Portuguese", "Portuguese" }, { "Portugues", "Portuguese" }, { "Português", "Portuguese" },
            { "POL", "Polish" }, { "PL", "Polish" }, { "Polish", "Polish" }, { "Polski", "Polish" },
            { "RUS", "Russian" }, { "RU", "Russian" }, { "Russian", "Russian" },
            { "JPN", "Japanese" }, { "JA", "Japanese" }, { "JP", "Japanese" }, { "Japanese", "Japanese" },
            { "CHI", "Chinese" }, { "ZHO", "Chinese" }, { "ZH", "Chinese" }, { "Chinese", "Chinese" }, { "Mandarin", "Chinese" },
            { "CES", "Czech" }, { "CZE", "Czech" }, { "CS", "Czech" }, { "CZ", "Czech" }, { "Czech", "Czech" },
            { "HUN", "Hungarian" }, { "HU", "Hungarian" }, { "Hungarian", "Hungarian" }, { "Magyar", "Hungarian" },
            { "TUR", "Turkish" }, { "TR", "Turkish" }, { "Turkish", "Turkish" },
            { "ELL", "Greek" }, { "GRE", "Greek" }, { "Greek", "Greek" },
            { "KOR", "Korean" }, { "KO", "Korean" }, { "Korean", "Korean" },
            { "ISL", "Icelandic" }, { "Icelandic", "Icelandic" },
        };

    // Tokens that are also ordinary words or names — the short codes ("it", "no", "se",
    // "pt") and the name-like aliases ("DAN" as in "Dan Brown", "NOR", "EL") — are trusted
    // only inside [brackets]/(parentheses) or in the delimited-before-the-year scene-tag
    // position, never as a bare title token. This stops an author's name or a stray word
    // from being read as the release language (e.g. "Dan Brown ...-WEB-DE-2017" is German,
    // not Danish).
    private static readonly IReadOnlyDictionary<string, string> AmbiguousLanguageCodes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "IT", "Italian" }, { "NO", "Norwegian" }, { "SE", "Swedish" }, { "PT", "Portuguese" },
            { "DAN", "Danish" }, { "NOR", "Norwegian" }, { "EL", "Greek" },
        };

    private static readonly IReadOnlyDictionary<string, string> AllLanguageCodes =
        LanguageCodes.Concat(AmbiguousLanguageCodes)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

    // A bitrate counts only where it stands as a token of its own. "x264" and "1964" both contain
    // the digits 64 and neither is a 64kbps release, so the digits are required to start on a
    // boundary and to end on one once an optional k/kb/kbps suffix has been taken.
    private static readonly Regex BitrateTokenPattern = new(
        @"(?<![\p{L}\p{N}])(320|256|192|128|64)\s*(?:k(?:bit/s|bits?|bps|b)?)?(?![\p{L}\p{N}])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns the highest recognised MP3 bitrate label that <paramref name="text"/> declares as a
    /// standalone token, or null when it declares none. Digits inside a longer token (a video codec
    /// such as x264, a year such as 1964, a resolution such as 1280x720) are not a bitrate.
    /// </summary>
    public static string? DetectBitrateQuality(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        var bestKbps = 0;
        foreach (Match match in BitrateTokenPattern.Matches(text))
        {
            if (int.TryParse(match.Groups[1].Value, out var kbps) && kbps > bestKbps)
                bestKbps = kbps;
        }

        return bestKbps switch
        {
            320 => "MP3 320kbps",
            256 => "MP3 256kbps",
            192 => "MP3 192kbps",
            128 => "MP3 128kbps",
            64 => "MP3 64kbps",
            _ => null
        };
    }

    public static string DetectQualityFromTags(string tags)
    {
        var lowerTags = tags.ToLowerInvariant();

        if (lowerTags.Contains("flac"))
            return "FLAC";

        var bitrate = DetectBitrateQuality(lowerTags);
        if (bitrate != null)
            return bitrate;

        if (lowerTags.Contains("m4b"))
            return "M4B";

        return "Unknown";
    }

    public static string DetectQualityFromFormat(string format)
    {
        if (string.IsNullOrEmpty(format))
            return "Unknown";

        var lowerFormat = format.ToLowerInvariant();

        if (lowerFormat.Contains("flac"))
            return "FLAC";
        if (lowerFormat.Contains("m4b") || lowerFormat.Contains("apple audiobook"))
            return "M4B";
        if (lowerFormat.Contains("320kbps") || lowerFormat.Contains("320 kbps"))
            return "MP3 320kbps";
        if (lowerFormat.Contains("256kbps") || lowerFormat.Contains("256 kbps"))
            return "MP3 256kbps";
        if (lowerFormat.Contains("192kbps") || lowerFormat.Contains("192 kbps"))
            return "MP3 192kbps";
        if (lowerFormat.Contains("128kbps") || lowerFormat.Contains("128 kbps"))
            return "MP3 128kbps";
        if (lowerFormat.Contains("64kbps") || lowerFormat.Contains("64 kbps"))
            return "MP3 64kbps";
        if (lowerFormat.Contains("vbr mp3") || lowerFormat.Contains("variable bitrate"))
            return "MP3 VBR";
        if (lowerFormat.Contains("ogg vorbis") || lowerFormat.Contains("ogg"))
            return "OGG Vorbis";
        if (lowerFormat.Contains("opus"))
            return "OPUS";
        if (lowerFormat.Contains("aac"))
            return "AAC";
        if (lowerFormat.Contains("mp3"))
            return "MP3";

        return "Unknown";
    }

    public static string DetectFormatFromTags(string tags)
    {
        var lowerTags = tags.ToLowerInvariant();

        if (lowerTags.Contains("m4b"))
            return "M4B";
        if (lowerTags.Contains("flac"))
            return "FLAC";
        if (lowerTags.Contains("mp3"))
            return "MP3";
        if (lowerTags.Contains("opus"))
            return "OPUS";
        if (lowerTags.Contains("aac"))
            return "AAC";

        return "MP3";
    }

    public static string? ParseLanguageFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var normalized = Regex.Replace(text, "\\s+", " ", RegexOptions.Compiled).Trim();

        // Bracketed/parenthesized tags are high-confidence; trust every code there,
        // including the short ambiguous ones.
        var bracketed = MatchLanguageToken(normalized, AllLanguageCodes, bracketedOnly: true);
        if (bracketed != null) return bracketed;

        // Audiobook scene releases encode the language as a code wedged tightly between scene
        // delimiters immediately before the year, e.g. "...AUDiOBOOK-WEB-SE-2023-GROUP"
        // (Swedish) or "...-DK-2026-..." (Danish). That is the release's own language label,
        // so it is checked BEFORE the standalone pass — otherwise an ordinary word in the book
        // title ("French.Lessons-...-WEB-SE-2023") would override the release tag. Requiring the
        // tight scene delimiters (no spaces) keeps human titles like "It 1986" / "Book No 2022"
        // from being mistaken for a scene tag.
        var sceneTag = MatchReleaseTagLanguage(normalized, AllLanguageCodes);
        if (sceneTag != null) return sceneTag;

        // Elsewhere only trust unambiguous tokens. The ambiguous short codes and the name-like
        // aliases (DAN/NOR/EL) are excluded here so an author's name or a stray word ("Dan
        // Brown", "it", "no") is not read as a language on its own.
        return MatchLanguageToken(normalized, LanguageCodes, bracketedOnly: false);
    }

    private static string? MatchReleaseTagLanguage(
        string normalized,
        IReadOnlyDictionary<string, string> codes)
    {
        var alternation = string.Join("|", codes.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));
        if (alternation.Length == 0)
        {
            return null;
        }

        // <scene-delimiter><code><scene-delimiter><19xx|20xx year>. The delimiters are the
        // tight scene characters (-._) only — a plain space is excluded so an ordinary title
        // word before a year ("It 1986", "Book No 2022") is not accepted as a scene tag.
        var pattern = $@"(?<=[-._])(?<tok>{alternation})(?=[-._](?:19|20)\d{{2}}(?:\b|[-._ ]))";
        var match = Regex.Match(normalized, pattern, RegexOptions.IgnoreCase);
        return match.Success && codes.TryGetValue(match.Groups["tok"].Value, out var language)
            ? language
            : null;
    }

    private static string? MatchLanguageToken(
        string normalized,
        IReadOnlyDictionary<string, string> codes,
        bool bracketedOnly)
    {
        // Longest tokens first so full names win over their abbreviations.
        var alternation = string.Join("|", codes.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));
        if (alternation.Length == 0) return null;

        var pattern = bracketedOnly
            ? $@"[\[\(]\s*(?<tok>{alternation})\b"
            : $@"\b(?<tok>{alternation})\b";
        var match = Regex.Match(normalized, pattern, RegexOptions.IgnoreCase);
        return match.Success && codes.TryGetValue(match.Groups["tok"].Value, out var language)
            ? language
            : null;
    }

    public static string? ParseLanguageFromCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        return AllLanguageCodes.TryGetValue(code.Trim(), out var language)
            ? language
            : null;
    }
}
