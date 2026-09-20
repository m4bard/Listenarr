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
    private static readonly IReadOnlyDictionary<string, string> LanguageCodes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ENG", "English" }, { "EN", "English" },
            { "DUT", "Dutch" },   { "NLD", "Dutch" },   { "NL", "Dutch" },
            { "GER", "German" },  { "DEU", "German" },  { "DE", "German" },
            { "FRE", "French" },  { "FRA", "French" },  { "FR", "French" },
            { "SPA", "Spanish" }, { "ES", "Spanish" }
        };

    // Digits are a bitrate only where the text says they are one: either a rate word carries them
    // (mp3 320, MP3CBR320, mp3-320) or a rate unit follows them (320kbps, 64 kbps, [64k]). Every
    // other number in a release name is something else, and reading it as a bitrate is how "x264"
    // became MP3 64kbps and "Size: 320 MB" became MP3 320kbps.
    private static readonly Regex BitrateTokenPattern = new(
        @"(?<=(?:mp3|cbr|vbr|abr)[\s@_./|+-]{0,3})(?<rate>320|256|192|128|64)(?![\p{L}\p{N}])"
        + @"|(?<![\p{L}\p{N}])(?<rate>320|256|192|128|64)[\s_-]{0,2}k(?:bit/s|bits?|bps|bs|b)?(?![\p{L}\p{N}])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns the highest recognised MP3 bitrate label that <paramref name="text"/> states as a
    /// bitrate, or null when it states none. A number that merely happens to be 64, 128, 192, 256
    /// or 320 is not one: not inside a longer token (x264, 1964, 1280x720) and not standing on its
    /// own either (Size: 320 MB, Chapter 64, 128,000 words).
    /// </summary>
    public static string? DetectBitrateQuality(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        var bestKbps = 0;
        foreach (Match match in BitrateTokenPattern.Matches(text))
        {
            if (int.TryParse(match.Groups["rate"].Value, out var kbps) && kbps > bestKbps)
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

        var normalized = Regex.Replace(text, "\\s+", " ", RegexOptions.Compiled | RegexOptions.IgnoreCase).Trim();
        var alternation = string.Join("|", LanguageCodes.Keys.Select(Regex.Escape));
        var bracketedPattern = $@"[\[\(]\s*(?:{alternation})\b";
        var wordBoundaryPattern = $"\\b(?:{alternation})\\b";

        var bracketMatch = Regex.Match(normalized, bracketedPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        if (bracketMatch.Success)
        {
            var code = bracketMatch.Value.TrimStart('[', '(').Trim().Split(' ', '/', ',')[0];
            if (LanguageCodes.TryGetValue(code.ToUpperInvariant(), out var language)) return language;
        }

        var wordMatch = Regex.Match(normalized, wordBoundaryPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        if (wordMatch.Success)
        {
            var code = wordMatch.Value.Trim();
            if (LanguageCodes.TryGetValue(code.ToUpperInvariant(), out var language)) return language;
        }

        return null;
    }

    public static string? ParseLanguageFromCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        return LanguageCodes.TryGetValue(code.ToUpperInvariant(), out var language)
            ? language
            : null;
    }
}
