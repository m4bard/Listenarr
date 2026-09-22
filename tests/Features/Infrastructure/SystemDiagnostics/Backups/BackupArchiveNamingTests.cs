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
using Listenarr.Infrastructure.SystemDiagnostics.Backups;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.SystemDiagnostics.Backups;

[Trait("Name", "BackupArchiveNamingTests")]
[Trait("Category", "Backup")]
public sealed class BackupArchiveNamingTests : BaseTests
{
    [Fact]
    [Trait("Scenario", "NamesRoundTrip")]
    public void BuildFileName_ProducesNamesThatAreRecognisedAgain()
    {
        // Given a build version and a moment
        var timestamp = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

        // When the archive is named
        var name = BackupArchiveNaming.BuildFileName("1.3.4", timestamp);

        // Then it carries the version and the moment, and is recognised as ours
        Assert.Equal("listenarr_backup_v1.3.4_2026.03.04_05.06.07.zip", name);
        Assert.True(BackupArchiveNaming.IsBackupArchive(name));
    }

    [Fact]
    [Trait("Scenario", "UnknownVersion")]
    public void BuildFileName_OmitsTheVersionSegment_WhenNoVersionIsAvailable()
    {
        // Given no usable version, which ApplicationVersionService reports as "unknown"
        var timestamp = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

        // When the archive is named
        var name = BackupArchiveNaming.BuildFileName(null, timestamp);

        // Then there is no empty "v_" segment, and it is still recognised
        Assert.Equal("listenarr_backup_2026.03.04_05.06.07.zip", name);
        Assert.True(BackupArchiveNaming.IsBackupArchive(name));
    }

    [Theory]
    [Trait("Scenario", "VersionSanitisation")]
    [InlineData("1.3.4+build/9", "listenarr_backup_v1.3.4build9_2026.03.04_05.06.07.zip")]
    [InlineData("../../etc", "listenarr_backup_vetc_2026.03.04_05.06.07.zip")]
    [InlineData("///", "listenarr_backup_2026.03.04_05.06.07.zip")]
    public void BuildFileName_StripsCharactersThatWouldLeaveTheDirectory(string version, string expected)
    {
        // Given a version string carrying separators, which informational versions can
        var timestamp = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

        // When the archive is named
        var name = BackupArchiveNaming.BuildFileName(version, timestamp);

        // Then the name stays a single path segment
        Assert.Equal(expected, name);
        Assert.DoesNotContain(Path.DirectorySeparatorChar, name);
        Assert.DoesNotContain(Path.AltDirectorySeparatorChar, name);
    }

    [Theory]
    [Trait("Scenario", "ForeignFilesAreNotOurs")]
    [InlineData("notes.txt")]
    [InlineData("readarr_backup_v1.0.0_2026.03.04_05.06.07.zip")]
    [InlineData("listenarr_backup.zip")]
    [InlineData("my_listenarr_backup_v1.3.4_2026.03.04_05.06.07.zip")]
    [InlineData("listenarr_backup_v1.3.4_2026.03.04_05.06.07.zip.bak")]
    public void IsBackupArchive_RejectsAnythingThisCodeDidNotWrite(string fileName)
    {
        // Given a file that is not one of ours
        // When it is offered to the matcher
        // Then it is refused, because the retention sweep deletes what this returns true for
        Assert.False(BackupArchiveNaming.IsBackupArchive(fileName));
    }
}
