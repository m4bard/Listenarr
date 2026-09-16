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

namespace Listenarr.Tests.Features.Application.Security.Outbound
{
    /// <summary>
    /// Characterization tests for the outbound URL blocklist. These record what the code
    /// actually does today, including the cases where it lets a private target through,
    /// so that a fix can be seen to change them. Each group carries its own control.
    /// </summary>
    public class OutboundRequestSecurityMappedAddressTests
    {
        // Control: the plain dotted-quad forms must be refused. If any of these start
        // passing, the blocklist is not wired up and every other result here is void.
        [Theory]
        [InlineData("http://10.0.0.0/payload")]
        [InlineData("http://192.168.0.0/payload")]
        [InlineData("http://172.16.9.9/payload")]
        [InlineData("http://169.254.169.254/latest/meta-data/")]
        [InlineData("http://127.0.0.1/payload")]
        [InlineData("https://127.0.0.1/payload")]
        public void PlainPrivateAndLoopbackLiterals_AreRefused(string url)
        {
            var allowed = OutboundRequestSecurity.TryValidateExternalHttpUrl(url, out var reason);

            Assert.False(allowed);
            Assert.Equal("Private or loopback IP targets are not allowed", reason);
        }

        // Control: an ordinary public host must be allowed. If this starts failing, the
        // validator is refusing everything and the refusals above prove nothing.
        [Theory]
        [InlineData("https://example.com/cover.jpg")]
        [InlineData("http://archive.org/download/thing/thing.m4b")]
        public void PublicHosts_AreAllowed(string url)
        {
            var allowed = OutboundRequestSecurity.TryValidateExternalHttpUrl(url, out var reason);

            Assert.True(allowed, $"expected allow, got refusal: {reason}");
            Assert.Equal(string.Empty, reason);
        }

        // Loopback survives the mapped form, because IsLoopback maps down to v4 first.
        [Theory]
        [InlineData("http://[::1]/payload")]
        [InlineData("http://[::ffff:127.0.0.1]/payload")]
        [InlineData("http://[::ffff:7f00:1]/payload")]
        public void LoopbackInEveryForm_IsRefused(string url)
        {
            var allowed = OutboundRequestSecurity.TryValidateExternalHttpUrl(url, out var reason);

            Assert.False(allowed);
            Assert.Equal("Private or loopback IP targets are not allowed", reason);
        }

        // The case under test. IsPrivateOrLoopback only maps down inside its loopback
        // check, so a mapped private v4 address takes the InterNetworkV6 branch, fails
        // the link-local / site-local / unique-local tests, and is reported as public.
        [Theory]
        [InlineData("http://[::ffff:10.0.0.0]/payload")]
        [InlineData("http://[::ffff:192.168.0.0]/payload")]
        [InlineData("http://[::ffff:172.16.9.9]/payload")]
        [InlineData("http://[::ffff:169.254.169.254]/latest/meta-data/")]
        [InlineData("http://[::ffff:ac10:909]/payload")]
        public void MappedPrivateLiterals_AreCurrentlyAllowed(string url)
        {
            var allowed = OutboundRequestSecurity.TryValidateExternalHttpUrl(url, out var reason);

            Assert.True(allowed, $"expected the current behaviour (allow), got refusal: {reason}");
        }

        // The step the whole question turned on: Uri.Host keeps the brackets, but
        // IPAddress.TryParse accepts a bracketed literal, so control does reach the
        // helper rather than falling through to DNS resolution. The compressed hex
        // form canonicalizes to the same host as the dotted form.
        [Theory]
        [InlineData("http://[::ffff:172.16.9.9]/payload", "[::ffff:172.16.9.9]", "::ffff:172.16.9.9")]
        [InlineData("http://[::ffff:ac10:909]/payload", "[::ffff:172.16.9.9]", "::ffff:172.16.9.9")]
        [InlineData("http://[::1]/payload", "[::1]", "::1")]
        public void BracketedHost_ParsesAsAnIpAddress(string url, string expectedHost, string expectedAddress)
        {
            var uri = new Uri(url);

            Assert.Equal(UriHostNameType.IPv6, uri.HostNameType);
            Assert.Equal(expectedHost, uri.Host);
            Assert.True(IPAddress.TryParse(uri.Host, out var ip));
            Assert.Equal(expectedAddress, ip!.ToString());
        }

        // Where the helper actually diverges, isolated from the URL plumbing.
        [Fact]
        public void IsPrivateOrLoopback_DisagreesBetweenTheTwoFormsOfTheSameAddress()
        {
            var plain = IPAddress.Parse("172.16.9.9");
            var mapped = IPAddress.Parse("::ffff:172.16.9.9");

            Assert.True(mapped.IsIPv4MappedToIPv6);
            Assert.Equal(plain, mapped.MapToIPv4());

            Assert.True(SecurityRequestUtils.IsPrivateOrLoopback(plain));
            Assert.False(SecurityRequestUtils.IsPrivateOrLoopback(mapped));
        }

        // Step 3: the near-identical private copy in the image cache maps down BEFORE the
        // family split, so it refuses both forms. The shared helper is the one that drifted.
        [Theory]
        [InlineData("http://10.0.0.0/cover.jpg")]
        [InlineData("http://[::ffff:10.0.0.0]/cover.jpg")]
        [InlineData("http://172.16.9.9/cover.jpg")]
        [InlineData("http://[::ffff:172.16.9.9]/cover.jpg")]
        [InlineData("http://169.254.169.254/cover.jpg")]
        [InlineData("http://[::ffff:169.254.169.254]/cover.jpg")]
        [InlineData("http://127.0.0.1/cover.jpg")]
        [InlineData("http://[::ffff:127.0.0.1]/cover.jpg")]
        public void ImageValidatorSibling_RefusesBothForms(string url)
        {
            var allowed = ImageDownloadValidator.TryValidateExternalImageUrl(url, out var reason);

            Assert.False(allowed);
            Assert.Equal("Private or loopback IP targets are not allowed", reason);
        }

        // Control for the sibling: it is not simply refusing everything.
        [Fact]
        public void ImageValidatorSibling_AllowsAPublicHost()
        {
            Assert.True(ImageDownloadValidator.TryValidateExternalImageUrl(
                "https://example.com/cover.jpg", out var reason), reason);
        }

        // Side effect of the same defect, on the inbound side. Kestrel on a dual-stack
        // socket reports an IPv4 peer as the mapped form, so a genuine private-network
        // client reads as public. That direction denies access rather than granting it.
        [Fact]
        public void InboundPrivateNetworkCheck_FailsClosedOnTheMappedForm()
        {
            var lanPeerAsKestrelReportsIt = IPAddress.Parse("::ffff:172.16.9.9");

            Assert.False(SecurityRequestUtils.IsPrivateOrLoopback(lanPeerAsKestrelReportsIt));
            Assert.True(SecurityRequestUtils.IsLoopback(IPAddress.Parse("::ffff:127.0.0.1")));
        }

        // The DNS-path validator shares the same helper and the same literal shortcut,
        // so it does not catch the mapped form either.
        [Fact]
        public async Task ResolvedValidator_AlsoAllowsTheMappedForm()
        {
            var logger = new Mock<ILogger>().Object;

            Assert.False(await OutboundRequestSecurity.TryValidateResolvedExternalHttpUriAsync(
                new Uri("http://172.16.9.9/payload"), logger));

            Assert.True(await OutboundRequestSecurity.TryValidateResolvedExternalHttpUriAsync(
                new Uri("http://[::ffff:172.16.9.9]/payload"), logger));
        }
    }
}
