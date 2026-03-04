using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Listenarr.Api.Models;
using Listenarr.Domain.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class IndexersControllerProwlarrImportTests
    {
        private sealed class CaptureHandler : HttpMessageHandler
        {
            public HttpRequestMessage? LastRequest { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                });
            }
        }

        private static Listenarr.Api.Controllers.IndexersController CreateController(CaptureHandler handler)
        {
            var options = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase($"prowlarr-import-{Guid.NewGuid()}")
                .Options;

            var db = new ListenArrDbContext(options);
            var logger = new LoggerFactory().CreateLogger<Listenarr.Api.Controllers.IndexersController>();
            var client = new HttpClient(handler);
            return new Listenarr.Api.Controllers.IndexersController(db, logger, client);
        }

        [Fact]
        public async Task ImportFromProwlarr_AcceptsEmbeddedPortInHostField_WhenSchemeOmitted()
        {
            var handler = new CaptureHandler();
            var controller = CreateController(handler);

            var result = await controller.ImportFromProwlarr(new ProwlarrImportRequest
            {
                Url = "192.168.1.10:4545",
                ApiKey = "test-key"
            });

            Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(handler.LastRequest);
            Assert.Equal("http://192.168.1.10:4545/api/v1/indexer", handler.LastRequest!.RequestUri!.ToString());
        }

        [Fact]
        public async Task ImportFromProwlarr_BuildsFromHostAndSeparatePortField()
        {
            var handler = new CaptureHandler();
            var controller = CreateController(handler);

            var result = await controller.ImportFromProwlarr(new ProwlarrImportRequest
            {
                Url = "192.168.1.10",
                Port = 4545,
                ApiKey = "test-key"
            });

            Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(handler.LastRequest);
            Assert.Equal("http://192.168.1.10:4545/api/v1/indexer", handler.LastRequest!.RequestUri!.ToString());
        }

        [Fact]
        public async Task ImportFromProwlarr_HonorsExplicitHttpsScheme_WhenProvided()
        {
            var handler = new CaptureHandler();
            var controller = CreateController(handler);

            var result = await controller.ImportFromProwlarr(new ProwlarrImportRequest
            {
                Url = "https://192.168.1.10",
                Port = 4545,
                ApiKey = "test-key"
            });

            Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(handler.LastRequest);
            Assert.Equal("https://192.168.1.10:4545/api/v1/indexer", handler.LastRequest!.RequestUri!.ToString());
        }
    }
}
