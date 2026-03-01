using System.Net.Http;
using System.Reflection;
using Listenarr.Api.Hubs;
using Listenarr.Api.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class DownloadMonitorServiceTests
    {
        private static ListenArrDbContext CreateInMemoryDb()
        {
            var options = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new ListenArrDbContext(options);
        }

        [Fact]
        public async Task MonitorDownloadsAsync_SkipsClientPolling_WhenNoEnabledDownloadClientsConfigured()
        {
            using var db = CreateInMemoryDb();

            db.Downloads.Add(new Download
            {
                Id = "dl-no-clients",
                Title = "No Clients Configured",
                Status = DownloadStatus.Downloading,
                DownloadClientId = "missing-client-id",
                StartedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var configMock = new Mock<IConfigurationService>();
            configMock.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(new ApplicationSettings());
            configMock.Setup(c => c.GetDownloadClientConfigurationsAsync()).ReturnsAsync(new List<DownloadClientConfiguration>());
            configMock.Setup(c => c.GetDownloadClientConfigurationAsync(It.IsAny<string>())).ReturnsAsync((DownloadClientConfiguration?)null);

            var services = new ServiceCollection();
            services.AddSingleton(db);
            services.AddSingleton<IConfigurationService>(configMock.Object);
            var serviceProvider = services.BuildServiceProvider();
            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

            var clientProxyMock = new Mock<IClientProxy>();
            clientProxyMock
                .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var hubClientsMock = new Mock<IHubClients>();
            hubClientsMock.SetupGet(h => h.All).Returns(clientProxyMock.Object);

            var hubContextMock = new Mock<IHubContext<DownloadHub>>();
            hubContextMock.SetupGet(h => h.Clients).Returns(hubClientsMock.Object);

            var loggerMock = new Mock<ILogger<DownloadMonitorService>>();
            var httpFactoryMock = new Mock<IHttpClientFactory>();
            using var httpClient = new HttpClient(new HttpClientHandler());
            httpFactoryMock
                .Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(httpClient);

            var monitor = new DownloadMonitorService(
                scopeFactory,
                hubContextMock.Object,
                loggerMock.Object,
                httpFactoryMock.Object);

            var method = typeof(DownloadMonitorService).GetMethod("MonitorDownloadsAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var task = (Task?)method!.Invoke(monitor, new object[] { CancellationToken.None });
            Assert.NotNull(task);
            await task!;

            configMock.Verify(c => c.GetDownloadClientConfigurationsAsync(), Times.Once);
            configMock.Verify(c => c.GetDownloadClientConfigurationAsync(It.IsAny<string>()), Times.Never);
        }
    }
}
