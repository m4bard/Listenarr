using Listenarr.Application.Interfaces;
using Listenarr.Domain.Models;

namespace Listenarr.Tests.Mocks
{
    /// <summary>
    /// Simple IStartupConfigService stub for integration tests.
    /// </summary>
    internal class StartupConfigServiceMock : IStartupConfigService
    {
        private readonly StartupConfig _cfg;
        public StartupConfigServiceMock(StartupConfig cfg) { _cfg = cfg; }
        public StartupConfig? GetConfig() => _cfg;
        public bool IsAuthenticationRequired() => _cfg.IsAuthenticationEnabled();
        public string GetEffectiveApiVersion(string? requestedApiVersion = null) => _cfg.GetEffectiveApiVersion(requestedApiVersion);
        public string NormalizeApiVersion(string? configuredApiVersion, string? requestedApiVersion = null)
            => new StartupConfig { ApiVersion = configuredApiVersion }.GetEffectiveApiVersion(requestedApiVersion);
        public Task ReloadAsync() => Task.CompletedTask;
        public Task SaveAsync(StartupConfig config) => Task.CompletedTask;
    }
}
