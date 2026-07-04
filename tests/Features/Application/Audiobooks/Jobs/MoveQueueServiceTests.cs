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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Listenarr.Tests.Mocks;

namespace Listenarr.Tests.Features.Application.Audiobooks.Jobs
{
    public class MoveQueueServiceTests
    {
        private const string LeaseOwner = "test-worker";
        [Fact]
        public async Task UpdateJobStatus_ExhaustedPersistenceRetries_PropagatesWithoutBroadcasting()
        {
            var job = new MoveJob { Id = Guid.NewGuid(), AudiobookId = 42, LeaseOwner = LeaseOwner, LeaseGeneration = 3 };
            var persistence = new Mock<IMoveQueuePersistence>();
            persistence.Setup(store => store.GetByIdAsync(job.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(job);
            persistence.Setup(store => store.UpdateStatusAsync(
                    job.Id,
                    LeaseOwner,
                    job.LeaseGeneration,
                    MoveJobStatus.Completed,
                    It.IsAny<MoveJobPhase>(),
                    null,
                    MoveFailureKind.None,
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new PersistenceException(
                    "Status write failed.",
                    new InvalidOperationException("Database unavailable.")));
            var broadcaster = new Mock<IHubBroadcaster>();
            var service = new MoveQueueService(
                NullLogger<MoveQueueService>.Instance,
                persistence.Object,
                broadcaster.Object,
                TimeProvider.System);

            await Assert.ThrowsAsync<PersistenceException>(() => service.UpdateJobStatusAsync(
                job.Id,
                LeaseOwner,
                job.LeaseGeneration,
                MoveJobStatus.Completed));

            persistence.Verify(store => store.UpdateStatusAsync(
                job.Id,
                LeaseOwner,
                job.LeaseGeneration,
                MoveJobStatus.Completed,
                It.IsAny<MoveJobPhase>(),
                null,
                MoveFailureKind.None,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()), Times.Exactly(3));
            broadcaster.Verify(service => service.BroadcastAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task UpdateJobStatus_PostCommitRelocationFailure_RemainsSuccessfulAndBroadcasts()
        {
            var job = new MoveJob { Id = Guid.NewGuid(), AudiobookId = 42, LeaseOwner = LeaseOwner, LeaseGeneration = 3 };
            var persistence = new Mock<IMoveQueuePersistence>();
            persistence.Setup(store => store.GetByIdAsync(job.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(job);
            persistence.Setup(store => store.UpdateStatusAsync(
                    job.Id,
                    LeaseOwner,
                    job.LeaseGeneration,
                    MoveJobStatus.Completed,
                    It.IsAny<MoveJobPhase>(),
                    null,
                    MoveFailureKind.None,
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            var relocation = new Mock<IRootFolderRelocationService>();
            relocation.Setup(service => service.OnMoveJobStateChangedAsync(
                    job.Id,
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new PersistenceException(
                    "Relocation reconciliation failed.",
                    new InvalidOperationException("Database unavailable.")));
            var broadcaster = new Mock<IHubBroadcaster>();
            broadcaster.Setup(service => service.BroadcastAsync(
                    "MoveJobUpdate",
                    It.IsAny<object>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var service = new MoveQueueService(
                NullLogger<MoveQueueService>.Instance,
                persistence.Object,
                broadcaster.Object,
                TimeProvider.System,
                relocation.Object);

            await service.UpdateJobStatusAsync(
                job.Id,
                LeaseOwner,
                job.LeaseGeneration,
                MoveJobStatus.Completed);

            broadcaster.Verify(service => service.BroadcastAsync(
                "MoveJobUpdate",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpdateJobStatus_PersistsAndUpdatesInMemory()
        {
            var dbOpts = new DbContextOptionsBuilder<ListenArrDbContext>()
                .UseInMemoryDatabase("test_db_movejob_" + Guid.NewGuid().ToString("N"))
                .Options;
            var db = new ListenArrDbContext(dbOpts);

            var logger = new NullLogger<MoveQueueService>();
            var persistence = new Mock<IMoveQueuePersistence>();
            persistence.Setup(store => store.GetActiveByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string key, CancellationToken _) =>
                    db.MoveJobs.SingleOrDefault(job => job.ActiveDeduplicationKey == key));
            persistence.Setup(store => store.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => db.MoveJobs.Find(id));
            persistence.Setup(store => store.AddAsync(It.IsAny<MoveJob>(), It.IsAny<CancellationToken>()))
                .Returns(async (MoveJob job, CancellationToken ct) =>
                {
                    db.MoveJobs.Add(job);
                    await db.SaveChangesAsync(ct);
                });
            persistence.Setup(store => store.UpdateStatusAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<MoveJobStatus>(),
                    It.IsAny<MoveJobPhase>(),
                    It.IsAny<string?>(),
                    It.IsAny<MoveFailureKind>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async (Guid id, string _, int _, MoveJobStatus status, MoveJobPhase phase, string? error, MoveFailureKind failureKind, DateTimeOffset updatedAt, CancellationToken ct) =>
                {
                    var persisted = await db.MoveJobs.FindAsync([id], ct);
                    if (persisted == null) return false;
                    persisted.Status = status;
                    persisted.Phase = phase;
                    persisted.Error = error;
                    persisted.FailureKind = failureKind;
                    persisted.UpdatedAt = updatedAt.UtcDateTime;
                    await db.SaveChangesAsync(ct);
                    return true;
                });

            var svc = new MoveQueueService(
                logger,
                persistence.Object,
                new NoopHubBroadcaster(),
                TimeProvider.System);

            // Enqueue a job (creates DB entry)
            var jobId = await svc.EnqueueMoveAsync(1, "C:\\dest\\path", "C:\\src\\path");

            // Initially the job should be queued
            var job1 = await svc.GetJobAsync(jobId);
            Assert.NotNull(job1);
            Assert.Equal(MoveJobStatus.Queued, job1!.Status);

            // Update status to Processing
            await svc.UpdateJobStatusAsync(jobId, LeaseOwner, 0, MoveJobStatus.Running);
            var job2 = await svc.GetJobAsync(jobId);
            Assert.NotNull(job2);
            Assert.Equal(MoveJobStatus.Running, job2!.Status);

            // Verify persisted in DB
            var dbJob = await db.MoveJobs.FindAsync(jobId);
            Assert.NotNull(dbJob);
            Assert.Equal(MoveJobStatus.Running, dbJob!.Status);
        }

        [Fact]
        public async Task EnqueueMoveAsync_ConcurrentDuplicates_ReturnSingleJob()
        {
            var jobs = new List<MoveJob>();
            var sync = new object();
            var persistence = new Mock<IMoveQueuePersistence>();
            persistence.Setup(store => store.GetActiveByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string key, CancellationToken _) =>
                {
                    lock (sync)
                    {
                        return jobs.SingleOrDefault(job => job.ActiveDeduplicationKey == key);
                    }
                });
            persistence.Setup(store => store.AddAsync(It.IsAny<MoveJob>(), It.IsAny<CancellationToken>()))
                .Returns((MoveJob job, CancellationToken _) =>
                {
                    lock (sync)
                    {
                        jobs.Add(job);
                    }

                    return Task.CompletedTask;
                });
            var service = new MoveQueueService(
                NullLogger<MoveQueueService>.Instance,
                persistence.Object,
                new NoopHubBroadcaster(),
                TimeProvider.System);

            var ids = await Task.WhenAll(
                Enumerable.Range(0, 16)
                    .Select(_ => service.EnqueueMoveAsync(7, @"C:\Library\Book\")));

            Assert.Single(ids.Distinct());
            Assert.Single(jobs);
            persistence.Verify(
                store => store.AddAsync(It.IsAny<MoveJob>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task EnqueueMoveAsync_CaseDistinctDestinations_OnCaseSensitiveHost_CreateSeparateJobs()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            var jobs = new List<MoveJob>();
            var persistence = CreateInMemoryPersistence(jobs);
            var service = new MoveQueueService(
                NullLogger<MoveQueueService>.Instance,
                persistence.Object,
                new NoopHubBroadcaster(),
                TimeProvider.System);

            var firstId = await service.EnqueueMoveAsync(9, "/library/Title");
            var secondId = await service.EnqueueMoveAsync(9, "/library/title");

            Assert.NotEqual(firstId, secondId);
            Assert.Equal(2, jobs.Count);
        }

        [Fact]
        public async Task EnqueueMoveAsync_TrailingWhitespaceDestination_IsDistinctFromTrimmedPath()
        {
            var jobs = new List<MoveJob>();
            var persistence = CreateInMemoryPersistence(jobs);
            var service = new MoveQueueService(
                NullLogger<MoveQueueService>.Instance,
                persistence.Object,
                new NoopHubBroadcaster(),
                TimeProvider.System);

            var firstId = await service.EnqueueMoveAsync(9, "/library/Title ");
            var secondId = await service.EnqueueMoveAsync(9, "/library/Title");

            Assert.NotEqual(firstId, secondId);
            Assert.Equal(2, jobs.Count);
        }

        [Fact]
        public async Task EnqueueMoveAsync_DeleteEmptySourceFalse_PersistsCleanupChoice()
        {
            var jobs = new List<MoveJob>();
            var persistence = CreateInMemoryPersistence(jobs);
            var service = new MoveQueueService(
                NullLogger<MoveQueueService>.Instance,
                persistence.Object,
                new NoopHubBroadcaster(),
                TimeProvider.System);

            var jobId = await service.EnqueueMoveAsync(
                9,
                "/library/Title",
                "/downloads/Title",
                deleteEmptySource: false);

            var job = Assert.Single(jobs, candidate => candidate.Id == jobId);
            Assert.False(job.DeleteEmptySource);
        }

        [Fact]
        public async Task EnqueueMoveAsync_PersistedActiveJob_SchedulesExistingJob()
        {
            var existingJob = new MoveJob
            {
                Id = Guid.NewGuid(),
                AudiobookId = 9,
                RequestedPath = "/library/Title",
                ActiveDeduplicationKey = "9:/library/Title",
                Status = MoveJobStatus.Queued
            };
            var persistence = new Mock<IMoveQueuePersistence>();
            persistence.Setup(store => store.GetActiveByKeyAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(existingJob);
            var service = new MoveQueueService(
                NullLogger<MoveQueueService>.Instance,
                persistence.Object,
                new NoopHubBroadcaster(),
                TimeProvider.System);

            var jobId = await service.EnqueueMoveAsync(9, "/library/Title");

            Assert.Equal(existingJob.Id, jobId);
            Assert.True(service.Reader.TryRead(out var scheduledJob));
            Assert.Equal(existingJob.Id, scheduledJob.Id);
        }

        [Fact]
        public async Task RequeueMoveAsync_FailedJob_ReusesRecoveryIdentity()
        {
            var jobs = new List<MoveJob>();
            var persistence = CreateInMemoryPersistence(jobs);
            var service = new MoveQueueService(
                NullLogger<MoveQueueService>.Instance,
                persistence.Object,
                new NoopHubBroadcaster(),
                TimeProvider.System);
            var jobId = await service.EnqueueMoveAsync(9, "/library/Title", "/downloads/Title");
            Assert.True(service.Reader.TryRead(out _));
            await service.UpdateJobStatusAsync(jobId, LeaseOwner, 0, MoveJobStatus.Failed, "copy interrupted");

            var requeuedJobId = await service.RequeueMoveAsync(jobId);

            Assert.Equal(jobId, requeuedJobId);
            Assert.True(service.Reader.TryRead(out var scheduledJob));
            Assert.Equal(jobId, scheduledJob.Id);
        }

        [Fact]
        public async Task RecoverActiveJobsAsync_SchedulesPersistedProcessingJob()
        {
            var persistedJob = new MoveJob
            {
                Id = Guid.NewGuid(),
                AudiobookId = 9,
                RequestedPath = "/library/Title",
                ActiveDeduplicationKey = "9:/library/Title",
                Status = MoveJobStatus.Running
            };
            var persistence = new Mock<IMoveQueuePersistence>();
            persistence.Setup(store => store.GetActiveAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([persistedJob]);
            var service = new MoveQueueService(
                NullLogger<MoveQueueService>.Instance,
                persistence.Object,
                new NoopHubBroadcaster(),
                TimeProvider.System);

            await service.RecoverActiveJobsAsync();

            Assert.True(service.Reader.TryRead(out var scheduledJob));
            Assert.Equal(persistedJob.Id, scheduledJob.Id);
        }

        [Fact]
        public async Task TerminalStatus_ReleasesDeduplicationKey_ForLaterMove()
        {
            var jobs = new List<MoveJob>();
            var persistence = CreateInMemoryPersistence(jobs);
            var service = new MoveQueueService(
                NullLogger<MoveQueueService>.Instance,
                persistence.Object,
                new NoopHubBroadcaster(),
                TimeProvider.System);

            var firstId = await service.EnqueueMoveAsync(9, "/library/book");
            await service.UpdateJobStatusAsync(firstId, LeaseOwner, 0, MoveJobStatus.Completed);
            var secondId = await service.EnqueueMoveAsync(9, "/library/book/");

            Assert.NotEqual(firstId, secondId);
            Assert.Equal(2, jobs.Count);
            Assert.Null(jobs.Single(job => job.Id == firstId).ActiveDeduplicationKey);
            Assert.NotNull(jobs.Single(job => job.Id == secondId).ActiveDeduplicationKey);
        }

        private static Mock<IMoveQueuePersistence> CreateInMemoryPersistence(List<MoveJob> jobs)
        {
            var persistence = new Mock<IMoveQueuePersistence>();
            persistence.Setup(store => store.GetActiveByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string key, CancellationToken _) =>
                    jobs.SingleOrDefault(job => job.ActiveDeduplicationKey == key));
            persistence.Setup(store => store.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => jobs.SingleOrDefault(job => job.Id == id));
            persistence.Setup(store => store.AddAsync(It.IsAny<MoveJob>(), It.IsAny<CancellationToken>()))
                .Returns((MoveJob job, CancellationToken _) =>
                {
                    jobs.Add(job);
                    return Task.CompletedTask;
                });
            persistence.Setup(store => store.UpdateStatusAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<MoveJobStatus>(),
                    It.IsAny<MoveJobPhase>(),
                    It.IsAny<string?>(),
                    It.IsAny<MoveFailureKind>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Guid id, string _, int _, MoveJobStatus status, MoveJobPhase phase, string? error, MoveFailureKind failureKind, DateTimeOffset updatedAt, CancellationToken _) =>
                {
                    var job = jobs.Single(candidate => candidate.Id == id);
                    job.Status = status;
                    job.Phase = phase;
                    job.Error = error;
                    job.FailureKind = failureKind;
                    job.UpdatedAt = updatedAt.UtcDateTime;
                    if (!status.IsActive())
                    {
                        job.ActiveDeduplicationKey = null;
                    }

                    return Task.FromResult(true);
                });
            return persistence;
        }
    }
}
