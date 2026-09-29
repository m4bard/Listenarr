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

using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Listenarr.Api.Startup;

public static class ListenarrBuilderFactory
{
    public static WebApplicationBuilder Create(
        string[] args,
        ILogEventSink realtimeLogSink,
        IFileSystem fileSystem)
    {
        var contentRootPath = ResolveContentRootPath();
        var environmentName = ResolveEnvironmentName();

        if (string.Equals("Test", environmentName, StringComparison.Ordinal))
        {
            var testContentRootPath = Path.Combine(Path.GetTempPath(), "ListenarrTests");
            fileSystem.CreateDirectory(testContentRootPath);
            Environment.SetEnvironmentVariable("LISTENARR_CONTENT_ROOT", testContentRootPath);
            contentRootPath = testContentRootPath;
        }

        contentRootPath = ApplyContentRootOverride(contentRootPath, fileSystem);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = contentRootPath,
            EnvironmentName = environmentName
        });

        EnsureExternalConfiguration(builder.Environment.ContentRootPath, fileSystem);
        builder.Configuration.AddJsonFile(
            Path.Join("config", "appsettings", "appsettings.json"),
            optional: true,
            reloadOnChange: true);

        ConfigureSerilog(builder, realtimeLogSink);
        ConfigureDefaultUrls(builder, args);

        return builder;
    }

    private static string ResolveEnvironmentName()
    {
        var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        if (string.IsNullOrEmpty(environmentName))
        {
            environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        }

        if (string.IsNullOrEmpty(environmentName))
        {
            environmentName = "Production";
        }

        var processName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
        if (string.Equals(processName, "testhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Environment.GetEnvironmentVariable("LISTENARR_TEST_MODE"), "true", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VSTEST_SESSION_ID")) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTNET_TEST_RUNNER")))
        {
            environmentName = "Test";
        }

        return environmentName;
    }

    private static string ResolveContentRootPath()
        => AppContext.BaseDirectory;

    private static string ApplyContentRootOverride(string contentRootPath, IFileSystem fileSystem)
    {
        var contentRootOverride = Environment.GetEnvironmentVariable("LISTENARR_CONTENT_ROOT");
        if (string.IsNullOrWhiteSpace(contentRootOverride))
        {
            return contentRootPath;
        }

        try
        {
            contentRootOverride = Path.GetFullPath(contentRootOverride);

            if (!fileSystem.DirectoryExists(contentRootOverride))
            {
                Console.WriteLine($"[Listenarr] LISTENARR_CONTENT_ROOT '{contentRootOverride}' does not exist; creating it");
                fileSystem.CreateDirectory(contentRootOverride);
            }

            return contentRootOverride;
        }
        catch (Exception)
        {
            Console.WriteLine($"[Listenarr] Error: LISTENARR_CONTENT_ROOT '{contentRootOverride}' cannot be used or created; ignoring override.");
            return contentRootPath;
        }
    }

    internal static void EnsureExternalConfiguration(string contentRootPath, IFileSystem fileSystem)
    {
        var externalConfigRelative = Path.Join("config", "appsettings", "appsettings.json");
        var externalConfigAbsolute = Path.Join(contentRootPath, externalConfigRelative);

        try
        {
            var dir = Path.GetDirectoryName(externalConfigAbsolute) ?? string.Empty;
            if (!fileSystem.DirectoryExists(dir)) fileSystem.CreateDirectory(dir);

            if (!fileSystem.FileExists(externalConfigAbsolute))
            {
                if (!fileSystem.TryValidateMutationTarget(
                        externalConfigAbsolute,
                        [contentRootPath],
                        out var safeExternalConfigAbsolute,
                        out var reason))
                {
                    throw new IOException(
                        $"External config path is outside the resolved content root: {LogRedaction.SanitizeText(reason)}");
                }

                var defaultJson = "{\n  \"Serilog\": {\n    \"MinimumLevel\": {\n      \"Default\": \"Information\",\n      \"Override\": {\n        \"Microsoft\": \"Warning\",\n        \"System\": \"Warning\"\n      }\n    }\n  }\n}";
                fileSystem.WriteAllText(safeExternalConfigAbsolute, defaultJson);
                Console.WriteLine($"[Listenarr] Created default configuration at '{safeExternalConfigAbsolute}'. Edit this file to customize app settings.");
            }
        }
        catch (Exception ex) when (
            ex is IOException
            || ex is UnauthorizedAccessException
            || ex is System.Security.SecurityException
            || ex is ArgumentException
            || ex is NotSupportedException)
        {
            Console.WriteLine($"[Listenarr] Warning: failed to create default config '{externalConfigRelative}': {ex.Message}");
        }
    }

    /// <summary>
    /// Accepted names for <c>LISTENARR_LOG_LEVEL</c> and the Serilog/Logging configuration
    /// level keys, as shown in the warning when a non-empty value is not recognized.
    /// </summary>
    internal const string AcceptedLogLevelNamesMessage =
        "Verbose (or Trace), Debug, Information, Warning, Error, Fatal (or Critical)";

    /// <summary>
    /// Parses a log level name into a Serilog <see cref="LogEventLevel"/>. Serilog's own levels
    /// are Verbose, Debug, Information, Warning, Error, Fatal; there is no <c>Trace</c> or
    /// <c>Critical</c> member. Those two names are accepted case-insensitively as aliases for
    /// <see cref="LogEventLevel.Verbose"/> and <see cref="LogEventLevel.Fatal"/> respectively,
    /// because they are exactly the most- and least-verbose level names in
    /// Microsoft.Extensions.Logging, and anyone arriving from that convention will type
    /// <c>Trace</c> expecting the most verbose logging, not silently get less of it.
    /// </summary>
    /// <remarks>
    /// Microsoft.Extensions.Logging also defines a <c>None</c> level meaning "no logging at
    /// all". It is deliberately NOT accepted here: Serilog's <see cref="LogEventLevel"/> has no
    /// member representing that, and mapping it onto any real level (Fatal being the closest)
    /// would still emit log lines while claiming to emit none, which is a different flavor of
    /// the exact silent-surprise bug this method exists to remove.
    /// </remarks>
    internal static bool TryParseLogEventLevel(string? value, out LogEventLevel level)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            level = default;
            return false;
        }

        var trimmed = value.Trim();

        if (trimmed.Equals("Trace", StringComparison.OrdinalIgnoreCase))
        {
            level = LogEventLevel.Verbose;
            return true;
        }

        if (trimmed.Equals("Critical", StringComparison.OrdinalIgnoreCase))
        {
            level = LogEventLevel.Fatal;
            return true;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out level);
    }

    private static LogEventLevel ResolveMinimumLevel(string? logLevelEnv, string? configLevel)
    {
        if (!string.IsNullOrWhiteSpace(logLevelEnv))
        {
            if (TryParseLogEventLevel(logLevelEnv, out var parsedFromEnv))
            {
                return parsedFromEnv;
            }

            Console.WriteLine(
                $"[Listenarr] Warning: LISTENARR_LOG_LEVEL '{logLevelEnv}' was not recognized; accepted values are {AcceptedLogLevelNamesMessage}.");
        }

        if (!string.IsNullOrWhiteSpace(configLevel))
        {
            if (TryParseLogEventLevel(configLevel, out var parsedFromConfig))
            {
                return parsedFromConfig;
            }

            Console.WriteLine(
                $"[Listenarr] Warning: log level '{configLevel}' was not recognized; accepted values are {AcceptedLogLevelNamesMessage}.");
        }

        return LogEventLevel.Information;
    }

    private static void ConfigureSerilog(WebApplicationBuilder builder, ILogEventSink realtimeLogSink)
    {
        var logFilePath = Path.Join(builder.Environment.ContentRootPath, "config", "logs", "listenarr-.log");
        var logLevelEnv = Environment.GetEnvironmentVariable("LISTENARR_LOG_LEVEL");
        var configLevel = builder.Configuration["Serilog:MinimumLevel:Default"] ?? builder.Configuration["Logging:LogLevel:Default"];

        var minimumLevel = ResolveMinimumLevel(logLevelEnv, configLevel);

        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Machine", Environment.MachineName)
            .Enrich.WithProperty("ProcessId", Environment.ProcessId)
            .Enrich.WithProperty("Application", "Listenarr.Api")
            .MinimumLevel.Is(minimumLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .WriteTo.Console(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 5,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Sink(realtimeLogSink)
            .CreateLogger();

        builder.Host.UseSerilog();

        if (builder.Environment.IsEnvironment("Test"))
        {
            Log.Logger = Serilog.Core.Logger.None;
        }

    }

    private static void ConfigureDefaultUrls(WebApplicationBuilder builder, string[] args)
    {
        var hasUrlsArg = args.Any(arg =>
            arg.Equals("--urls", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--urls=", StringComparison.OrdinalIgnoreCase));
        var hasUrlsConfig =
            !string.IsNullOrWhiteSpace(builder.Configuration["urls"]) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTNET_URLS")) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("URLS"));

        if (!hasUrlsArg && !hasUrlsConfig)
        {
            builder.WebHost.UseUrls("http://*:4545");
        }
    }
}
