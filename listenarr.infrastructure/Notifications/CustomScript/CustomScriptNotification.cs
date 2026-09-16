/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using System.Diagnostics;
using Listenarr.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Notifications.CustomScript
{
    /// <summary>
    /// Runs an operator-supplied executable on notification events, passing the event as
    /// environment variables.
    /// </summary>
    /// <remarks>
    /// This is the *arr family's escape valve: an integration Listenarr does not ship can still be
    /// served by a shell script. The variable contract is in <see cref="CustomScriptEnvironment"/>.
    /// <para>
    /// The operator-visible name is "Custom Script", matching Sonarr, Radarr, Prowlarr and Readarr,
    /// because that is what someone will look for in the settings list. The class is named
    /// CustomScriptNotification rather than CustomScript only to avoid colliding with its own
    /// namespace.
    /// </para>
    /// </remarks>
    public sealed class CustomScriptNotification : INotificationSubscriber
    {
        /// <summary>
        /// How long a script may run before it is killed. A notification target may not hold up the
        /// operation that produced the event indefinitely.
        /// </summary>
        public const int ScriptTimeoutMilliseconds = 60_000;

        private readonly IConfigurationService _configurationService;
        private readonly IProcessRunner _processRunner;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger<CustomScriptNotification> _logger;

        public CustomScriptNotification(
            IConfigurationService configurationService,
            IProcessRunner processRunner,
            IFileSystem fileSystem,
            ILogger<CustomScriptNotification> logger)
        {
            _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
            _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string Name => "Custom Script";

        // A script is opaque to us, so there is no channel it could not in principle handle. Test is
        // excluded because it is not a delivered event; it is reached through TestAsync.
        public bool Supports(NotificationChannel channel) => channel != NotificationChannel.Test;

        public async Task NotifyAsync(NotificationEvent notification, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(notification);

            if (!Supports(notification.Channel))
            {
                return;
            }

            List<CustomScriptConfiguration> configurations;
            try
            {
                configurations = await _configurationService.GetCustomScriptConfigurationsAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogWarning(ex, "Could not read custom script configuration; no scripts will run for {Channel}", notification.Channel);
                return;
            }

            var (instanceName, applicationUrl) = await ResolveInstanceAsync();
            var variables = CustomScriptEnvironment.Build(notification, instanceName, applicationUrl);

            foreach (var configuration in configurations.Where(candidate =>
                candidate.IsEnabled && candidate.Channels.Contains(notification.Channel)))
            {
                await RunQuietlyAsync(configuration, variables, cancellationToken);
            }
        }

        public async Task<NotificationSubscriberTestResult> TestAsync(string configurationId, CancellationToken cancellationToken = default)
        {
            var configurations = await _configurationService.GetCustomScriptConfigurationsAsync();
            var configuration = configurations.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, configurationId, StringComparison.Ordinal));

            if (configuration == null)
            {
                return NotificationSubscriberTestResult.Failure($"No custom script is configured with id '{configurationId}'");
            }

            var pathFailure = ValidatePath(configuration.Path);
            if (pathFailure != null)
            {
                return NotificationSubscriberTestResult.Failure(pathFailure);
            }

            var (instanceName, applicationUrl) = await ResolveInstanceAsync();
            var variables = CustomScriptEnvironment.BuildTest(instanceName, applicationUrl);

            try
            {
                var result = await ExecuteAsync(configuration, variables, cancellationToken);

                if (result.TimedOut)
                {
                    return NotificationSubscriberTestResult.Failure(
                        $"Script did not finish within {ScriptTimeoutMilliseconds / 1000} seconds and was stopped");
                }

                return result.ExitCode == 0
                    ? NotificationSubscriberTestResult.Success()
                    : NotificationSubscriberTestResult.Failure($"Script exited with code: {result.ExitCode}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "Custom script test failed for {ScriptName}", configuration.Name);
                return NotificationSubscriberTestResult.Failure(ex.Message);
            }
        }

        /// <summary>
        /// Rejects a path before we try to run it. A relative path would resolve against whatever
        /// working directory the service happens to have, which is not something an operator can
        /// reason about, so it is refused rather than guessed at.
        /// </summary>
        private string? ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "No script path is configured";
            }

            if (!Path.IsPathRooted(path))
            {
                return "Script path must be absolute";
            }

            return _fileSystem.FileExists(path) ? null : "File does not exist";
        }

        private async Task RunQuietlyAsync(
            CustomScriptConfiguration configuration,
            IReadOnlyDictionary<string, string> variables,
            CancellationToken cancellationToken)
        {
            var pathFailure = ValidatePath(configuration.Path);
            if (pathFailure != null)
            {
                _logger.LogWarning("Skipping custom script {ScriptName}: {Reason}", configuration.Name, pathFailure);
                return;
            }

            try
            {
                var result = await ExecuteAsync(configuration, variables, cancellationToken);

                if (result.TimedOut)
                {
                    _logger.LogWarning("Custom script {ScriptName} timed out after {Timeout}ms and was stopped", configuration.Name, ScriptTimeoutMilliseconds);
                }
                else if (result.ExitCode != 0)
                {
                    _logger.LogWarning("Custom script {ScriptName} exited with code {ExitCode}", configuration.Name, result.ExitCode);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            // A failing notification target must never break the operation that produced the event.
#pragma warning disable CA1031
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "Custom script {ScriptName} could not be run", configuration.Name);
            }
#pragma warning restore CA1031
        }

        private async Task<ProcessResult> ExecuteAsync(
            CustomScriptConfiguration configuration,
            IReadOnlyDictionary<string, string> variables,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = configuration.Path,
                UseShellExecute = false,
            };

            foreach (var variable in variables)
            {
                startInfo.Environment[variable.Key] = variable.Value;
            }

            _logger.LogDebug(
                "Executing custom script {ScriptName} for {EventType}",
                configuration.Name,
                variables.TryGetValue(CustomScriptEnvironment.Prefix + "EventType", out var eventType) ? eventType : "Unknown");

            var result = await _processRunner.RunAsync(startInfo, ScriptTimeoutMilliseconds, cancellationToken);

            _logger.LogDebug(
                "Executed custom script {ScriptName} - exit code {ExitCode}, timed out: {TimedOut}",
                configuration.Name,
                result.ExitCode,
                result.TimedOut);

            return result;
        }

        private async Task<(string? InstanceName, string? ApplicationUrl)> ResolveInstanceAsync()
        {
            try
            {
                var startup = await _configurationService.GetStartupConfigAsync();
                return (startup?.InstanceName, startup?.UrlBase);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogDebug(ex, "Could not resolve instance name or application URL for custom script environment");
                return (null, null);
            }
        }
    }
}
