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

using System.Reflection;
using Listenarr.Application.Common;
using Xunit;

namespace Listenarr.Tests.Features.Application.Common
{
    public class ApplicationVersionResolverTests
    {
        [Fact]
        public void Resolve_UsesPreferredAssemblyInformationalVersion()
        {
            var expectedVersion = GetExpectedApiVersion();
            var version = ApplicationVersionResolver.Resolve(typeof(global::Program).Assembly.GetName().Name);

            Assert.Equal(expectedVersion, version);
            Assert.NotEqual("1.0.0.0", version);
        }

        private static string GetExpectedApiVersion()
        {
            var version = typeof(global::Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            Assert.False(string.IsNullOrWhiteSpace(version));

            var metadataIndex = version.IndexOf('+');
            return metadataIndex > 0
                ? version[..metadataIndex]
                : version;
        }
    }
}
