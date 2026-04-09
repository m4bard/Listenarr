using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Listenarr.Api.Services;
using Listenarr.Domain.Utils;

namespace Listenarr.Api.Tests
{
    public class LegacyOutputPathMigratorTests
    {
        private string booksPath = FileUtils.GetAbsolutePath("books");
        private string otherPath = FileUtils.GetAbsolutePath("other");

        [Fact]
        public async Task Migrate_CreatesRoot_WhenNoExistingAndOutputPathPresent()
        {
            var mockConfig = new Mock<IConfigurationService>();
            mockConfig.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(new Listenarr.Domain.Models.ApplicationSettings { OutputPath = booksPath });

            var mockRootService = new Mock<IRootFolderService>();
            mockRootService.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<RootFolder>());
            mockRootService.Setup(r => r.CreateAsync(It.IsAny<RootFolder>())).ReturnsAsync((RootFolder r) => r);

            var migrator = new LegacyOutputPathMigrator(mockConfig.Object, mockRootService.Object, new NullLogger<LegacyOutputPathMigrator>());

            await migrator.MigrateAsync();

            mockRootService.Verify(r => r.CreateAsync(It.Is<RootFolder>(rf => rf.Name == "Default" && rf.Path == booksPath && rf.IsDefault)), Times.Once);
        }

        [Fact]
        public async Task Migrate_DoesNotCreate_WhenRootsExist()
        {
            var mockConfig = new Mock<IConfigurationService>();
            mockConfig.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(new Listenarr.Domain.Models.ApplicationSettings { OutputPath = booksPath });

            var mockRootService = new Mock<IRootFolderService>();
            mockRootService.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<RootFolder> { new RootFolder { Name = "X", Path = otherPath } });

            var migrator = new LegacyOutputPathMigrator(mockConfig.Object, mockRootService.Object, new NullLogger<LegacyOutputPathMigrator>());

            await migrator.MigrateAsync();

            mockRootService.Verify(r => r.CreateAsync(It.IsAny<RootFolder>()), Times.Never);
        }

        [Fact]
        public async Task Migrate_DoesNotCreate_WhenOutputPathEmpty()
        {
            var mockConfig = new Mock<IConfigurationService>();
            mockConfig.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(new Listenarr.Domain.Models.ApplicationSettings { OutputPath = "" });

            var mockRootService = new Mock<IRootFolderService>();
            mockRootService.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<RootFolder>());

            var migrator = new LegacyOutputPathMigrator(mockConfig.Object, mockRootService.Object, new NullLogger<LegacyOutputPathMigrator>());

            await migrator.MigrateAsync();

            mockRootService.Verify(r => r.CreateAsync(It.IsAny<RootFolder>()), Times.Never);
        }
    }
}