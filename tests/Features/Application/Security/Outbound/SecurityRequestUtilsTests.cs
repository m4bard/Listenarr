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
using Listenarr.Application.Security.Outbound;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Security.Outbound
{
    /// <summary>
    /// IsPrivateOrLoopback decides two unrelated things: whether an inbound caller is
    /// trusted enough to skip secret redaction, and whether an outbound URL points
    /// somewhere the server is not allowed to fetch. Both readings have to agree about
    /// an IPv4 address that arrives in its IPv6-mapped form, because a dual-stack
    /// listener hands over every IPv4 peer that way and a URL host may be written that
    /// way by whoever supplied it.
    /// </summary>
    [Trait("Name", "SecurityRequestUtilsTests")]
    [Trait("Category", "Application")]
    public class SecurityRequestUtilsTests : BaseTests
    {
        [Theory]
        [InlineData("127.0.0.1")]
        [InlineData("10.0.0.0")]
        [InlineData("172.16.0.1")]
        [InlineData("172.20.10.5")]
        [InlineData("172.31.255.254")]
        [InlineData("192.168.0.0")]
        [InlineData("169.254.10.20")]
        public void IsPrivateOrLoopback_BareIPv4_IsPrivate(string address)
        {
            Assert.True(SecurityRequestUtils.IsPrivateOrLoopback(IPAddress.Parse(address)));
        }

        /// <summary>
        /// The same addresses as above, in the form a dual-stack socket actually produces.
        /// </summary>
        [Theory]
        [InlineData("127.0.0.1")]
        [InlineData("10.0.0.0")]
        [InlineData("172.16.0.1")]
        [InlineData("172.20.10.5")]
        [InlineData("172.31.255.254")]
        [InlineData("192.168.0.0")]
        [InlineData("169.254.10.20")]
        public void IsPrivateOrLoopback_IPv4MappedToIPv6_IsPrivate(string address)
        {
            var mapped = IPAddress.Parse(address).MapToIPv6();

            Assert.True(mapped.IsIPv4MappedToIPv6);
            Assert.True(SecurityRequestUtils.IsPrivateOrLoopback(mapped));
        }

        [Theory]
        [InlineData("::1")]
        [InlineData("fe80::1")]
        [InlineData("fec0::1")]
        [InlineData("fc00::1")]
        [InlineData("fd12:3456:789a::1")]
        public void IsPrivateOrLoopback_NativeIPv6_IsPrivate(string address)
        {
            Assert.True(SecurityRequestUtils.IsPrivateOrLoopback(IPAddress.Parse(address)));
        }

        [Theory]
        [InlineData("8.8.8.8")]
        [InlineData("203.0.113.7")]
        [InlineData("172.32.0.1")]
        [InlineData("172.15.255.255")]
        [InlineData("2606:4700:4700::1111")]
        public void IsPrivateOrLoopback_PublicAddress_IsNotPrivate(string address)
        {
            Assert.False(SecurityRequestUtils.IsPrivateOrLoopback(IPAddress.Parse(address)));
        }

        /// <summary>
        /// Mapping must not widen the answer either: a public IPv4 address stays public
        /// when it arrives mapped.
        /// </summary>
        [Theory]
        [InlineData("8.8.8.8")]
        [InlineData("203.0.113.7")]
        [InlineData("172.32.0.1")]
        [InlineData("172.15.255.255")]
        public void IsPrivateOrLoopback_PublicIPv4MappedToIPv6_IsNotPrivate(string address)
        {
            var mapped = IPAddress.Parse(address).MapToIPv6();

            Assert.True(mapped.IsIPv4MappedToIPv6);
            Assert.False(SecurityRequestUtils.IsPrivateOrLoopback(mapped));
        }

        [Theory]
        [InlineData("127.0.0.1")]
        [InlineData("127.0.0.53")]
        public void IsLoopback_BareAndMapped_AgreeThatLoopbackIsLoopback(string address)
        {
            var bare = IPAddress.Parse(address);

            Assert.True(SecurityRequestUtils.IsLoopback(bare));
            Assert.True(SecurityRequestUtils.IsLoopback(bare.MapToIPv6()));
        }

        /// <summary>
        /// The mapped-form literal is the shape a URL host can carry, which is how this
        /// reaches OutboundRequestSecurity rather than the redaction gate.
        /// </summary>
        [Theory]
        [InlineData("::ffff:127.0.0.1")]
        [InlineData("::ffff:172.16.0.1")]
        [InlineData("::ffff:169.254.10.20")]
        public void IsPrivateOrLoopback_MappedFormLiteral_IsPrivate(string address)
        {
            Assert.True(SecurityRequestUtils.IsPrivateOrLoopback(IPAddress.Parse(address)));
        }
    }
}
