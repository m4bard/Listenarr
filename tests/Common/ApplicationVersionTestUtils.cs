using System.Reflection;

namespace Listenarr.Tests.Common
{
    public static class ApplicationVersionTestUtils
    {
        public static string GetExpectedApiVersion()
        {
            var version = typeof(global::Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidOperationException("Program assembly informational version is missing.");
            }

            var metadataIndex = version.IndexOf('+');
            return metadataIndex > 0
                ? version[..metadataIndex]
                : version;
        }
    }
}
