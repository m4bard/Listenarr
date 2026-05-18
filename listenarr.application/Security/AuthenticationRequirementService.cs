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

using Listenarr.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Security
{
    /// <summary>
    /// Reads the startup configuration to determine whether authentication is currently required.
    /// Fails closed — returns <see langword="true"/> — whenever the configuration cannot be read,
    /// to avoid accidentally exposing an unauthenticated API surface.
    /// </summary>
    public sealed class AuthenticationRequirementService : IAuthenticationRequirementService
    {
        private readonly IStartupConfigService _startupConfigService;
        private readonly ILogger<AuthenticationRequirementService> _logger;

        public AuthenticationRequirementService(
            IStartupConfigService startupConfigService,
            ILogger<AuthenticationRequirementService> logger)
        {
            _startupConfigService = startupConfigService;
            _logger = logger;
        }

        public bool IsAuthenticationRequired()
        {
            try
            {
                var config = _startupConfigService.GetConfig();
                if (config == null)
                {
                    _logger.LogError("Startup configuration was unavailable while evaluating authentication requirements. Failing closed.");
                    return true;
                }

                return config.IsAuthenticationEnabled();
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogError(ex, "Startup configuration service was disposed while evaluating authentication requirements. Failing closed.");
                return true;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Startup configuration could not be read while evaluating authentication requirements. Failing closed.");
                return true;
            }
        }
    }
}
