using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Listenarr.Api.Services;
using Listenarr.Application.Repositories;
using Listenarr.Infrastructure.Models;
using Listenarr.Infrastructure.Repositories;
using Listenarr.Domain.Models;

namespace Listenarr.Api.Tests
{
    public class MoveQueueServiceTests
    {
        [Fact]
        public async Task UpdateJobStatus_PersistsAndUpdatesInMemory()
        {
            var dbOpts = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase("test_db_movejob_" + Guid.NewGuid().ToString("N"))
                .Options;
            var db = new ListenArrDbContext(dbOpts);

            var services = new ServiceCollection();
            services.AddSingleton(db);
            services.AddScoped<IMoveJobRepository>(_ => new EfMoveJobRepository(db));
            var provider = services.BuildServiceProvider();
            var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
            var logger = new NullLogger<MoveQueueService>();

            var svc = new MoveQueueService(logger, scopeFactory);

            // Enqueue a job (creates DB entry)
            var jobId = await svc.EnqueueMoveAsync(1, "C:\\dest\\path", "C:\\src\\path");

            // Initially the job should be queued
            Assert.True(svc.TryGetJob(jobId, out var job1));
            Assert.Equal("Queued", job1!.Status);

            // Update status to Processing
            svc.UpdateJobStatus(jobId, "Processing", null);
            Assert.True(svc.TryGetJob(jobId, out var job2));
            Assert.Equal("Processing", job2!.Status);

            // Verify persisted in DB
            using (var scope = scopeFactory.CreateScope())
            {
                var verifyDb = scope.ServiceProvider.GetRequiredService<ListenArrDbContext>();
                var dbJob = await verifyDb.MoveJobs.FindAsync(jobId);
                Assert.NotNull(dbJob);
                Assert.Equal("Processing", dbJob!.Status);
            }
        }
    }
}
