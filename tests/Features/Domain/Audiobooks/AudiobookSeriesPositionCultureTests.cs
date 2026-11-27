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
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Audiobooks;

[Trait("Name", "AudiobookSeriesPositionCultureTests")]
[Trait("Category", "Domain")]
public sealed class AudiobookSeriesPositionCultureTests : BaseTests
{
    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void CreateBasicAudioMetadata_ParsesSeriesNumberInvariantly(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);

            var metadata = new Audiobook { SeriesNumber = "1.5" }.CreateBasicAudioMetadata();

            Assert.Equal(1.5m, metadata.SeriesPosition);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
