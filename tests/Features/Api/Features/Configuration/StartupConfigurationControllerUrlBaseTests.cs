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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace Listenarr.Tests.Features.Api.Features.Configuration;

[Trait("Name", "StartupConfigurationControllerUrlBaseTests")]
[Trait("Category", "Api")]
public sealed class StartupConfigurationControllerUrlBaseTests : BaseTests
{
    private static JsonElement Body(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/listenarr")]
    [InlineData("listenarr")]
    [InlineData("/listenarr/audiobooks")]
    [InlineData("/https-is-not-a-scheme-here")]
    public void UrlBaseThatIsAPath_IsAccepted(string? urlBase)
    {
        Assert.True(StartupConfigurationController.IsValidUrlBase(urlBase));
    }

    [Theory]
    [InlineData("https://listenarr.example.com")]
    [InlineData("http://listenarr.example.com/listenarr")]
    [InlineData("HTTPS://LISTENARR.EXAMPLE.COM")]
    [InlineData("/https://listenarr.example.com")]
    [InlineData("  https://listenarr.example.com  ")]
    public void UrlBaseThatIsAFullUrl_IsRejected(string urlBase)
    {
        Assert.False(StartupConfigurationController.IsValidUrlBase(urlBase));
    }

    [Fact]
    public async Task SaveStartupConfig_PartialBodyWithAFullUrl_ReturnsBadRequest_AndSavesNothing()
    {
        // The value would be persisted and then silently ignored at startup, which reads as a
        // proxy fault rather than a rejected setting. The *arr projects refuse it here.
        var configurationService = OnDiskConfiguration();
        var controller = BuildController(configurationService);

        var result = await controller.SaveStartupConfig(
            Body("""{"urlBase":"https://listenarr.example.com"}"""));

        AssertRejectedForUrlBase(result, configurationService);
    }

    [Fact]
    public async Task SaveStartupConfig_PascalCaseBodyWithAFullUrl_ReturnsBadRequest()
    {
        // config.json is written in PascalCase and the SPA posts camelCase, so the check has
        // to read the body under the same case rules the merge does.
        var configurationService = OnDiskConfiguration();
        var controller = BuildController(configurationService);

        var result = await controller.SaveStartupConfig(
            Body("""{"UrlBase":"https://listenarr.example.com"}"""));

        AssertRejectedForUrlBase(result, configurationService);
    }

    [Fact]
    public async Task SaveStartupConfig_FullBodyWithAFullUrl_ReturnsBadRequest_AndSavesNothing()
    {
        var configurationService = OnDiskConfiguration();
        var controller = BuildController(configurationService);

        var result = await controller.SaveStartupConfig(
            JsonSerializer.SerializeToElement(new StartupConfig
            {
                ApiKey = "posted-api-key",
                Port = 8686,
                UrlBase = "https://listenarr.example.com",
            }));

        AssertRejectedForUrlBase(result, configurationService);
    }

    [Fact]
    public async Task SaveStartupConfig_BodyThatNeverMentionsUrlBase_IsMergedOverAStoredFullUrl()
    {
        // The control for the tests above. A body about the port is not a body about the
        // UrlBase, so a bad value already in config.json must not turn every other setting
        // into a 400. Validating the merged config instead of the posted one would fail here.
        StartupConfig? saved = null;
        var configurationService = new Mock<IConfigurationService>();
        configurationService
            .Setup(service => service.GetStartupConfigAsync())
            .ReturnsAsync(() => saved ?? new StartupConfig
            {
                ApiKey = "on-disk-api-key",
                Port = 5000,
                UrlBase = "https://listenarr.example.com",
            });
        configurationService
            .Setup(service => service.SaveStartupConfigAsync(It.IsAny<StartupConfig>()))
            .Callback<StartupConfig>(config => saved = config)
            .Returns(Task.CompletedTask);
        var controller = BuildController(configurationService);

        var result = await controller.SaveStartupConfig(Body("""{"port":8686}"""));

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(saved);
        Assert.Equal(8686, saved!.Port);
        Assert.Equal("https://listenarr.example.com", saved.UrlBase);
        Assert.Equal("on-disk-api-key", saved.ApiKey);
    }

    [Fact]
    public async Task SaveStartupConfig_SavesAPathUrlBase()
    {
        var configurationService = new Mock<IConfigurationService>();
        configurationService
            .Setup(service => service.GetStartupConfigAsync())
            .ReturnsAsync(new StartupConfig { UrlBase = "/listenarr" });
        var controller = BuildController(configurationService);

        var result = await controller.SaveStartupConfig(Body("""{"urlBase":"/listenarr"}"""));

        Assert.IsType<OkObjectResult>(result.Result);
        configurationService.Verify(
            service => service.SaveStartupConfigAsync(It.Is<StartupConfig>(c => c.UrlBase == "/listenarr")),
            Times.Once);
    }

    [Fact]
    public async Task SaveStartupConfig_ClearingUrlBaseIsAccepted()
    {
        // Sending it empty or null is how the merge is told to clear a property, and the
        // empty value is one the rule accepts, so neither spelling may be refused.
        var configurationService = new Mock<IConfigurationService>();
        configurationService
            .Setup(service => service.GetStartupConfigAsync())
            .ReturnsAsync(new StartupConfig { UrlBase = "/listenarr" });
        var controller = BuildController(configurationService);

        Assert.IsType<OkObjectResult>(
            (await controller.SaveStartupConfig(Body("""{"urlBase":""}"""))).Result);
        Assert.IsType<OkObjectResult>(
            (await controller.SaveStartupConfig(Body("""{"urlBase":null}"""))).Result);
    }

    private static void AssertRejectedForUrlBase(
        ActionResult<StartupConfig> result,
        Mock<IConfigurationService> configurationService)
    {
        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains(
            StartupConfigurationController.InvalidUrlBaseMessage,
            badRequest.Value!.ToString(),
            StringComparison.Ordinal);
        configurationService.Verify(
            service => service.SaveStartupConfigAsync(It.IsAny<StartupConfig>()),
            Times.Never);
    }

    private static Mock<IConfigurationService> OnDiskConfiguration()
    {
        var configurationService = new Mock<IConfigurationService>();
        configurationService
            .Setup(service => service.GetStartupConfigAsync())
            .ReturnsAsync(new StartupConfig { ApiKey = "on-disk-api-key", UrlBase = "/listenarr" });
        return configurationService;
    }

    private static StartupConfigurationController BuildController(Mock<IConfigurationService> configurationService)
    {
        var startupConfigService = new Mock<IStartupConfigService>();
        startupConfigService
            .Setup(service => service.NormalizeApiVersion(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns("v1");

        return new StartupConfigurationController(
            configurationService.Object,
            startupConfigService.Object,
            NullLogger<StartupConfigurationController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }
}
