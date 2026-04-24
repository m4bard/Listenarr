using System.Net;
using System.Text;
using Listenarr.Api.Hubs;
using Listenarr.Api.Services;
using Listenarr.Api.Services.Metadata;
using Listenarr.Infrastructure.Extensions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Listenarr.Api.Tests
{
    public abstract class MockUtils
    {
        /// <summary>
        /// Returns a 200 HTTP reply with the given JSON as content
        /// </summary>
        /// <param name="json">JSON structure to use as a response body</param>
        /// <returns>HttpResponseMessage with status 200 and JSON body</returns>
        public static HttpResponseMessage GetCannedResponse(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        public static Mock<DownloadMonitorService> GetDownloadMonitorServiceMock()
        {
            var scopeFactoryMock = new Mock<IServiceScopeFactory>();
            var scopeMock = new Mock<IServiceScope>();
            var serviceProviderMock = new Mock<IServiceProvider>();

            scopeFactoryMock.Setup(x => x.CreateScope()).Returns(scopeMock.Object);
            scopeMock.Setup(x => x.ServiceProvider).Returns(serviceProviderMock.Object);

            return new Mock<DownloadMonitorService>(
                scopeFactoryMock.Object,
                new Mock<IHubContext<DownloadHub>>().Object,
                new Mock<ILogger<DownloadMonitorService>>().Object,
                new Mock<IHttpClientFactory>().Object,
                new Mock<IAppMetricsService>().Object);
        }

        public static ServiceCollection InitServiceCollection()
        {
            var configMock = new Mock<IConfigurationService>();
            configMock.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(new ApplicationSettings
            {
                OutputPath = "",
                CompletedFileAction = "Copy",
                EnableMetadataProcessing = false,
                MultiFileNamingPattern = "{Title}-{DiskNumber:00}-{ChapterNumber:00}",
            });
            
            var startupConfigServiceMock = new Mock<IStartupConfigService>();
            startupConfigServiceMock.Setup(s => s.GetConfig()).Returns(new StartupConfig { AuthenticationRequired = "false" });

            var importItemResolutionServiceMock = new Mock<IImportItemResolutionService>();
            importItemResolutionServiceMock
                .Setup(r => r.ResolveImportItemAsync(
                    It.IsAny<Download>(),
                    It.IsAny<QueueItem>(),
                    It.IsAny<QueueItem?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((Download download, QueueItem queueItem, QueueItem? previousAttempt, CancellationToken ct) =>
                {
                    return queueItem;
                });

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(configMock.Object);
            services.AddSingleton(startupConfigServiceMock.Object);
            services.AddSingleton(importItemResolutionServiceMock.Object);
            services.AddSingleton(new Mock<IAppMetricsService>().Object);
            services.AddSingleton(new Mock<IDownloadService>().Object);
            services.AddSingleton(new Mock<IFfmpegService>().Object);
            services.AddSingleton(new Mock<HttpClient>().Object);
            services.AddSingleton(new Mock<IHubContext<DownloadHub>>().Object);
            services.AddSingleton<IProcessRunner, SystemProcessRunner>();
            services.AddMemoryCache();
            services.AddScoped<IFileNamingService, FileNamingService>();
            services.AddScoped<IArchiveExtractor, ArchiveExtractor>();
            services.AddScoped<IMetadataService, MetadataService>();
            services.AddScoped<IRemotePathMappingService, RemotePathMappingService>();
            services.AddScoped<IDownloadProcessingQueueService, DownloadProcessingQueueService>();
            services.AddListenarrInfrastructure(options =>
                options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));
            
            return services;
        }
        
        public static ServiceProvider CreateServiceProvider(string outputPath = "")
        {
            return CreateServiceProvider(new Mock<IImportItemResolutionService>().Object, outputPath);
        }
        
        public static ServiceProvider CreateServiceProvider(IImportItemResolutionService importItemResolutionService, string outputPath = "", DownloadClientConfiguration downloadClientConfiguration = null)
        {
            var services = InitServiceCollection();
            var metricsMock = new Mock<IAppMetricsService>();

            var configMock = new Mock<IConfigurationService>();
            configMock.Setup(c => c.GetApplicationSettingsAsync()).ReturnsAsync(new ApplicationSettings
            {
                OutputPath = outputPath,
                CompletedFileAction = "Copy",
                EnableMetadataProcessing = false,
                MultiFileNamingPattern = "{Title}-{DiskNumber:00}-{ChapterNumber:00}",
            });
            if (downloadClientConfiguration != null)
            {
                configMock.Setup(c => c.GetDownloadClientConfigurationsAsync()).ReturnsAsync([
                    downloadClientConfiguration
                ]);
                configMock.Setup(c => c.GetDownloadClientConfigurationAsync(It.IsAny<string>())).ReturnsAsync(downloadClientConfiguration);
            }
            
            services.AddSingleton(configMock.Object);
            services.AddSingleton(importItemResolutionService);
            
            return services.BuildServiceProvider();
        }

        public static async Task<DownloadProcessingJob> CreateDownloadProcessingJob(ServiceProvider provider, Download download, string sourcePath)
        {
            var queueService = provider.GetRequiredService<IDownloadProcessingQueueService>();
            var jobId = await queueService.QueueDownloadProcessingAsync(download.Id, sourcePath, null);
            var job = await queueService.GetJobAsync(jobId);

            // Set job to processing (the outer loop normally does this)
            job.Status = ProcessingJobStatus.Processing;
            job.StartedAt = DateTime.UtcNow;
            await queueService.UpdateJobAsync(job);

            return job;
        }

        public static CompletedDownloadProcessor CreateCompletedDownloadProcessor(ServiceProvider provider)
        {
            var queueMock = new Mock<IDownloadQueueService>();
            queueMock.Setup(q => q.GetQueueAsync()).ReturnsAsync([]);

            var serviceScopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            
            var importService = new ImportService(
                provider.GetRequiredService<IAudiobookRepository>(),
                serviceScopeFactory,
                provider.GetRequiredService<IFileNamingService>(),
                provider.GetRequiredService<IMetadataService>());
            
            var fileFinalizer = new FileFinalizer(
                importService,
                provider.GetRequiredService<IDownloadRepository>(),
                serviceScopeFactory,
                new Mock<ILogger<FileFinalizer>>().Object);
            
            return new CompletedDownloadProcessor(
                provider.GetRequiredService<IDownloadRepository>(),
                fileFinalizer,
                provider.GetRequiredService<IConfigurationService>(),
                serviceScopeFactory,
                importService,
                provider.GetRequiredService<IArchiveExtractor>(),
                queueMock.Object,
                provider.GetRequiredService<IHubContext<DownloadHub>>(),
                new Mock<ILogger<CompletedDownloadProcessor>>().Object);
        }
    }
}
