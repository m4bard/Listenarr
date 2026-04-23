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

using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace Listenarr.Application.Security
{
    public sealed record ImageAccessTokenResult(string Token, DateTimeOffset ExpiresAt);

    public interface IImageAccessTokenService
    {
        ImageAccessTokenResult CreateToken(string username, TimeSpan? lifetime = null);
        ClaimsPrincipal? ValidateToken(string token);
    }

    public sealed class ImageAccessTokenService : IImageAccessTokenService
    {
        public static readonly TimeSpan DefaultExpiration = TimeSpan.FromHours(8);

        private const string Scope = "images";
        private readonly IDataProtector _protector;
        private readonly ILogger<ImageAccessTokenService> _logger;

        public ImageAccessTokenService(
            IDataProtectionProvider dataProtectionProvider,
            ILogger<ImageAccessTokenService> logger)
        {
            _protector = dataProtectionProvider.CreateProtector("Listenarr.ImageAccessToken.v1");
            _logger = logger;
        }

        public ImageAccessTokenResult CreateToken(string username, TimeSpan? lifetime = null)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentException("Username is required.", nameof(username));
            }

            var expiresAt = DateTimeOffset.UtcNow.Add(lifetime ?? DefaultExpiration);
            var payload = new ImageAccessTokenPayload
            {
                Username = username.Trim(),
                ExpiresAt = expiresAt,
                Scope = Scope,
            };

            var json = JsonSerializer.Serialize(payload);
            var protectedPayload = _protector.Protect(json);
            var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(protectedPayload));

            return new ImageAccessTokenResult(token, expiresAt);
        }

        public ClaimsPrincipal? ValidateToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            try
            {
                var protectedPayload = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
                var json = _protector.Unprotect(protectedPayload);
                var payload = JsonSerializer.Deserialize<ImageAccessTokenPayload>(json);

                if (payload == null ||
                    !string.Equals(payload.Scope, Scope, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(payload.Username) ||
                    payload.ExpiresAt <= DateTimeOffset.UtcNow)
                {
                    return null;
                }

                var claims = new List<Claim>
                {
                    new(ClaimTypes.Name, payload.Username),
                    new("AuthMethod", "ImageToken"),
                    new("Scope", "Images"),
                };

                var identity = new ClaimsIdentity(claims, "ImageToken");
                return new ClaimsPrincipal(identity);
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException &&
                ex is not OutOfMemoryException &&
                ex is not StackOverflowException)
            {
                _logger.LogDebug(ex, "Failed to validate image access token");
                return null;
            }
        }

        private sealed class ImageAccessTokenPayload
        {
            public string Username { get; set; } = string.Empty;
            public DateTimeOffset ExpiresAt { get; set; }
            public string Scope { get; set; } = string.Empty;
        }
    }
}
