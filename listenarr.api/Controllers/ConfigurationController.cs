/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2025 Robbie Davis
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

using Listenarr.Domain.Models;
using Listenarr.Api.Services;
using Listenarr.Api.Hubs;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using Microsoft.AspNetCore.SignalR;

namespace Listenarr.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ConfigurationController : ControllerBase
    {
        private readonly IConfigurationService _configurationService;
        private readonly ILogger<ConfigurationController> _logger;
        private readonly IUserService _userService;
        private readonly IHubContext<SettingsHub> _settingsHub;
        private readonly IDownloadService _downloadService;
        private readonly NotificationService _notificationService;

        public ConfigurationController(IConfigurationService configurationService, ILogger<ConfigurationController> logger, IUserService userService, IHubContext<SettingsHub> settingsHub, IDownloadService downloadService, NotificationService notificationService)
        {
            _configurationService = configurationService;
            _logger = logger;
            _userService = userService;
            _settingsHub = settingsHub;
            _downloadService = downloadService;
            _notificationService = notificationService;
        }

        // API Configuration endpoints
        /// <summary>
        /// Get all API configurations.
        /// </summary>
        [HttpGet("apis")]
        [ProducesResponseType(typeof(List<ApiConfiguration>), 200)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<List<ApiConfiguration>>> GetApiConfigurations()
        {
            try
            {
                var configs = await _configurationService.GetApiConfigurationsAsync();
                return Ok(configs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving API configurations");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get a specific API configuration by ID.
        /// </summary>
        /// <param name="id">API configuration ID</param>
        [HttpGet("apis/{id}")]
        [ProducesResponseType(typeof(ApiConfiguration), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<ApiConfiguration>> GetApiConfiguration(string id)
        {
            try
            {
                var config = await _configurationService.GetApiConfigurationAsync(id);
                if (config == null)
                {
                    return NotFound();
                }
                return Ok(config);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving API configuration {Id}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Save an API configuration.
        /// </summary>
        /// <param name="config">API configuration to save</param>
        [HttpPost("apis")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<object>> SaveApiConfiguration([FromBody] ApiConfiguration config)
        {
            try
            {
                var id = await _configurationService.SaveApiConfigurationAsync(config);
                return Ok(new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving API configuration");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Delete an API configuration by ID.
        /// </summary>
        /// <param name="id">API configuration ID</param>
        [HttpDelete("apis/{id}")]
        [ProducesResponseType(typeof(bool), 200)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<bool>> DeleteApiConfiguration(string id)
        {
            try
            {
                var deleted = await _configurationService.DeleteApiConfigurationAsync(id);
                return Ok(deleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting API configuration {Id}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        // Download Client Configuration endpoints
        /// <summary>
        /// Get all download client configurations.
        /// </summary>
        [HttpGet("download-clients")]
        [ProducesResponseType(typeof(List<DownloadClientConfiguration>), 200)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<List<DownloadClientConfiguration>>> GetDownloadClientConfigurations()
        {
            try
            {
                var configs = await _configurationService.GetDownloadClientConfigurationsAsync();
                // Redact client-local DownloadPath before returning to frontend
                var response = configs.Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.Type,
                    c.Host,
                    c.Port,
                    c.Username,
                    // Do not include DownloadPath - client should decide its local path
                    c.UseSSL,
                    c.IsEnabled,
                    Settings = c.Settings,
                    c.CreatedAt
                }).ToList();

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving download client configurations");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get a specific download client configuration by ID.
        /// </summary>
        /// <param name="id">Download client configuration ID</param>
        [HttpGet("download-clients/{id}")]
        [ProducesResponseType(typeof(DownloadClientConfiguration), 200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<DownloadClientConfiguration>> GetDownloadClientConfiguration(string id)
        {
            try
            {
                var config = await _configurationService.GetDownloadClientConfigurationAsync(id);
                if (config == null)
                {
                    return NotFound();
                }

                // Redact client-local DownloadPath before returning
                var response = new
                {
                    config.Id,
                    config.Name,
                    config.Type,
                    config.Host,
                    config.Port,
                    config.Username,
                    // Do not include DownloadPath
                    config.UseSSL,
                    config.IsEnabled,
                    Settings = config.Settings,
                    config.CreatedAt
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving download client configuration {Id}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost("download-clients")]
        public async Task<ActionResult<object>> SaveDownloadClientConfiguration([FromBody] DownloadClientConfiguration config)
        {
            try
            {
                if (config == null)
                {
                    return BadRequest("Missing download client configuration");
                }
                // If updating an existing configuration, avoid overwriting sensitive fields
                // with blank values from the incoming payload. Fetch existing config
                // and copy username/password and any client-specific apiKey when missing.
                if (!string.IsNullOrWhiteSpace(config?.Id))
                {
                    var existing = await _configurationService.GetDownloadClientConfigurationAsync(config.Id);
                    if (existing != null)
                    {
                        // Preserve username/password if incoming values are empty
                        if (string.IsNullOrWhiteSpace(config.Username) && !string.IsNullOrWhiteSpace(existing.Username))
                        {
                            config.Username = existing.Username;
                        }

                        if (string.IsNullOrWhiteSpace(config.Password) && !string.IsNullOrWhiteSpace(existing.Password))
                        {
                            config.Password = existing.Password;
                        }

                        // Preserve SABnzbd API key (stored in Settings["apiKey"]) if not provided
                        try
                        {
                            if (existing.Settings != null)
                            {
                                if (!config.Settings?.ContainsKey("apiKey") ?? true)
                                {
                                    if (existing.Settings.TryGetValue("apiKey", out var existingApiKeyObj))
                                    {
                                        var existingApiKey = existingApiKeyObj?.ToString();
                                        if (!string.IsNullOrWhiteSpace(existingApiKey))
                                        {
                                            if (config.Settings == null)
                                                config.Settings = new System.Collections.Generic.Dictionary<string, object>();
                                            config.Settings["apiKey"] = existingApiKey;
                                        }
                                    }
                                }
                                else
                                {
                                    // If config.Settings contains apiKey but it's blank, preserve existing
                                    if (config.Settings != null && config.Settings.TryGetValue("apiKey", out var incomingApiKeyObj))
                                    {
                                        var incomingApiKey = incomingApiKeyObj?.ToString();
                                        if (string.IsNullOrWhiteSpace(incomingApiKey) && existing.Settings.TryGetValue("apiKey", out var exKey))
                                        {
                                            var existingApiKey = exKey?.ToString();
                                            if (!string.IsNullOrWhiteSpace(existingApiKey))
                                            {
                                                config.Settings["apiKey"] = existingApiKey;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Non-fatal: if Settings isn't a dictionary or unexpected structure, ignore and proceed
                        }
                    }
                }

                var id = await _configurationService.SaveDownloadClientConfigurationAsync(config!);
                return Ok(new { id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving download client configuration");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpDelete("download-clients/{id}")]
        public async Task<ActionResult<bool>> DeleteDownloadClientConfiguration(string id)
        {
            try
            {
                var deleted = await _configurationService.DeleteDownloadClientConfigurationAsync(id);
                return Ok(deleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting download client configuration {Id}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        // Test a download client configuration (accepts full config payload so credentials can be included)
        [HttpPost("download-clients/test")]
        public async Task<ActionResult<object>> TestDownloadClientConfiguration([FromBody] DownloadClientConfiguration config)
        {
            try
            {
                if (config == null)
                {
                    return BadRequest(new { success = false, message = "Missing download client configuration" });
                }

                if (!string.IsNullOrWhiteSpace(config.Id))
                {
                    var existing = await _configurationService.GetDownloadClientConfigurationAsync(config.Id);
                    if (existing != null)
                    {
                        if (string.IsNullOrWhiteSpace(config.Username) && !string.IsNullOrWhiteSpace(existing.Username))
                        {
                            config.Username = existing.Username;
                        }

                        if (string.IsNullOrWhiteSpace(config.Password) && !string.IsNullOrWhiteSpace(existing.Password))
                        {
                            config.Password = existing.Password;
                        }

                        try
                        {
                            if (existing.Settings != null)
                            {
                                config.Settings ??= new System.Collections.Generic.Dictionary<string, object>();

                                foreach (var kvp in existing.Settings)
                                {
                                    if (!config.Settings.ContainsKey(kvp.Key))
                                    {
                                        config.Settings[kvp.Key] = kvp.Value;
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Non-fatal; continue with whatever settings were provided.
                        }
                    }
                }

                // Delegate to download service to perform protocol-specific lightweight tests
                var (Success, Message, Client) = await _downloadService.TestDownloadClientAsync(config);
                return Ok(new { success = Success, message = Message, client = Client });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing download client configuration");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // Application Settings endpoints
        [HttpGet("settings")]
        public async Task<ActionResult<ApplicationSettings>> GetApplicationSettings()
        {
            try
            {
                var settings = await _configurationService.GetApplicationSettingsAsync();
                return Ok(settings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving application settings");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost("settings")]
        public async Task<ActionResult<ApplicationSettings>> SaveApplicationSettings([FromBody] ApplicationSettings settings)
        {
            try
            {
                _logger.LogDebug("Saving application settings");
                await _configurationService.SaveApplicationSettingsAsync(settings);

                // Return the saved settings to confirm what was persisted
                var savedSettings = await _configurationService.GetApplicationSettingsAsync();

                // Clear sensitive admin credentials from response (they are [NotMapped] but let's be safe)
                savedSettings.AdminUsername = null;
                savedSettings.AdminPassword = null;

                // Broadcast settings change to all connected clients (including Discord bot)
                await _settingsHub.Clients.All.SendAsync("SettingsUpdated", savedSettings);

                _logger.LogDebug("Application settings saved successfully and broadcasted via SignalR");
                return Ok(savedSettings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving application settings");
                return StatusCode(500, new { error = "Failed to save application settings", message = ex.Message });
            }
        }

        // Startup Configuration endpoints
        /// <summary>
        /// Get the Listenarr startup configuration (API key, authentication, etc).
        /// API key is redacted if authentication is enabled and user is not authenticated.
        /// </summary>
        /// <returns>StartupConfig object</returns>
        [HttpGet("startupconfig")]
        [ProducesResponseType(typeof(StartupConfig), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<StartupConfig>> GetStartupConfig()
        {
            try
            {
                var config = await _configurationService.GetStartupConfigAsync() ?? new StartupConfig();
                var rawAuth = config.AuthenticationRequired;
                var authEnabled = rawAuth?.ToLowerInvariant() is "true" or "yes" or "1";
                var isAuthenticated = User?.Identity?.IsAuthenticated ?? false;
                _logger.LogInformation($"[ConfigurationController] AuthenticationRequired config value: '{rawAuth}', authEnabled: {authEnabled}, user authenticated: {isAuthenticated}");
                if (authEnabled && !isAuthenticated)
                {
                    _logger.LogWarning("[ConfigurationController] Authentication is enabled and user is not authenticated. Returning 401.");
                    return Unauthorized();
                }
                // Only redact API key if authentication is enabled and user is not authenticated
                if (authEnabled && !isAuthenticated)
                {
                    if (!string.IsNullOrEmpty(config.ApiKey))
                    {
                        _logger.LogInformation("[ConfigurationController] Authentication is enabled and user is not authenticated, redacting ApiKey.");
                        config.ApiKey = "REDACTED";
                    }
                }
                // If authentication is disabled, always return the real API key
                _logger.LogInformation($"[ConfigurationController] Returning startup config. ApiKey: '{(string.IsNullOrEmpty(config.ApiKey) ? "(empty)" : config.ApiKey)}'");
                return Ok(config);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving startup configuration");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Save the Listenarr startup configuration (API key, authentication, etc).
        /// </summary>
        /// <param name="config">StartupConfig object to save</param>
        /// <returns>The saved StartupConfig</returns>
        [HttpPost("startupconfig")]
        [ProducesResponseType(typeof(StartupConfig), 200)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<StartupConfig>> SaveStartupConfig([FromBody] StartupConfig config)
        {
            try
            {
                await _configurationService.SaveStartupConfigAsync(config);
                // Return the saved config to confirm what was persisted
                var savedConfig = await _configurationService.GetStartupConfigAsync();
                return Ok(savedConfig);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving startup configuration");
                return StatusCode(500, "Internal server error");
            }
        }

        // Regenerate API key (requires authentication)
        [HttpPost("apikey/regenerate")]
        [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Administrator")]
        public async Task<ActionResult<object>> RegenerateApiKey()
        {
            try
            {
                var cfg = await _configurationService.GetStartupConfigAsync();
                var current = cfg ?? new StartupConfig();
                // Generate a new API key (cryptographically secure)
                var bytes = new byte[32];
                using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                {
                    rng.GetBytes(bytes);
                }
                var newKey = Convert.ToBase64String(bytes).TrimEnd('=');
                current.ApiKey = newKey;
                await _configurationService.SaveStartupConfigAsync(current);
                return Ok(new { apiKey = newKey });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error regenerating API key");
                return StatusCode(500, "Internal server error");
            }
        }

        // Generate API key for initial setup (when no API key exists and no users exist)
        [HttpPost("apikey/generate-initial")]
        public async Task<ActionResult<object>> GenerateInitialApiKey()
        {
            try
            {
                var current = await _configurationService.GetStartupConfigAsync();
                if (current == null)
                {
                    return StatusCode(500, "Unable to load startup configuration");
                }

                // Generate a new API key
                var newKey = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                current.ApiKey = newKey;
                await _configurationService.SaveStartupConfigAsync(current);
                return Ok(new { apiKey = newKey });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating initial API key");
                return StatusCode(500, "Internal server error");
            }
        }

        // Test notification endpoint
        /// <summary>
        /// Send a test notification to the configured webhook URL.
        /// </summary>
        [HttpPost("notifications/test")]
        public async Task<ActionResult<object>> TestNotification()
        {
            try
            {
                var settings = await _configurationService.GetApplicationSettingsAsync();

                if (string.IsNullOrWhiteSpace(settings.WebhookUrl))
                {
                    return BadRequest(new { success = false, message = "No webhook URL configured" });
                }

                // Create test notification data
                var testData = new
                {
                    title = "Test Audiobook",
                    authors = new[] { "Test Author" },
                    asin = "B000TEST",
                    description = "This is a test notification from Listenarr to verify your webhook configuration is working correctly.",
                    message = "This is a test notification from Listenarr",
                    timestamp = DateTime.UtcNow,
                    version = "1.0.0" // Could be made dynamic later
                };

                // Get notification service and send test notification
                if (_notificationService == null)
                {
                    _logger.LogError("NotificationService not available to send test notification");
                    return StatusCode(500, new { success = false, message = "Server misconfiguration: notification service unavailable" });
                }

                // Send notification with "test" trigger - this will bypass the enabled triggers check
                // since we're testing the webhook URL directly
                await _notificationService.SendNotificationAsync("test", testData, settings.WebhookUrl, new List<string> { "test" });

                return Ok(new { success = true, message = "Test notification sent successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending test notification");
                return StatusCode(500, new { success = false, message = "Failed to send test notification", error = ex.Message });
            }
        }
    }
}

