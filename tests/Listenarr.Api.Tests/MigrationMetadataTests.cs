using System.Reflection;
using Listenarr.Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class MigrationMetadataTests
    {
        [Fact]
        public void AddImportBlacklistExtensionsMigration_IsDiscoverableByEf()
        {
            var attribute = typeof(AddImportBlacklistExtensionsToApplicationSettings)
                .GetCustomAttribute<MigrationAttribute>();

            Assert.NotNull(attribute);
            Assert.Equal("20260317123000_AddImportBlacklistExtensionsToApplicationSettings", attribute!.Id);
        }
    }
}
