using Listenarr.Infrastructure.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Listenarr.Infrastructure.Migrations
{
    [DbContext(typeof(ListenArrDbContext))]
    [Migration("20260319101500_RenameAudimetaMetadataSourceToAudible")]
    public partial class RenameAudimetaMetadataSourceToAudible : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ApiConfigurations
                SET Name = 'Audible',
                    BaseUrl = 'https://api.audible.com'
                WHERE Type = 'metadata'
                  AND (
                    Name = 'Audimeta'
                    OR BaseUrl LIKE '%audimeta.de%'
                  );
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ApiConfigurations
                SET Name = 'Audimeta',
                    BaseUrl = 'https://audimeta.de'
                WHERE Type = 'metadata'
                  AND (
                    Name = 'Audible'
                    OR BaseUrl LIKE '%api.audible%'
                  );
            ");
        }
    }
}
