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
using System.Net;
using System.Security.Claims;
using Listenarr.Api.Security;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks;
using Microsoft.AspNetCore.Http;

namespace Listenarr.Tests.Features.Api.Security
{
    /// <summary>
    /// ShouldRedactSecretsForCaller is the only check in front of every redactor
    /// in ApiResponseRedactor. These pin what it is allowed to accept as a stand-in
    /// for a credential.
    /// </summary>
    [Trait("Name", "RedactionCallerTrustTests")]
    [Trait("Category", "Api")]
    public class RedactionCallerTrustTests : BaseTests
    {
        private static HttpContext BuildContext(
            string? remoteIp,
            bool authenticationRequired,
            ClaimsPrincipal? user = null,
            bool registerStartupConfigService = true)
        {
            var services = new ServiceCollection();
            if (registerStartupConfigService)
            {
                services.AddSingleton<IStartupConfigService>(
                    new StartupConfigServiceMock(new StartupConfig
                    {
                        AuthenticationRequired = authenticationRequired ? "true" : "false"
                    }));
            }

            var context = new DefaultHttpContext
            {
                RequestServices = services.BuildServiceProvider()
            };

            context.Connection.RemoteIpAddress = remoteIp == null ? null : IPAddress.Parse(remoteIp);
            if (user != null)
            {
                context.User = user;
            }

            return context;
        }

        private static ClaimsPrincipal AdminSession() =>
            new(new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.Name, "operator"),
                    new Claim(ClaimTypes.Role, "Administrator")
                },
                "Session"));

        private static ClaimsPrincipal ApiKeyPrincipal() =>
            new(new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.Name, "ApiKey"),
                    new Claim("AuthMethod", "ApiKey")
                },
                "ApiKey"));

        // Every one of these addresses satisfies SecurityRequestUtils.IsPrivateOrLoopback,
        // so before the fix each of them turned redaction off for an anonymous caller
        // even on an instance whose operator had switched the login screen on.
        [Theory]
        [InlineData("10.0.0.0")]
        [InlineData("172.16.9.9")]
        [InlineData("172.31.0.1")]
        [InlineData("192.168.0.0")]
        [InlineData("169.254.7.7")]
        [InlineData("127.0.0.1")]
        [InlineData("::1")]
        [InlineData("fd00::1234")]
        [InlineData("fe80::1")]
        public void AuthEnabled_AnonymousCallerOnPrivateAddress_IsStillRedacted(string remoteIp)
        {
            var context = BuildContext(remoteIp, authenticationRequired: true);

            Assert.True(HttpSecurityRequestUtils.ShouldRedactSecretsForCaller(context));
        }

        [Fact]
        public void AuthEnabled_AdminSessionOnPrivateAddress_IsNotRedacted()
        {
            var context = BuildContext("192.168.0.0", authenticationRequired: true, user: AdminSession());

            Assert.False(HttpSecurityRequestUtils.ShouldRedactSecretsForCaller(context));
        }

        [Fact]
        public void AuthEnabled_ApiKeyCallerOnPublicAddress_IsNotRedacted()
        {
            var context = BuildContext("203.0.113.9", authenticationRequired: true, user: ApiKeyPrincipal());

            Assert.False(HttpSecurityRequestUtils.ShouldRedactSecretsForCaller(context));
        }

        // The control. With the login screen off the operator has declared the
        // instance open and the frontend reads these endpoints with no credential
        // of its own, so the private-address allowance has to survive the fix.
        // If this ever starts agreeing with the auth-enabled cases above, the
        // tests have stopped discriminating.
        [Theory]
        [InlineData("192.168.0.0")]
        [InlineData("10.0.0.0")]
        [InlineData("127.0.0.1")]
        [InlineData("::1")]
        public void AuthDisabled_AnonymousCallerOnPrivateAddress_IsNotRedacted(string remoteIp)
        {
            var context = BuildContext(remoteIp, authenticationRequired: false);

            Assert.False(HttpSecurityRequestUtils.ShouldRedactSecretsForCaller(context));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AnonymousCallerOnPublicAddress_IsAlwaysRedacted(bool authenticationRequired)
        {
            var context = BuildContext("203.0.113.9", authenticationRequired);

            Assert.True(HttpSecurityRequestUtils.ShouldRedactSecretsForCaller(context));
        }

        [Fact]
        public void StartupConfigurationUnavailable_Redacts()
        {
            var context = BuildContext(
                "192.168.0.0",
                authenticationRequired: false,
                registerStartupConfigService: false);

            Assert.True(HttpSecurityRequestUtils.ShouldRedactSecretsForCaller(context));
        }
    }
}
