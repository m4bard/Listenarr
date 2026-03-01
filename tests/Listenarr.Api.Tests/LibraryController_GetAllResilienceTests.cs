using System.Net;
using System.Threading.Tasks;
using Asp.Versioning.ApiExplorer;
using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Listenarr.Api.Tests
{
    public class LibraryController_GetAllResilienceTests : IClassFixture<ListenarrWebApplicationFactory>
    {
        private readonly ListenarrWebApplicationFactory _factory;

        public LibraryController_GetAllResilienceTests(ListenarrWebApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task GetAll_DoesNotFail_WhenLegacyIsbnTextExists()
        {
            var apiBasePath = ResolveApiBasePath(_factory.Services);

            int id;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ListenArrDbContext>();
                var audiobook = new Audiobook
                {
                    Title = "Legacy ISBN",
                    Monitored = true
                };
                db.Audiobooks.Add(audiobook);
                await db.SaveChangesAsync();
                id = audiobook.Id;

                // Simulate pre-migration legacy TEXT value instead of JSON array.
                await db.Database.ExecuteSqlRawAsync(
                    "UPDATE Audiobooks SET Isbn = '9780306406157' WHERE Id = {0}",
                    id);
            }

            var client = _factory.CreateClient();
            var response = await client.GetAsync($"{apiBasePath}/library");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        private static string ResolveApiBasePath(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var provider = scope.ServiceProvider.GetService<IApiVersionDescriptionProvider>();
            var groupName = provider?.ApiVersionDescriptions.FirstOrDefault(d => !d.IsDeprecated)?.GroupName
                ?? provider?.ApiVersionDescriptions.FirstOrDefault()?.GroupName;

            return string.IsNullOrWhiteSpace(groupName) ? "/api/v1" : $"/api/{groupName}";
        }
    }
}
