using Listenarr.Api.Controllers;
using Listenarr.Api.Hubs;
using Listenarr.Api.Services;
using Listenarr.Domain.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class ConfigurationControllerSettingsTests
    {
        [Fact]
        public async Task GetApplicationSettings_DoesNotReturnEncryptedProwlarrApiKey()
        {
            var configurationService = new Mock<IConfigurationService>(MockBehavior.Strict);
            configurationService
                .Setup(x => x.GetApplicationSettingsAsync())
                .ReturnsAsync(new ApplicationSettings
                {
                    Id = 1,
                    ProwlarrUrl = "http://localhost",
                    ProwlarrPort = 9696,
                    ProwlarrApiKeyEncrypted = "ciphertext",
                    ProwlarrTagFilter = "audiobooks"
                });

            var controller = new ConfigurationController(
                configurationService.Object,
                NullLogger<ConfigurationController>.Instance,
                Mock.Of<IUserService>(),
                Mock.Of<IHubContext<SettingsHub>>(),
                Mock.Of<IDownloadService>(),
                null!);

            var result = await controller.GetApplicationSettings();
            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var settings = Assert.IsType<ApplicationSettings>(ok.Value);

            Assert.Equal("http://localhost", settings.ProwlarrUrl);
            Assert.Equal(9696, settings.ProwlarrPort);
            Assert.Equal("audiobooks", settings.ProwlarrTagFilter);
            Assert.Null(settings.ProwlarrApiKeyEncrypted);

            configurationService.Verify(x => x.GetApplicationSettingsAsync(), Times.Once);
            configurationService.VerifyNoOtherCalls();
        }
    }
}
