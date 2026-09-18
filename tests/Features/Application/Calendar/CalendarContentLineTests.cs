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

using System.Text;
using Listenarr.Application.Calendar;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Calendar;

/// <summary>
/// The fold and the TEXT escape, tested at the boundary rather than well inside it. The writer's
/// own fold test builds a 400 character line and asserts every line is at most 75 octets, which
/// an implementation that folded at 70 would also satisfy.
/// </summary>
[Trait("Area", "Calendar")]
[Trait("Name", "CalendarContentLineTests")]
[Trait("Category", "Unit")]
public sealed class CalendarContentLineTests : BaseTests
{
    [Fact]
    public void AppendFolded_AtExactlySeventyFiveOctets_DoesNotFold()
    {
        var line = new string('x', 75);

        var folded = Fold(line);

        Assert.Equal(line + "\r\n", folded);
        Assert.Single(Lines(folded));
    }

    [Fact]
    public void AppendFolded_AtSeventySixOctets_FoldsExactlyOnce()
    {
        var line = new string('x', 76);

        var lines = Lines(Fold(line));

        Assert.Equal(2, lines.Length);
        Assert.Equal(new string('x', 75), lines[0]);

        // The continuation begins with a single space, and RFC 5545 counts that space against the
        // continuation line's own 75 octets, so the 76th character sits behind it.
        Assert.Equal(" x", lines[1]);
        Assert.Equal(75, Encoding.UTF8.GetByteCount(lines[0]));
    }

    [Fact]
    public void AppendFolded_AtSeventyFourOctets_DoesNotFoldEither()
    {
        // The other side of the boundary, so an implementation folding at 74 is caught too.
        var lines = Lines(Fold(new string('x', 74)));

        Assert.Single(lines);
    }

    [Fact]
    public void AppendFolded_NeverSplitsASurrogatePair()
    {
        // U+1F600 is four octets and lives outside the BMP, so it is two UTF-16 chars. The fold
        // walks chars, and splitting the pair would emit two lone surrogates. Nothing in the
        // suite reached this branch before: every other multi-byte case is in the BMP.
        // The prefix length is chosen, not arbitrary. A lone surrogate encodes as the 3-octet
        // replacement character, so a fold that walked chars rather than pairs would fit
        // floor((75 - prefix) / 3) halves on the first line. With an 8-octet prefix that is 22,
        // an even number, and the split would land on a pair boundary by luck and prove nothing.
        // A 6-octet prefix gives 23, so a per-char fold lands inside a pair.
        var emoji = "\U0001F600";
        var line = "TITLE:" + string.Concat(Enumerable.Repeat(emoji, 60));

        var folded = Fold(line);

        foreach (var emitted in Lines(folded))
        {
            Assert.True(
                Encoding.UTF8.GetByteCount(emitted) <= 75,
                $"line exceeded 75 octets: {Encoding.UTF8.GetByteCount(emitted)}");

            // A split pair leaves a lone surrogate, which is not encodable, so the strict UTF-8
            // encoder is the assertion: it throws rather than substituting U+FFFD.
            Encoding.GetEncoding(
                    "utf-8",
                    EncoderFallback.ExceptionFallback,
                    DecoderFallback.ExceptionFallback)
                .GetBytes(emitted);
        }

        // Unfolding must give the line back byte for byte, pairs intact.
        Assert.Equal(line, Unfold(folded));
    }

    [Fact]
    public void AppendFolded_WithAFourOctetCharacterStraddlingTheBoundary_KeepsItWhole()
    {
        // 72 ASCII characters then one four-octet character: 72 + 4 is 76, so the emoji cannot
        // stay on the first line and must move whole rather than be cut at the 75th octet.
        var line = new string('x', 72) + "\U0001F600";

        var lines = Lines(Fold(line));

        Assert.Equal(2, lines.Length);
        Assert.Equal(new string('x', 72), lines[0]);
        Assert.Equal(" \U0001F600", lines[1]);
    }

    [Theory]
    [InlineData("\u0000")]
    [InlineData("\u0001")]
    [InlineData("\u0007")]
    [InlineData("\u0008")]
    [InlineData("\u000B")]
    [InlineData("\u000C")]
    [InlineData("\u001B")]
    [InlineData("\u001F")]
    [InlineData("\u007F")]
    public void EscapeText_DropsControlCharactersTsafeCharExcludes(string control)
    {
        // RFC 5545 section 3.3.11. One stray control byte in a provider-supplied description
        // produces a document strict clients reject whole, so every event is lost rather than
        // one property.
        var escaped = CalendarContentLine.EscapeText("before" + control + "after");

        Assert.Equal("beforeafter", escaped);
    }

    [Fact]
    public void EscapeText_KeepsHorizontalTabBecauseWspAllowsIt()
    {
        Assert.Equal("before\tafter", CalendarContentLine.EscapeText("before\tafter"));
    }

    [Fact]
    public void EscapeText_StillEscapesTheCharactersThatNeedIt()
    {
        // Guard against the control-character filter swallowing the escapes it sits beside.
        Assert.Equal(
            "a\\\\b\\;c\\,d\\ne",
            CalendarContentLine.EscapeText("a\\b;c,d\ne"));
    }

    [Fact]
    public void EscapeText_DropsCarriageReturnWithoutLeavingAStrayEscape()
    {
        Assert.Equal("a\\nb", CalendarContentLine.EscapeText("a\r\nb"));
    }

    private static string Fold(string contentLine)
    {
        var builder = new StringBuilder();
        CalendarContentLine.AppendFolded(builder, contentLine);
        return builder.ToString();
    }

    private static string[] Lines(string folded) =>
        folded.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    private static string Unfold(string folded) =>
        folded
            .Replace("\r\n ", string.Empty, StringComparison.Ordinal)
            .Replace("\r\n", string.Empty, StringComparison.Ordinal);
}
