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

namespace Listenarr.Tests.Features.Application.Search.Parsing;

[Trait("Name", "BitrateTokenQualityTests")]
[Trait("Category", "Application")]
public sealed class BitrateTokenQualityTests : BaseTests
{
    // Digits that belong to some other token are not a bitrate. Each case is paired below with a
    // control carrying the same digits in a form that really is one.
    [Theory]
    [InlineData("The Governor x264 WEBRip")]
    [InlineData("Some Release h264 WEB-DL")]
    [InlineData("Mercury Rising 1964 Unabridged")]
    [InlineData("Documentary 1280x720 WEBRip")]
    [InlineData("Installer x64 Edition")]
    [InlineData("MP3 640kbps Oddity")]
    [InlineData("Chapter 1928 of the Chronicle")]
    public void DetectBitrateQuality_DigitsInsideAnotherToken_AreNotABitrate(string text)
    {
        Assert.Null(SearchResultAttributeParser.DetectBitrateQuality(text));
    }

    // A number standing on its own is not a bitrate either. These are the shapes a description
    // really carries, and every one of them named an MP3 bitrate before this change.
    [Theory]
    [InlineData("Size: 320 MB")]
    [InlineData("The Governor x264 WEBRip Size: 320 MB")]
    [InlineData("Chapter 64")]
    [InlineData("Part 128 of 200")]
    [InlineData("Duration: 320 min.")]
    [InlineData("128,000 words")]
    [InlineData("ISBN 978-0-320-12345-6")]
    [InlineData("Ancient Rome 64 BC")]
    public void DetectBitrateQuality_NumberWithNoRateWordOrUnit_IsNotABitrate(string text)
    {
        Assert.Null(SearchResultAttributeParser.DetectBitrateQuality(text));
    }

    // Glued to a rate word it still counts, which a plain token boundary would have missed.
    [Theory]
    [InlineData("Some Book mp3320", "MP3 320kbps")]
    [InlineData("Some Book MP3CBR320", "MP3 320kbps")]
    [InlineData("Some Book MP3_320", "MP3 320kbps")]
    [InlineData("Some Book VBR 256", "MP3 256kbps")]
    [InlineData("Some Book 320kbs", "MP3 320kbps")]
    public void DetectBitrateQuality_RateWordCarriesTheDigits_Resolves(string text, string expected)
    {
        Assert.Equal(expected, SearchResultAttributeParser.DetectBitrateQuality(text));
    }

    [Fact]
    public void DetectBitrateQuality_KnownResidual_AKilobyteSizeStillLooksLikeABitrate()
    {
        // Documented, not fixed: a k unit is taken as a rate unit, so a size quoted in kb reads as
        // a bitrate. Kept as a test so the next reader does not assume free text is now safe.
        Assert.Equal("MP3 128kbps", SearchResultAttributeParser.DetectBitrateQuality("Sample 128 kb"));
    }

    // The controls: the same digits, this time standing as a bitrate, still resolve.
    [Theory]
    [InlineData("The Governor 64kbps WEB", "MP3 64kbps")]
    [InlineData("The Governor MP3 64", "MP3 64kbps")]
    [InlineData("The Governor 64 kbps", "MP3 64kbps")]
    [InlineData("The Governor [64k]", "MP3 64kbps")]
    [InlineData("The Governor 64kbit/s", "MP3 64kbps")]
    [InlineData("Mercury Rising 1964 Unabridged 128kbps", "MP3 128kbps")]
    [InlineData("Documentary 1280x720 with 192kbps audio", "MP3 192kbps")]
    [InlineData("Book MP3-320", "MP3 320kbps")]
    [InlineData("Book 256 kbps", "MP3 256kbps")]
    public void DetectBitrateQuality_BitrateAsItsOwnToken_Resolves(string text, string expected)
    {
        Assert.Equal(expected, SearchResultAttributeParser.DetectBitrateQuality(text));
    }

    [Fact]
    public void DetectBitrateQuality_MixedRealAndIncidentalNumbers_TakesTheRealOne()
    {
        // The old ladder tested 320 anywhere before 128 anywhere, so a chapter number could beat
        // the release's own bitrate. Only the stated bitrate counts now.
        Assert.Equal("MP3 320kbps", SearchResultAttributeParser.DetectBitrateQuality("MP3CBR320 chapter 128"));
        Assert.Equal("MP3 128kbps", SearchResultAttributeParser.DetectBitrateQuality("128kbps, 320 pages"));
    }

    [Fact]
    public void DetectBitrateQuality_SeveralBitratesPresent_TakesTheHighest()
    {
        // The old ladder tested 320 before 64, so preserve that precedence.
        Assert.Equal("MP3 320kbps", SearchResultAttributeParser.DetectBitrateQuality("Sample 64kbps, full 320kbps"));
        Assert.Equal("MP3 320kbps", SearchResultAttributeParser.DetectBitrateQuality("Full 320kbps, sample 64kbps"));
    }

    // Every label DetectQualityFromTags recognised before still resolves the same way.
    [Theory]
    [InlineData("[ENG / FLAC] Title", "FLAC")]
    [InlineData("[ENG / M4B] Title", "M4B")]
    [InlineData("Title MP3 320kbps", "MP3 320kbps")]
    [InlineData("Title MP3 256kbps", "MP3 256kbps")]
    [InlineData("Title MP3 192kbps", "MP3 192kbps")]
    [InlineData("Title MP3 128kbps", "MP3 128kbps")]
    [InlineData("Title MP3 64kbps", "MP3 64kbps")]
    [InlineData("Title with nothing to go on", "Unknown")]
    public void DetectQualityFromTags_KeepsRecognisedLabels(string tags, string expected)
    {
        Assert.Equal(expected, SearchResultAttributeParser.DetectQualityFromTags(tags));
    }

    [Fact]
    public void DetectQualityFromTags_VideoTitle_NoLongerReadsAsLowBitrateMp3()
    {
        // The defect: x264 in a title was labelled MP3 64kbps.
        Assert.Equal("Unknown", SearchResultAttributeParser.DetectQualityFromTags("The Governor x264 WEBRip"));

        // The control: a real 64kbps release in the same shape is still labelled.
        Assert.Equal("MP3 64kbps", SearchResultAttributeParser.DetectQualityFromTags("The Governor 64kbps WEBRip"));
    }

    [Fact]
    public void DetectQualityFromTags_FlacStillOutranksABitrateToken()
    {
        Assert.Equal("FLAC", SearchResultAttributeParser.DetectQualityFromTags("Title FLAC 320kbps transcode note"));
    }
}
