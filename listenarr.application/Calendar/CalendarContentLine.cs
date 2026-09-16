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
using System.Text;

namespace Listenarr.Application.Calendar
{
    /// <summary>
    /// Builds RFC 5545 content lines: TEXT escaping and the 75 octet fold.
    /// </summary>
    public static class CalendarContentLine
    {
        /// <summary>RFC 5545 section 3.1: lines are delimited by CRLF, not by the host newline.</summary>
        public const string LineSeparator = "\r\n";

        private const int MaxOctets = 75;

        /// <summary>
        /// Escapes a TEXT value: backslash, semicolon and comma are escaped, and newlines become
        /// a literal backslash-n. Carriage returns are dropped so a CRLF does not leave a stray
        /// escape behind.
        /// </summary>
        public static string EscapeText(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case ';':
                        builder.Append("\\;");
                        break;
                    case ',':
                        builder.Append("\\,");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        break;
                    default:
                        builder.Append(character);
                        break;
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Appends one already-escaped content line, folded so no line exceeds 75 octets. Folds
        /// fall on UTF-8 character boundaries, never inside a multi-byte sequence.
        /// </summary>
        public static void AppendFolded(StringBuilder destination, string contentLine)
        {
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(contentLine);

            var octetsOnLine = 0;
            var index = 0;

            while (index < contentLine.Length)
            {
                // Surrogate pairs are one character for folding purposes, so take the whole pair.
                var runLength = char.IsHighSurrogate(contentLine[index])
                                && index + 1 < contentLine.Length
                                && char.IsLowSurrogate(contentLine[index + 1])
                    ? 2
                    : 1;
                var run = contentLine.Substring(index, runLength);
                var runOctets = Encoding.UTF8.GetByteCount(run);

                if (octetsOnLine > 0 && octetsOnLine + runOctets > MaxOctets)
                {
                    destination.Append(LineSeparator).Append(' ');
                    octetsOnLine = 1;
                }

                destination.Append(run);
                octetsOnLine += runOctets;
                index += runLength;
            }

            destination.Append(LineSeparator);
        }

        /// <summary>Formats a DateOnly as an RFC 5545 DATE value.</summary>
        public static string FormatDate(DateOnly date) =>
            date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        /// <summary>Formats an instant as an RFC 5545 UTC DATE-TIME value.</summary>
        public static string FormatUtcTimestamp(DateTimeOffset instant) =>
            instant.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
    }
}
