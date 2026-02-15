/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2025 Robbie Davis
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

using Listenarr.Api.Services;
using Listenarr.Api.Services.Search;
using Listenarr.Api.Services.Search.Filters;
using Listenarr.Api.Services.Search.Strategies;
using Listenarr.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using System.Linq;
using Listenarr.Api.Middleware;
using System.Net;
using Listenarr.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;
using Serilog;
using Serilog.Events;
using Polly;
using Polly.Extensions.Http;
using Listenarr.Api.Extensions;
using Listenarr.Infrastructure.Extensions;

// Check for special CLI helpers before building the web host
// Pass a non-null args array to satisfy nullable analysis
var builder = WebApplication.CreateBuilder(args ?? Array.Empty<string>());

// Configure Serilog for structured logging, file rotation and SignalR broadcasting
var logFilePath = Path.Combine(builder.Environment.ContentRootPath, "config", "logs", "listenarr-.log");
var signalRSink = new SignalRLogSink();
// Prefer explicit environment variable (useful for Docker/runtime overrides)
var logLevelEnv = Environment.GetEnvironmentVariable("LISTENARR_LOG_LEVEL");

// Ensure an external config file in 'config/appsettings/appsettings.json' is available and registered.
// If the file does not exist on first startup, create a default one so non-Docker users have a place to customize.
var externalConfigRelative = Path.Combine("config", "appsettings", "appsettings.json");
var externalConfigAbsolute = Path.Combine(builder.Environment.ContentRootPath, externalConfigRelative);
try
{
    var dir = Path.GetDirectoryName(externalConfigAbsolute) ?? string.Empty;
    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

    if (!File.Exists(externalConfigAbsolute))
    {
        // Minimal, safe default configuration (non-sensitive)
        var defaultJson = "{\n  \"Serilog\": {\n    \"MinimumLevel\": {\n      \"Default\": \"Information\",\n      \"Override\": {\n        \"Microsoft\": \"Warning\",\n        \"System\": \"Warning\"\n      }\n    }\n  }\n}";
        File.WriteAllText(externalConfigAbsolute, defaultJson);
        Console.WriteLine($"[Listenarr] Created default configuration at '{externalConfigRelative}'. Edit this file to customize app settings.");
    }
}
catch (Exception ex)
{
    // Do not fail startup on inability to write sample config; just log to console and continue
    Console.WriteLine($"[Listenarr] Warning: failed to create default config '{externalConfigRelative}': {ex.Message}");
}

// Register the external config file (relative path is resolved against ContentRootPath)
builder.Configuration.AddJsonFile(externalConfigRelative, optional: true, reloadOnChange: true);

// Allow configuration files to also specify the minimum level (e.g., appsettings.json or appsettings.Development.json)
var configLevel = builder.Configuration["Serilog:MinimumLevel:Default"] ?? builder.Configuration["Logging:LogLevel:Default"];

LogEventLevel minimumLevel;
if (!string.IsNullOrWhiteSpace(logLevelEnv) && Enum.TryParse<LogEventLevel>(logLevelEnv, ignoreCase: true, out var parsedFromEnv))
{
    minimumLevel = parsedFromEnv;
}
else if (!string.IsNullOrWhiteSpace(configLevel) && Enum.TryParse<LogEventLevel>(configLevel, ignoreCase: true, out var parsedFromConfig))
{
    minimumLevel = parsedFromConfig;
}
else
{
    minimumLevel = LogEventLevel.Information;
}

// Industry-standard defaults:
// - Application logs at Information (unless overridden)
// - Third-party and framework logs (Microsoft/System) at Warning
// - EF Core DB command logging elevated to Warning by default (can be lowered to Debug for troubleshooting)
Log.Logger = new Serilog.LoggerConfiguration()
    .Enrich.FromLogContext()
    // Use explicit properties to avoid optional enrichers that may not be present in all builds
    .Enrich.WithProperty("Machine", Environment.MachineName)
    .Enrich.WithProperty("ProcessId", Environment.ProcessId)
    .Enrich.WithProperty("Application", "Listenarr.Api")
    .MinimumLevel.Is(minimumLevel)
    // Framework and system noise should be at Warning by default
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    // EF Core: keep DB command messages higher than app logs; changeable via configuration when needed
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    // Enable debug logging for Transmission adapter to troubleshoot RPC issues
    .MinimumLevel.Override("Listenarr.Api.Services.Adapters.TransmissionAdapter", LogEventLevel.Debug)
    // Console sink for developer-friendly output (includes SourceContext for quick tracing)
    .WriteTo.Console(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    // Primary file sink with daily rolling and structured JSON compatible output template
    .WriteTo.File(
        logFilePath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 5,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.Sink(signalRSink)
    .CreateLogger();

// Use Serilog for logging
builder.Host.UseSerilog();

// Configure URLs to listen on port 4545 (main Listenarr port) - can be overridden by --urls
if (!args?.Any(arg => arg.StartsWith("--urls")) ?? true)
{
    builder.WebHost.UseUrls("http://*:4545");
}

// Configure logging is now handled by Serilog above

// Add services to the container.
// If running as an integration test host, allow the test-side partial to apply any
// additional registrations (for example AddListenarrPersistence so IDbContextFactory<>
// is available to hosted/background services during tests).
if (builder.Environment.IsEnvironment("Testing"))
{
    ApplyTestHostPatches(builder);
}
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Serialize enums as strings instead of integers for better frontend compatibility
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        // Only ignore null values (not empty strings or zeros) to reduce payload size while preserving meaningful empty values
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

// Add SignalR for real-time updates
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        // Serialize enums as strings for SignalR messages too
        options.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

// RootFolder repository + service
builder.Services.AddScoped<Listenarr.Api.Repositories.IRootFolderRepository, Listenarr.Api.Repositories.EfRootFolderRepository>();
builder.Services.AddScoped<Listenarr.Api.Services.IRootFolderService, Listenarr.Api.Services.RootFolderService>();
// Migrator for legacy single-outputPath -> RootFolder migration
builder.Services.AddScoped<Listenarr.Api.Services.ILegacyOutputPathMigrator, Listenarr.Api.Services.LegacyOutputPathMigrator>();

// History repository for tracking events
builder.Services.AddScoped<Listenarr.Infrastructure.Repositories.IHistoryRepository, Listenarr.Infrastructure.Repositories.HistoryRepository>();

// Download history service for idempotency and audit trail
builder.Services.AddScoped<Listenarr.Application.Services.IDownloadHistoryService, Listenarr.Infrastructure.Services.DownloadHistoryService>();

// Add in-memory cache for metadata prefetch / reuse
builder.Services.AddMemoryCache();

// Add HTTP client for Audimeta service
builder.Services.AddHttpClient<AudimetaService>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All
    });

// Add HTTP client for Audnexus service
builder.Services.AddHttpClient<IAudnexusService, AudnexusService>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All
    });

// Metadata routing across providers
builder.Services.AddScoped<IAudiobookMetadataService, AudiobookMetadataService>();

// Add metadata converters helper
builder.Services.AddScoped<MetadataConverters>();
builder.Services.AddScoped<MetadataMerger>();
builder.Services.AddScoped<SearchProgressReporter>();

// Add search result filters
builder.Services.AddScoped<ISearchResultFilter, KindleEditionFilter>();
builder.Services.AddScoped<ISearchResultFilter, AudiobookOnlyFilter>();
builder.Services.AddScoped<ISearchResultFilter, PromotionalTitleFilter>();
builder.Services.AddScoped<ISearchResultFilter, ProductLikeTitleFilter>();
builder.Services.AddScoped<ISearchResultFilter, MissingInformationFilter>();
builder.Services.AddScoped<SearchResultFilterPipeline>();

// Add metadata fetching strategies
builder.Services.AddScoped<IMetadataStrategy, AudimetaStrategy>();
builder.Services.AddScoped<IMetadataStrategy, AudnexusStrategy>();
builder.Services.AddScoped<MetadataStrategyCoordinator>();

// Add ASIN candidate collector
builder.Services.AddScoped<AsinCandidateCollector>();

// Add ASIN enricher
builder.Services.AddScoped<AsinEnricher>();

// Add fallback scraper
// Add search result scorer
builder.Services.AddScoped<SearchResultScorer>();

// Add ASIN search handler
builder.Services.AddScoped<AsinSearchHandler>();

// Add default HTTP client for other services
builder.Services.AddHttpClient();

// Add named HttpClient for download operations (qBittorrent, Transmission, etc.)
// Prevents socket exhaustion by reusing connections
builder.Services.AddHttpClient("DownloadClient")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        UseCookies = false // Cookies handled per-request for multi-tenant scenarios
    })
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(
            handledEventsAllowedBeforeBreaking: 3,
            durationOfBreak: TimeSpan.FromSeconds(30),
            onBreak: (outcome, duration) =>
            {
                Log.Logger.Warning("[CIRCUIT BREAKER] Download client circuit opened due to {Reason}. Breaking for {Seconds}s", outcome.Exception?.Message ?? "policy trigger", duration.TotalSeconds);
            },
            onReset: () =>
            {
                Log.Logger.Information("[CIRCUIT BREAKER] Download client circuit reset - service recovered");
            }
        ))
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
            onRetry: (outcome, timespan, retryAttempt, context) =>
            {
                Log.Logger.Information("[RETRY] Download client retry attempt {Attempt} after {Delay}s delay", retryAttempt, timespan.TotalSeconds);
            }
        ));

// Bind download client definitions from configuration and expose via IOptions
builder.Services.Configure<Listenarr.Api.Services.Adapters.DownloadClientsOptions>(builder.Configuration.GetSection("DownloadClients"));

// Validate download client configuration at startup, surface errors early
builder.Services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<Listenarr.Api.Services.Adapters.DownloadClientsOptions>, Listenarr.Api.Services.Adapters.DownloadClientsOptionsValidator>();

// Register named HttpClients for each adapter type so adapter implementations can request the appropriately-configured client.
// qbittorrent
builder.Services.AddHttpClient("qbittorrent")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        UseCookies = false
    })
    .SetHandlerLifetime(TimeSpan.FromMinutes(5))
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(
            handledEventsAllowedBeforeBreaking: 3,
            durationOfBreak: TimeSpan.FromSeconds(30),
            onBreak: (outcome, duration) =>
            {
                Log.Logger.Warning("[CIRCUIT BREAKER] qbittorrent client circuit opened due to {Reason}. Breaking for {Seconds}s", outcome.Exception?.Message ?? "policy trigger", duration.TotalSeconds);
            },
            onReset: () =>
            {
                Log.Logger.Information("[CIRCUIT BREAKER] qbittorrent client circuit reset - service recovered");
            }
        ))
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
            onRetry: (outcome, timespan, retryAttempt, context) =>
            {
                Log.Logger.Information("[RETRY] qbittorrent client retry attempt {Attempt} after {Delay}s delay", retryAttempt, timespan.TotalSeconds);
            }
        ));

// transmission
builder.Services.AddHttpClient("transmission")
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        UseCookies = false
    })
    .SetHandlerLifetime(TimeSpan.FromMinutes(5))
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(3, TimeSpan.FromSeconds(30)))
    .AddPolicyHandler(HttpPolicyExtensions.HandleTransientHttpError()
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

// sabnzbd
builder.Services.AddHttpClient("sabnzbd")
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        UseCookies = false
    })
    .SetHandlerLifetime(TimeSpan.FromMinutes(5))
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(3, TimeSpan.FromSeconds(30)))
    .AddPolicyHandler(HttpPolicyExtensions.HandleTransientHttpError()
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

// nzbget
builder.Services.AddHttpClient("nzbget")
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        UseCookies = false
    })
    .SetHandlerLifetime(TimeSpan.FromMinutes(5))
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(3, TimeSpan.FromSeconds(30)))
    .AddPolicyHandler(HttpPolicyExtensions.HandleTransientHttpError()
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

// Adapter factory resolution is provided by `IDownloadClientAdapterFactory`.

// Register import item resolution service for V2 path resolution
builder.Services.AddScoped<Listenarr.Api.Services.IImportItemResolutionService, Listenarr.Api.Services.ImportItemResolutionService>();

// Add named HttpClient for direct downloads (DDL)
builder.Services.AddHttpClient("DirectDownload")
    .ConfigureHttpClient(client =>
    {


        // Allow up to 2 hours for large file downloads
        client.Timeout = TimeSpan.FromHours(2);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .Or<TaskCanceledException>()
        .CircuitBreakerAsync(
            handledEventsAllowedBeforeBreaking: 3,
            durationOfBreak: TimeSpan.FromMinutes(1),
            onBreak: (outcome, duration) =>
            {
                Log.Logger.Warning("[CIRCUIT BREAKER] Direct download circuit opened. Breaking for {Minutes}m", duration.TotalMinutes);
            },
            onReset: () =>
            {
                Log.Logger.Information("[CIRCUIT BREAKER] Direct download circuit reset");
            }
        ))
    .AddPolicyHandler(HttpPolicyExtensions
        .HandleTransientHttpError()
        .Or<TaskCanceledException>()
        .WaitAndRetryAsync(
            retryCount: 2,
            sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
        ));

// Register our custom services
// Compute an absolute path for the SQLite file based on the content root so
// the published exe will create/use the intended config/database path even
// when the working directory differs.
// Compute default SQLite DB path (config/database/listenarr.db) relative to content root.
var sqliteDbPath = Path.Combine(builder.Environment.ContentRootPath, "config", "database", "listenarr.db");
// Ensure directory exists at startup so EF migrations can create the DB file there
var sqliteDbDir = Path.GetDirectoryName(sqliteDbPath);
if (!string.IsNullOrEmpty(sqliteDbDir) && !Directory.Exists(sqliteDbDir))
{
    Directory.CreateDirectory(sqliteDbDir);
}

// Register persistence (DbContextFactory + compatibility DbContext + repositories) via extension
builder.Services.AddListenarrPersistence(builder.Configuration, sqliteDbPath);

// Register consolidated HttpClient policies and common typed clients
builder.Services.AddListenarrHttpClients(builder.Configuration);

// Register adapters and related options/validators
builder.Services.AddListenarrAdapters(builder.Configuration);

// Register infrastructure implementations (repositories live in the Infrastructure project)
builder.Services.AddListenarrInfrastructure();
// Register application-level services (moved from Program.cs to keep startup focused)
builder.Services.AddListenarrAppServices(builder.Configuration);
// Register hosted/background services (moved from Program.cs)
builder.Services.AddListenarrHostedServices(builder.Configuration);

// External request options (Prefer US domain / optional US proxy)
builder.Services.Configure<Listenarr.Api.Services.ExternalRequestOptions>(builder.Configuration.GetSection("ExternalRequests"));

// Named HttpClient for US-origin requests (can be configured to use a proxy)
builder.Services.AddHttpClient("us").ConfigurePrimaryHttpMessageHandler(() =>
{
    var handler = new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All
    };

    // Proxy configuration removed; keep handler default (no explicit proxy configuration)

    return handler;
});

// CORS is handled by reverse proxy (nginx, Traefik, Caddy, etc.)
// Only add CORS support for local development
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("DevOnly",
            policy =>
            {
                policy.WithOrigins(
                        "http://localhost:5173",
                        "https://localhost:5173",
                        "http://127.0.0.1:5173",
                        "https://127.0.0.1:5173"
                    )
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials(); // Required for SignalR
            });
    });
}

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Try to include XML comments if available
    try
    {
        var xmlFile = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name + ".xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
        {
            options.IncludeXmlComments(xmlPath);
        }
    }
    catch (Exception ex)
    {
        Log.Logger.Warning("[WARNING] Failed to include XML comments in Swagger: {Message}", ex.Message);
    }
    // Use full type names for schema Ids (replace '+' from nested types with '.') to
    // avoid collisions between nested controller DTOs and top-level DTOs that share
    // the same simple type name (e.g. TranslatePathRequest).
    options.CustomSchemaIds(type => (type.FullName ?? type.Name).Replace('+', '.'));

        // Resolve conflicting actions (ambiguous HTTP method actions) by selecting the first
        // description. This prevents Swagger generation failures when multiple action descriptors
        // map to similar routes. If more complex disambiguation is needed in future, refine here.
        options.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
    });

// whether the cookie is marked secure by forwarding the original scheme (X-Forwarded-Proto).
// Override via configuration: Antiforgery:Cookie:SecurePolicy = None|SameAsRequest|Always
var antiforgeryCookiePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
var cfgPolicy = builder.Configuration["Antiforgery:Cookie:SecurePolicy"];
if (!string.IsNullOrWhiteSpace(cfgPolicy) && Enum.TryParse<Microsoft.AspNetCore.Http.CookieSecurePolicy>(cfgPolicy, true, out var parsedPolicy))
{
    antiforgeryCookiePolicy = parsedPolicy;
}
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.SecurePolicy = antiforgeryCookiePolicy;
    options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict;
});

// Log guidance on startup when running in production so self-hosters know requirements
if (builder.Environment.IsProduction())
{
    Log.Logger.Information("Antiforgery cookie SecurePolicy set to {Policy}. Ensure the app runs behind HTTPS or forwards X-Forwarded-Proto from a TLS-terminating proxy.", antiforgeryCookiePolicy);
}

// During local development we often run the frontend on a different port via Vite
// and use plain HTTP. Ensure antiforgery cookie can be set in that scenario by
// relaxing the SecurePolicy and SameSite settings when running in Development.
// NOTE: CodeQL flags SecurePolicy.None as a security issue, but this is acceptable
// in Development environment where HTTPS is not available. Production uses SameAsRequest
// (configured above) which properly enforces Secure flag when behind HTTPS reverse proxy.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddAntiforgery(options =>
    {
        options.HeaderName = "X-XSRF-TOKEN";
        // Allow the antiforgery cookie to be sent over plain HTTP during local dev
        // This is safe because Development environment is not exposed to external traffic
        options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.None; // lgtm[cs/cookie-secure-policy-none]
        // During local development the frontend often runs on a different origin
        // (Vite dev server). Use SameSite=Lax so the browser will accept the
        // cookie for same-site requests to the Vite dev server while avoiding
        // the requirement to set Secure (which would require HTTPS). In our
        // setup the dev server proxies /api requests, so Lax is sufficient and
        // more compatible with local HTTP development.
        options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
    });
}

// Persist Data Protection keys to disk so antiforgery tokens/cookies remain valid
// across process restarts and between instances during local development.
// This avoids issues where tokens are protected with an ephemeral key ring and
// cannot be validated later.
{
    var keyDir = Path.Combine(builder.Environment.ContentRootPath, "config", "dataprotection-keys");
    if (!System.IO.Directory.Exists(keyDir)) System.IO.Directory.CreateDirectory(keyDir);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new System.IO.DirectoryInfo(keyDir))
        .SetApplicationName("Listenarr");
}

var app = builder.Build();

// Ensure database is created and migrations are applied
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ListenArrDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    // Ensure the database directory exists (use the absolute path computed above)
    string dbFullPath = sqliteDbPath;
    string? dbDirectory = Path.GetDirectoryName(dbFullPath);
    if (!string.IsNullOrEmpty(dbDirectory) && !Directory.Exists(dbDirectory))
    {
        Directory.CreateDirectory(dbDirectory);
    }

    try
    {
        logger.LogInformation("Checking EF Core migrations (available/applied/pending)...");

        try
        {
            var available = context.Database.GetMigrations().ToList();
            var applied = context.Database.GetAppliedMigrations().ToList();
            var pending = context.Database.GetPendingMigrations().ToList();

            logger.LogInformation("Available migrations: {Count}", available.Count);
            foreach (var m in available)
            {
                logger.LogInformation("  - {Migration}", m);
            }

            logger.LogInformation("Applied migrations: {Count}", applied.Count);
            foreach (var m in applied)
            {
                logger.LogInformation("  - {Migration}", m);
            }

            logger.LogInformation("Pending migrations: {Count}", pending.Count);
            foreach (var m in pending)
            {
                logger.LogInformation("  - {Migration}", m);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to enumerate EF Core migrations before migrating");
        }

        logger.LogInformation("Applying database migrations...");
        // Apply any pending migrations (including AddDefaultMetadataSources which adds Audimeta and Audnexus)
        context.Database.Migrate();
        logger.LogInformation("Database migrations applied successfully");

        // Apply SQLite PRAGMA settings after database is created
        SqlitePragmaInitializer.ApplyPragmas(context);
        logger.LogInformation("SQLite pragmas applied successfully");

            // Migrate legacy single-root configuration (ApplicationSettings.outputPath) into the new RootFolder table
            try
            {
                using var migrScope = app.Services.CreateScope();
                var migrator = migrScope.ServiceProvider.GetRequiredService<Listenarr.Api.Services.ILegacyOutputPathMigrator>();
                migrator.MigrateAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Legacy output path migration failed");
            }
        // Schema changes should be applied by EF migrations. See Migration:
        // Migrations/20251125103000_AddDownloadFinalizationSettingsToApplicationSettings.cs
    }
    catch (Exception ex)
    {
        // Migrations can fail when the database file exists but is missing expected
        // tables (for example, when an older DB file was copied into the publish
        // folder). In that case try the cheaper EnsureCreated() path which will
        // create tables for the current model if no __EFMigrationsHistory table
        // exists. If EnsureCreated() succeeds, log the fact and continue. If it
        // fails as well, log full details for debugging but continue so the host
        // can start (tests may override DbContext configuration).
        logger.LogError(ex, "Error during database migration attempt. Will try EnsureCreated() fallback.");

        try
        {
            logger.LogInformation("Attempting EnsureCreated() as a fallback...");
            var created = context.Database.EnsureCreated();
            if (created)
            {
                logger.LogInformation("Database created via EnsureCreated() fallback.");
            }
            else
            {
                logger.LogWarning("EnsureCreated() did not create the database (it may already exist but be missing migrations).");
            }

            // Try applying pragmas even if EnsureCreated() didn't create the schema
            SqlitePragmaInitializer.ApplyPragmas(context);
            logger.LogInformation("SQLite pragmas applied successfully (fallback path)");
        }
        catch (Exception innerEx)
        {
            // Log full details. We intentionally do not rethrow to avoid breaking
            // test harnesses that may run with an alternate DbContext.
            logger.LogError(innerEx, "EnsureCreated() fallback also failed during database initialization");
        }
    }
}

// Initialize the SignalR sink now that the hub context is available
signalRSink.Initialize(app.Services.GetRequiredService<IHubContext<LogHub>>());

// Ensure ffprobe is available on first launch (best-effort). Installation runs in background via
// the registered hosted service so the app can serve requests immediately.

// If the user passed --query-users, run the helper now that the DB is migrated and exit.
if (args is not null && args.Contains("--query-users"))
{
    QueryUsersProgram.Run();
    return;
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Use forwarded headers middleware (must be early in pipeline)
// This processes X-Forwarded-For and X-Forwarded-Proto headers from the reverse proxy
app.UseForwardedHeaders();

// Note: HTTPS redirection is handled by the reverse proxy, not by this application

// Serve frontend static files from wwwroot (index.html + assets)
// DefaultFiles enables serving index.html when requesting '/'
// Map `/placeholder.svg` to the frontend `fe/public/placeholder.svg` so the API
// serves the exact same placeholder image used by the frontend without
// modifying any frontend files.
var frontendPlaceholderPath = Path.Combine(app.Environment.ContentRootPath, "..", "fe", "public", "placeholder.svg");
app.MapGet("/placeholder.svg", async context =>
{
    try
    {
        if (File.Exists(frontendPlaceholderPath))
        {
            context.Response.ContentType = "image/svg+xml";
            context.Response.Headers["Cache-Control"] = "public, max-age=300";
            await context.Response.SendFileAsync(frontendPlaceholderPath);
            return;
        }

        // Fallback to backend wwwroot placeholder if the frontend file is not present
        var fallback = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "placeholder.svg");
        if (File.Exists(fallback))
        {
            context.Response.ContentType = "image/svg+xml";
            context.Response.Headers["Cache-Control"] = "public, max-age=300";
            await context.Response.SendFileAsync(fallback);
            return;
        }

        context.Response.StatusCode = 404;
    }
    catch
    {
        context.Response.StatusCode = 500;
    }
});

app.UseDefaultFiles();
app.UseStaticFiles();

// Serve cached images from config/cache/images directory
var cacheImagesPath = Path.Combine(app.Environment.ContentRootPath, "config", "cache", "images");
if (Directory.Exists(cacheImagesPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(cacheImagesPath),
        RequestPath = "/config/cache/images"
    });
}

// Ensure routing middleware is enabled so endpoint routing features (CORS, Authorization)
// can be applied by subsequent middleware. This must run before UseCors()/UseAuthorization().
app.UseRouting();

// Log incoming request bodies for POST/PUT/PATCH to aid debugging of client integrations
app.UseMiddleware<Listenarr.Api.Middleware.RequestBodyLoggingMiddleware>();

// Enable CORS only in development (production should use reverse proxy for CORS)
if (app.Environment.IsDevelopment())
{
    app.UseCors("DevOnly");
}
// Session-based authentication middleware
app.UseMiddleware<Listenarr.Api.Middleware.SessionAuthenticationMiddleware>();
// API key middleware: allows requests with a valid X-Api-Key or Authorization: ApiKey <key>
app.UseMiddleware<Listenarr.Api.Middleware.ApiKeyMiddleware>();
// Enforce authentication based on startup config
app.UseMiddleware<AuthenticationEnforcerMiddleware>();
// Validate antiforgery tokens for unsafe methods
app.UseMiddleware<Listenarr.Api.Middleware.AntiforgeryValidationMiddleware>();
app.UseAuthorization();

app.MapControllers();

// Map SignalR hub for real-time download updates
if (app.Environment.IsDevelopment())
{
    app.MapHub<DownloadHub>("/hubs/downloads").RequireCors("DevOnly");
    // Map SignalR hub for real-time log broadcasting
    app.MapHub<LogHub>("/hubs/logs").RequireCors("DevOnly");
    // Map SignalR hub for real-time settings updates
    app.MapHub<SettingsHub>("/hubs/settings").RequireCors("DevOnly");
}
else
{
    app.MapHub<DownloadHub>("/hubs/downloads");
    app.MapHub<LogHub>("/hubs/logs");
    // Map SignalR hub for real-time settings updates
    app.MapHub<SettingsHub>("/hubs/settings");
}

// SPA fallback: serve index.html for non-API routes so client-side routing works
app.MapFallbackToFile("index.html");

app.Run();
