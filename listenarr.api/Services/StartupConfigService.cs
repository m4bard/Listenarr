using Listenarr.Domain.Models;
using System.Text.Json;

namespace Listenarr.Api.Services
{
    public interface IStartupConfigService
    {
        StartupConfig? GetConfig();
        Task ReloadAsync();
        Task SaveAsync(StartupConfig config);
    }

    public class StartupConfigService : IStartupConfigService
    {
        private readonly ILogger<StartupConfigService> _logger;
        private readonly string _configPath;
        private StartupConfig? _config;

        public StartupConfigService(ILogger<StartupConfigService> logger, Microsoft.Extensions.Hosting.IHostEnvironment env)
        {
            _logger = logger;

            // Determine config.json path. Prefer the repository copy at
            // <repoRoot>/listenarr.api/config/config.json when it exists so
            // local development runs always use the repo config file.
            var contentRoot = env.ContentRootPath ?? AppContext.BaseDirectory;

            try
            {
                var dirInfo = new DirectoryInfo(contentRoot);

                // First pass: search ancestors for a repository-style config at
                // <ancestor>/listenarr.api/config/config.json and prefer that.
                const int maxDepth = 8;
                int depth = 0;
                while (dirInfo != null && depth++ < maxDepth)
                {
                    var candidateRepo = Path.Combine(dirInfo.FullName, Path.Combine("listenarr.api", "config", "config.json"));
                    if (File.Exists(candidateRepo))
                    {
                        _configPath = candidateRepo;
                        break;
                    }

                    dirInfo = dirInfo.Parent;
                }

                // Second pass (only if repo-style not found): search for any
                // config/config.json in ancestors (this picks up bin/config/config.json).
                if (string.IsNullOrEmpty(_configPath))
                {
                    dirInfo = new DirectoryInfo(contentRoot);
                    depth = 0;
                    while (dirInfo != null && depth++ < maxDepth)
                    {
                        var candidateLocal = Path.Combine(dirInfo.FullName, Path.Combine("config", "config.json"));
                        if (File.Exists(candidateLocal))
                        {
                            _configPath = candidateLocal;
                            break;
                        }
                        dirInfo = dirInfo.Parent;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error while probing for repository config.json; falling back to content root");
            }

            // Fallback: use content-root/config/config.json if nothing else was found
            if (string.IsNullOrEmpty(_configPath))
            {
                _configPath = Path.Combine(contentRoot, Path.Combine("config", "config.json"));
            }

            _logger.LogInformation("[StartupConfigService] Using startup config path: {Path}", _configPath);

            Load();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    _logger.LogInformation("Startup config not found at {Path}, creating default config", _configPath);
                    _config = CreateDefaultConfig();
                    SaveDefaultConfig();
                    return;
                }

                var json = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<StartupConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // Auto-generate API key if missing from existing config
                if (_config != null && string.IsNullOrWhiteSpace(_config.ApiKey))
                {
                    _config.ApiKey = GenerateApiKey();
                    SaveConfigFile(_config); // Save the updated config with new API key
                    _logger.LogInformation("Auto-generated API key for existing configuration");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load startup config from {Path}", _configPath);
                _config = new StartupConfig();
            }
        }

        public StartupConfig? GetConfig() => _config;

        public Task ReloadAsync()
        {
            Load();
            return Task.CompletedTask;
        }

        public Task SaveAsync(StartupConfig config)
        {
            try
            {
                _logger.LogInformation("[StartupConfigService] Attempting to save config to {Path}", _configPath);
                // Accept whatever value the caller provides for AuthenticationRequired.
                // This allows the frontend 'require login' toggle to control the flag.
                SaveConfigFile(config);
                _logger.LogInformation("[StartupConfigService] Successfully saved config to {Path}", _configPath);
                _config = config;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[StartupConfigService] Exception while saving config to {Path}", _configPath);
                throw;
            }

            return Task.CompletedTask;
        }

        private StartupConfig CreateDefaultConfig()
        {
            // Generate a cryptographically secure API key for initial setup
            var apiKey = GenerateApiKey();

            return new StartupConfig
            {
                // Basic configuration with sensible defaults
                LogLevel = "Information",
                EnableSsl = false,
                Port = 5000,
                SslPort = 6868,
                UrlBase = "/",
                BindAddress = "*",
                ApiKey = apiKey, // Auto-generated on first run
                // Authentication: Set to "true" to require login, "false" for open access
                // When enabled, uses secure session-based authentication with Bearer tokens
                AuthenticationRequired = "false",
                UpdateMechanism = "BuiltIn",
                LaunchBrowser = true,
                Branch = "main",
                InstanceName = "Listenarr",
                SyslogPort = null,
                AnalyticsEnabled = false,
                SslCertPath = null,
                SslCertPassword = null,
                Ffmpeg = new FfmpegConfig
                {
                    Provider = "gyan", // Default to gyan.dev for Windows
                    ReleaseOverride = null,
                    ChecksumUrl = null,
                    Arch = null
                }
            };
        }

        private static string GenerateApiKey()
        {
            var bytes = new byte[32];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Convert.ToBase64String(bytes).TrimEnd('=');
        }

        private void SaveDefaultConfig()
        {
            try
            {
                SaveConfigFile(_config);
                _logger.LogInformation("Default config.json created at {Path}", _configPath);
                _logger.LogInformation("Authentication is DISABLED by default. Set 'AuthenticationRequired' to 'true' in config.json to enable secure login.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save default config to {Path}", _configPath);
            }
        }

        private void SaveConfigFile(StartupConfig? config)
        {
            if (config == null)
            {
                _logger.LogWarning("[StartupConfigService] SaveConfigFile called with null config. Path: {Path}", _configPath);
                return;
            }

            // Ensure the config directory exists
            var configDir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
            {
                _logger.LogWarning("[StartupConfigService] Config directory did not exist. Creating: {Dir}", configDir);
                Directory.CreateDirectory(configDir);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(config, options);
            try
            {
                File.WriteAllText(_configPath, json);
                _logger.LogInformation("[StartupConfigService] File.WriteAllText succeeded for {Path}", _configPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[StartupConfigService] File.WriteAllText failed for {Path}", _configPath);
                throw;
            }
        }
    }
}

