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
using Listenarr.Application.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Listenarr.Tests.Features.Api.Services
{
    public class ImageAccessTokenServiceTests
    {
        private readonly IImageAccessTokenService _service =
            new ImageAccessTokenService(new EphemeralDataProtectionProvider(), NullLogger<ImageAccessTokenService>.Instance);

        [Fact]
        public void CreateToken_ValidateToken_ReturnsImageScopedPrincipal()
        {
            var issued = _service.CreateToken("testuser");

            var principal = _service.ValidateToken(issued.Token);

            Assert.NotNull(principal);
            Assert.Equal("testuser", principal?.Identity?.Name);
            Assert.True(principal?.Identity?.IsAuthenticated);
            Assert.Equal("ImageToken", principal?.Identity?.AuthenticationType);
            Assert.Equal("ImageToken", principal?.FindFirst("AuthMethod")?.Value);
            Assert.Equal("Images", principal?.FindFirst("Scope")?.Value);
        }

        [Fact]
        public void ValidateToken_ReturnsNull_ForExpiredToken()
        {
            var issued = _service.CreateToken("expired-user", TimeSpan.FromSeconds(-1));

            var principal = _service.ValidateToken(issued.Token);

            Assert.Null(principal);
        }

        [Fact]
        public void ValidateToken_ReturnsNull_ForInvalidToken()
        {
            var principal = _service.ValidateToken("not-a-valid-token");

            Assert.Null(principal);
        }
    }
}
