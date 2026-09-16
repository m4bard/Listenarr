/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using System.Diagnostics;
using Listenarr.Domain.Notifications;
using Listenarr.Infrastructure.Notifications.CustomScript;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Notifications.CustomScript
{
    [Trait("Name", "CustomScriptNotificationTests")]
    [Trait("Category", "Notifications")]
    public class CustomScriptNotificationTests : BaseTests
    {
        private const string ScriptPath = "/opt/scripts/on-event.sh";

        private readonly Mock<IConfigurationService> _configuration = new();
        private readonly Mock<IProcessRunner> _processRunner = new();
        private readonly Mock<IFileSystem> _fileSystem = new();
        private readonly List<ProcessStartInfo> _started = new();

        private CustomScriptNotification BuildSubject(params CustomScriptConfiguration[] scripts)
        {
            _configuration.Setup(service => service.GetCustomScriptConfigurationsAsync())
                .ReturnsAsync(scripts.ToList());
            _configuration.Setup(service => service.GetStartupConfigAsync())
                .ReturnsAsync(new StartupConfig { InstanceName = "Listenarr", UrlBase = "https://example.invalid" });
            _fileSystem.Setup(system => system.FileExists(It.IsAny<string>())).Returns(true);
            _processRunner
                .Setup(runner => runner.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Callback<ProcessStartInfo, int, CancellationToken>((startInfo, _, _) => _started.Add(startInfo))
                .ReturnsAsync(new ProcessResult(0, string.Empty, string.Empty, false));

            return new CustomScriptNotification(
                _configuration.Object,
                _processRunner.Object,
                _fileSystem.Object,
                Mock.Of<ILogger<CustomScriptNotification>>());
        }

        private static CustomScriptConfiguration AScript(
            string name = "A Script",
            bool enabled = true,
            params NotificationChannel[] channels) =>
            new()
            {
                Id = name,
                Name = name,
                Path = ScriptPath,
                IsEnabled = enabled,
                Channels = channels.ToList(),
            };

        private static NotificationEvent AnEvent(NotificationChannel channel) =>
            new() { Channel = channel, Book = new NotificationEventBook { Id = 1, Title = "Frankenstein" } };

        [Fact]
        public void Name_MatchesTheNameOperatorsKnowFromTheOtherArrApplications()
        {
            Assert.Equal("Custom Script", BuildSubject().Name);
        }

        [Fact]
        public void Supports_CoversEveryDeliverableChannel()
        {
            var subject = BuildSubject();

            Assert.All(
                Enum.GetValues<NotificationChannel>().Where(channel => channel != NotificationChannel.Test),
                channel => Assert.True(subject.Supports(channel)));
            Assert.False(subject.Supports(NotificationChannel.Test));
        }

        [Fact]
        public async Task NotifyAsync_RunsExactlyOnceForAScriptEnabledForTheChannel()
        {
            // One invocation per configured script per event. The Discord and NTFY double-post came
            // from a handler that ran and then fell through into a second sender; the loop here has
            // nothing to fall through into, and this asserts it stays that way.
            var subject = BuildSubject(AScript(channels: NotificationChannel.Download));

            await subject.NotifyAsync(AnEvent(NotificationChannel.Download));

            Assert.Single(_started);
            Assert.Equal(ScriptPath, _started[0].FileName);
        }

        [Fact]
        public async Task NotifyAsync_PassesTheEventAsEnvironmentVariables()
        {
            var subject = BuildSubject(AScript(channels: NotificationChannel.Download));

            await subject.NotifyAsync(AnEvent(NotificationChannel.Download));

            var environment = _started.Single().Environment;
            Assert.Equal("Download", environment["Listenarr_EventType"]);
            Assert.Equal("Frankenstein", environment["Listenarr_Book_Title"]);
            Assert.Equal("Listenarr", environment["Listenarr_InstanceName"]);
        }

        [Fact]
        public async Task NotifyAsync_SkipsAScriptNotEnabledForTheChannel()
        {
            var subject = BuildSubject(AScript(channels: NotificationChannel.Grab));

            await subject.NotifyAsync(AnEvent(NotificationChannel.Download));

            Assert.Empty(_started);
        }

        [Fact]
        public async Task NotifyAsync_SkipsADisabledScript()
        {
            var subject = BuildSubject(AScript(enabled: false, channels: NotificationChannel.Download));

            await subject.NotifyAsync(AnEvent(NotificationChannel.Download));

            Assert.Empty(_started);
        }

        [Fact]
        public async Task NotifyAsync_RunsEveryScriptEnabledForTheChannel()
        {
            var subject = BuildSubject(
                AScript("First", channels: NotificationChannel.Download),
                AScript("Second", channels: NotificationChannel.Download),
                AScript("Third", channels: NotificationChannel.Grab));

            await subject.NotifyAsync(AnEvent(NotificationChannel.Download));

            Assert.Equal(2, _started.Count);
        }

        [Fact]
        public async Task NotifyAsync_DoesNotRunAScriptThatIsNotOnDisk()
        {
            var subject = BuildSubject(AScript(channels: NotificationChannel.Download));
            _fileSystem.Setup(system => system.FileExists(It.IsAny<string>())).Returns(false);

            await subject.NotifyAsync(AnEvent(NotificationChannel.Download));

            Assert.Empty(_started);
        }

        [Fact]
        public async Task NotifyAsync_RefusesARelativePath()
        {
            // A relative path resolves against whatever working directory the service happens to
            // have, which is not something an operator can reason about.
            var script = AScript(channels: NotificationChannel.Download);
            script.Path = "on-event.sh";
            var subject = BuildSubject(script);

            await subject.NotifyAsync(AnEvent(NotificationChannel.Download));

            Assert.Empty(_started);
        }

        [Fact]
        public async Task NotifyAsync_DoesNotThrowWhenTheScriptCannotBeRun()
        {
            // A notification target may not break the operation that produced the event.
            var subject = BuildSubject(AScript(channels: NotificationChannel.Download));
            _processRunner
                .Setup(runner => runner.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("no such file"));

            await subject.NotifyAsync(AnEvent(NotificationChannel.Download));
        }

        [Fact]
        public async Task NotifyAsync_DoesNotThrowWhenConfigurationCannotBeRead()
        {
            _configuration.Setup(service => service.GetCustomScriptConfigurationsAsync())
                .ThrowsAsync(new InvalidOperationException("database unavailable"));
            var subject = new CustomScriptNotification(
                _configuration.Object,
                _processRunner.Object,
                _fileSystem.Object,
                Mock.Of<ILogger<CustomScriptNotification>>());

            await subject.NotifyAsync(AnEvent(NotificationChannel.Download));

            Assert.Empty(_started);
        }

        [Fact]
        public async Task TestAsync_RunsTheScriptWithEventTypeTest()
        {
            var subject = BuildSubject(AScript(channels: NotificationChannel.Download));

            var result = await subject.TestAsync("A Script");

            Assert.True(result.IsValid);
            Assert.Equal("Test", _started.Single().Environment["Listenarr_EventType"]);
        }

        [Fact]
        public async Task TestAsync_FailsWithTheExitCodeWhenTheScriptReturnsNonZero()
        {
            var subject = BuildSubject(AScript(channels: NotificationChannel.Download));
            _processRunner
                .Setup(runner => runner.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult(2, string.Empty, string.Empty, false));

            var result = await subject.TestAsync("A Script");

            Assert.False(result.IsValid);
            Assert.Contains("Script exited with code: 2", result.Failures);
        }

        [Fact]
        public async Task TestAsync_FailsWhenTheScriptDoesNotFinish()
        {
            var subject = BuildSubject(AScript(channels: NotificationChannel.Download));
            _processRunner
                .Setup(runner => runner.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult(-1, string.Empty, string.Empty, true));

            var result = await subject.TestAsync("A Script");

            Assert.False(result.IsValid);
            Assert.Contains(result.Failures, failure => failure.Contains("did not finish", StringComparison.Ordinal));
        }

        [Fact]
        public async Task TestAsync_FailsWhenTheScriptIsNotOnDisk()
        {
            var subject = BuildSubject(AScript(channels: NotificationChannel.Download));
            _fileSystem.Setup(system => system.FileExists(It.IsAny<string>())).Returns(false);

            var result = await subject.TestAsync("A Script");

            Assert.False(result.IsValid);
            Assert.Contains("File does not exist", result.Failures);
            Assert.Empty(_started);
        }

        [Fact]
        public async Task TestAsync_FailsWhenNoSuchScriptIsConfigured()
        {
            var subject = BuildSubject(AScript(channels: NotificationChannel.Download));

            var result = await subject.TestAsync("not-a-configured-id");

            Assert.False(result.IsValid);
            Assert.Empty(_started);
        }

        [Fact]
        public async Task TestAsync_BoundsHowLongAScriptMayRun()
        {
            var subject = BuildSubject(AScript(channels: NotificationChannel.Download));
            var timeouts = new List<int>();
            _processRunner
                .Setup(runner => runner.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Callback<ProcessStartInfo, int, CancellationToken>((_, timeout, _) => timeouts.Add(timeout))
                .ReturnsAsync(new ProcessResult(0, string.Empty, string.Empty, false));

            await subject.TestAsync("A Script");

            Assert.Equal(CustomScriptNotification.ScriptTimeoutMilliseconds, Assert.Single(timeouts));
        }
    }
}
