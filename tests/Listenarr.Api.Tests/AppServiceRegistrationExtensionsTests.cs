using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Listenarr.Api.Extensions;
using Listenarr.Api.Services;

namespace Listenarr.Api.Tests
{
    public class AppServiceRegistrationExtensionsTests
    {
        [Fact]
        public void AddListenarrAppServices_RegistersImageCacheServiceAsSingleton()
        {
            var services = new ServiceCollection();
            var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

            services.AddLogging();
            services.AddHttpClient();
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>(
                new StubWebHostEnvironment());

            services.AddListenarrAppServices(config);

            Assert.Contains(
                services,
                descriptor =>
                    descriptor.ServiceType == typeof(IImageCacheService) &&
                    descriptor.Lifetime == ServiceLifetime.Singleton);
        }

        private sealed class StubWebHostEnvironment : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
        {
            public string ApplicationName { get; set; } = "Listenarr.Api.Tests";
            public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } =
                new Microsoft.Extensions.FileProviders.NullFileProvider();
            public string WebRootPath { get; set; } = string.Empty;
            public string EnvironmentName { get; set; } = "Development";
            public string ContentRootPath { get; set; } = System.IO.Directory.GetCurrentDirectory();
            public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
                new Microsoft.Extensions.FileProviders.NullFileProvider();
        }
    }
}
