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
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Gives QualityProfiles an explicit UpgradeAllowed flag and carries the meaning of every
    /// stored profile across to it.
    /// </summary>
    /// <remarks>
    /// A blank CutoffQuality was the only way to record "do not upgrade this", so that is what the
    /// flag is derived from. The column arrives defaulted to 0, which is already correct for a
    /// blank cutoff, and the backfill turns it on for every profile that names one. Reversing
    /// those two would switch upgrades on for everybody who had them off and change what their
    /// installs download, with nothing in the UI to show it had happened.
    ///
    /// Down blanks the cutoff of any profile whose upgrades were off before dropping the column,
    /// because on the older schema that blank is the only place the intent can live.
    /// </remarks>
    public partial class AddQualityProfileUpgradeAllowed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UpgradeAllowed",
                table: "QualityProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE "QualityProfiles"
                SET "UpgradeAllowed" = 1
                WHERE "CutoffQuality" IS NOT NULL AND trim("CutoffQuality") <> '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "QualityProfiles"
                SET "CutoffQuality" = NULL
                WHERE "UpgradeAllowed" = 0;
                """);

            migrationBuilder.DropColumn(
                name: "UpgradeAllowed",
                table: "QualityProfiles");
        }
    }
}
