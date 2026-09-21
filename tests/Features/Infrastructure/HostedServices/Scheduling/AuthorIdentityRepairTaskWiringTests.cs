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
using Listenarr.Application.Common.Scheduling;
using Listenarr.Infrastructure.HostedServices;
using Listenarr.Infrastructure.HostedServices.Scheduling;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Scheduling;

/// <summary>
/// The identity repair pass against the real registry and the real cycle runner, because what
/// is being asserted is that it is on the surface and can be brought forward, not that a
/// stand-in can be.
/// </summary>
/// <remarks>
/// The pass ships with a fifteen minute initial delay, so nothing here waits for a scheduled
/// cycle. That makes the assertion sharper rather than weaker: the only cycle that can happen
/// inside the test is the one the trigger starts, so reaching the processor is unambiguously
/// the manual path and not the clock.
/// </remarks>
[Trait("Name", "AuthorIdentityRepairTaskWiringTests")]
[Trait("Category", "BackgroundWorkers")]
public sealed class AuthorIdentityRepairTaskWiringTests : BaseTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ThePass_IsOnTheTaskSurface_AndAManualTriggerReachesItsProcessor()
    {
        var registry = CreateRegistry();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = new Mock<IAuthorIdentityRepairProcessor>();
        processor
            .Setup(candidate => candidate.RunCycleAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                reached.TrySetResult();
                return Task.CompletedTask;
            });

        var service = new AuthorIdentityRepairBackgroundService(
            Mock.Of<ILogger<AuthorIdentityRepairBackgroundService>>(),
            processor.Object,
            CreateRunner(registry),
            new AuthorIdentityRepairOptionsHolder(),
            EmptyScopeFactory());

        using var cancellation = new CancellationTokenSource();
        await service.StartAsync(cancellation.Token);
        try
        {
            var status = await WaitForRegistrationAsync(registry, nameof(AuthorIdentityRepairBackgroundService));

            Assert.Equal(ScheduledTaskManualTrigger.Allowed, status.ManualTrigger);

            // Daily, which is what HousekeepingCommand runs at in Readarr's TaskManager and what
            // the metadata walk beside this one runs at.
            Assert.Equal(TimeSpan.FromHours(24), status.Interval);

            Assert.Equal(
                ScheduledTaskTriggerResult.Accepted,
                registry.Trigger(nameof(AuthorIdentityRepairBackgroundService)).Result);

            await reached.Task.WaitAsync(Patience);
            Assert.Equal(
                ScheduledTaskTrigger.Manual,
                (await WaitForRegistrationAsync(registry, nameof(AuthorIdentityRepairBackgroundService))).LastTrigger);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    // The control, and it has to come out differently: a name nothing registered is refused, so
    // the acceptance above is the registration rather than a registry that says yes to anything.
    [Fact]
    public async Task ATaskNobodyRegistered_IsRefused()
    {
        var registry = CreateRegistry();
        var service = new AuthorIdentityRepairBackgroundService(
            Mock.Of<ILogger<AuthorIdentityRepairBackgroundService>>(),
            Mock.Of<IAuthorIdentityRepairProcessor>(),
            CreateRunner(registry),
            new AuthorIdentityRepairOptionsHolder(),
            EmptyScopeFactory());

        using var cancellation = new CancellationTokenSource();
        await service.StartAsync(cancellation.Token);
        try
        {
            await WaitForRegistrationAsync(registry, nameof(AuthorIdentityRepairBackgroundService));

            Assert.Equal(
                ScheduledTaskTriggerResult.NotFound,
                registry.Trigger("AuthorIdentityRepairBackgroundServiceTypo").Result);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static ScheduledTaskRegistry CreateRegistry() =>
        new(TimeProvider.System, Mock.Of<ILogger<ScheduledTaskRegistry>>());

    private static WorkerCycleRunner CreateRunner(IScheduledTaskRegistry registry) =>
        new(
            TimeProvider.System,
            Mock.Of<IAppMetricsService>(),
            registry,
            Mock.Of<ILogger<WorkerCycleRunner>>());

    /// <summary>
    /// A scope with no configuration service in it, which is the case the options loader is
    /// written to survive: it keeps the options already in use rather than substituting.
    /// </summary>
    private static IServiceScopeFactory EmptyScopeFactory()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private static async Task<ScheduledTaskStatus> WaitForRegistrationAsync(
        IScheduledTaskRegistry registry,
        string taskName)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;

        do
        {
            if (registry.Find(taskName) is { } status)
            {
                return status;
            }

            await Task.Delay(10);
        }
        while (DateTimeOffset.UtcNow < deadline);

        Assert.Fail($"'{taskName}' never reached the registry.");
        throw new InvalidOperationException("unreachable");
    }
}
