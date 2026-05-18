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

namespace Listenarr.Application.Interfaces
{
    /// <summary>
    /// Determines whether authentication is required for the current application configuration.
    /// Implementations must fail closed — returning <see langword="true"/> — whenever the
    /// configuration cannot be read, rather than accidentally opening an unauthenticated surface.
    /// </summary>
    public interface IAuthenticationRequirementService
    {
        /// <summary>
        /// Returns <see langword="true"/> if authentication is enabled in the startup configuration;
        /// <see langword="false"/> if authentication has been explicitly disabled.
        /// Fails closed (returns <see langword="true"/>) when the configuration is unavailable.
        /// </summary>
        bool IsAuthenticationRequired();
    }
}
