using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Listenarr.Api.Services;
using Listenarr.Domain.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace Listenarr.Api.Tests
{
    public class ListenarrWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Use "Test" environment (matches GitHub Actions ASPNETCORE_ENVIRONMENT=Test)
            builder.UseEnvironment("Test");

            builder.ConfigureAppConfiguration((context, config) =>
            {
                var dbPath = Path.Combine(Path.GetTempPath(), "listenarr-tests", $"listenarr-{Guid.NewGuid():N}.db");
                var dbDir = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrWhiteSpace(dbDir) && !Directory.Exists(dbDir))
                {
                    Directory.CreateDirectory(dbDir);
                }

                var overrides = new Dictionary<string, string?>
                {
                    ["Listenarr:SqliteDbPath"] = dbPath,
                    ["Playwright:Enabled"] = "false",
                    ["Listenarr:DisableHostedServices"] = "true"
                };

                config.AddInMemoryCollection(overrides);
            });

            builder.ConfigureServices(services =>
            {
                // Keep API integration tests deterministic regardless of a local config.json
                // in the repo/environment. Individual tests can still override this with
                // WithWebHostBuilder(...ConfigureServices(...)).
                services.RemoveAll<IStartupConfigService>();
                services.AddSingleton<IStartupConfigService>(_ =>
                {
                    var mock = new Mock<IStartupConfigService>();
                    mock.Setup(s => s.GetConfig()).Returns(new StartupConfig { AuthenticationRequired = "false" });
                    mock.Setup(s => s.ReloadAsync()).Returns(Task.CompletedTask);
                    mock.Setup(s => s.SaveAsync(It.IsAny<StartupConfig>())).Returns(Task.CompletedTask);
                    return mock.Object;
                });

                services.RemoveAll<IFfmpegService>();
                services.AddSingleton<IFfmpegService>(_ =>
                {
                    var ffmpegMock = new Mock<IFfmpegService>();
                    var mockPath = Path.Combine(Path.GetTempPath(), "mock-ffprobe");
                    ffmpegMock.Setup(f => f.GetFfprobePathAsync(It.IsAny<bool>()))
                        .ReturnsAsync(mockPath);
                    ffmpegMock.Setup(f => f.EnsureFfprobeInstalledAsync())
                        .ReturnsAsync(mockPath);
                    return ffmpegMock.Object;
                });
            });
        }
    }
}
