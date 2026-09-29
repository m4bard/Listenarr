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

using Listenarr.Api.Startup;
using Serilog.Events;

namespace Listenarr.Tests.Features.Api.Startup;

/// <summary>
/// Covers <see cref="ListenarrBuilderFactory.TryParseLogEventLevel"/>, the helper behind
/// <c>LISTENARR_LOG_LEVEL</c> parsing. Serilog's <see cref="LogEventLevel"/> has no
/// <c>Trace</c> or <c>Critical</c> member (its levels are Verbose, Debug, Information, Warning,
/// Error, Fatal), so a value typed by someone coming from Microsoft.Extensions.Logging
/// conventions used to fail <c>Enum.TryParse</c> silently and fall back to Information.
/// </summary>
public sealed class ListenarrBuilderFactoryLogLevelParsingTests
{
    [Theory]
    [InlineData("Trace", LogEventLevel.Verbose)]
    [InlineData("trace", LogEventLevel.Verbose)]
    [InlineData("TRACE", LogEventLevel.Verbose)]
    public void TryParseLogEventLevel_Trace_MapsToVerbose(string value, LogEventLevel expected)
    {
        var recognized = ListenarrBuilderFactory.TryParseLogEventLevel(value, out var level);

        Assert.True(recognized);
        Assert.Equal(expected, level);
    }

    [Theory]
    [InlineData("Critical", LogEventLevel.Fatal)]
    [InlineData("critical", LogEventLevel.Fatal)]
    public void TryParseLogEventLevel_Critical_MapsToFatal(string value, LogEventLevel expected)
    {
        var recognized = ListenarrBuilderFactory.TryParseLogEventLevel(value, out var level);

        Assert.True(recognized);
        Assert.Equal(expected, level);
    }

    [Theory]
    [InlineData("Verbose", LogEventLevel.Verbose)]
    [InlineData("Debug", LogEventLevel.Debug)]
    [InlineData("Information", LogEventLevel.Information)]
    [InlineData("information", LogEventLevel.Information)]
    [InlineData("Warning", LogEventLevel.Warning)]
    [InlineData("Error", LogEventLevel.Error)]
    [InlineData("Fatal", LogEventLevel.Fatal)]
    public void TryParseLogEventLevel_NativeSerilogNames_StillParse(string value, LogEventLevel expected)
    {
        // Control: the native Serilog names must keep working exactly as before. If the Trace/
        // Critical aliasing broke the existing behaviour this assertion would fail.
        var recognized = ListenarrBuilderFactory.TryParseLogEventLevel(value, out var level);

        Assert.True(recognized);
        Assert.Equal(expected, level);
    }

    [Theory]
    [InlineData("Verbos")] // typo, one letter short
    [InlineData("Trac")] // typo
    [InlineData("Chatty")] // not a level at all, in either logging convention
    [InlineData("None")] // Microsoft.Extensions.Logging's "no logging" sentinel; Serilog's
                          // LogEventLevel has no equivalent, so this is deliberately rejected
                          // rather than silently mapped onto some other level.
    public void TryParseLogEventLevel_UnrecognizedValue_ReturnsFalse(string value)
    {
        var recognized = ListenarrBuilderFactory.TryParseLogEventLevel(value, out _);

        Assert.False(recognized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParseLogEventLevel_EmptyOrWhitespace_ReturnsFalse(string? value)
    {
        var recognized = ListenarrBuilderFactory.TryParseLogEventLevel(value, out _);

        Assert.False(recognized);
    }

    [Fact]
    public void TryParseLogEventLevel_SurroundingWhitespace_IsTrimmed()
    {
        var recognized = ListenarrBuilderFactory.TryParseLogEventLevel("  Trace  ", out var level);

        Assert.True(recognized);
        Assert.Equal(LogEventLevel.Verbose, level);
    }
}
