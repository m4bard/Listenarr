using Listenarr.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace Listenarr.Api.Middleware
{
    public class AuthenticationEnforcerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IStartupConfigService _startupConfigService;
        private readonly ILogger<AuthenticationEnforcerMiddleware> _logger;

        public AuthenticationEnforcerMiddleware(RequestDelegate next, IStartupConfigService startupConfigService, ILogger<AuthenticationEnforcerMiddleware> logger)
        {
            _next = next;
            _startupConfigService = startupConfigService;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var method = context.Request.Method;
            var path = context.Request.Path.Value ?? string.Empty;
            var normalizedApiPath = NormalizeApiVersionedPath(path);
            _logger?.LogDebug("AuthenticationEnforcer: incoming request {Method} {Path}", method, path);

            var cfg = _startupConfigService.GetConfig();
            var authRequired = false;
            if (cfg != null && cfg.AuthenticationRequired != null)
            {
                if (bool.TryParse(cfg.AuthenticationRequired, out var b)) authRequired = b;
                else authRequired = cfg.AuthenticationRequired?.ToLower() == "enabled";
            }

            // Log logout requests specifically and include masked principal diagnostics
            if (normalizedApiPath.StartsWith("/api/account/logout", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var authenticated = context.User?.Identity?.IsAuthenticated ?? false;
                    string? nameMask = null;
                    int claimsCount = 0;
                    try
                    {
                        claimsCount = context.User?.Claims?.Count() ?? 0;
                        if (authenticated)
                        {
                            var pname = context.User?.Identity?.Name ?? context.User?.FindFirst("sub")?.Value ?? context.User?.FindFirst("name")?.Value;
                            if (!string.IsNullOrEmpty(pname)) nameMask = pname.Length <= 8 ? pname : pname.Substring(0, 8);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException) {
                        _logger?.LogDebug(ex, "Failed capturing logout principal diagnostics");
                    }

                    _logger?.LogInformation("Logout request detected - Method: {Method}, Path: {Path}, Authenticated={Authenticated}, PrincipalNameMask={NameMask}, PrincipalClaims={ClaimsCount}",
                        context.Request.Method, path, authenticated, nameMask, claimsCount);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException) {
                    System.Diagnostics.Debug.WriteLine($"AuthenticationEnforcerMiddleware logout logging failed: {ex.Message}");
                }
            }

            // If endpoint explicitly allows anonymous, skip enforcement
            var endpoint = context.GetEndpoint();
            if (endpoint?.Metadata?.GetMetadata<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>() != null)
            {
                await _next(context);
                return;
            }

            // Allow some public paths used by SPA and startup (swagger/ui, antiforgery token, and account login)
            if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)
                || normalizedApiPath.StartsWith("/api/antiforgery", StringComparison.OrdinalIgnoreCase)
                || normalizedApiPath.StartsWith("/api/account/login", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            // The startup config endpoint should only be public when authentication is not required
            if (normalizedApiPath.StartsWith("/api/configuration/startupconfig", StringComparison.OrdinalIgnoreCase) && !authRequired)
            {
                await _next(context);
                return;
            }

            // Serve SPA assets and client-side routes anonymously: if the request is not for an API or SignalR hub,
            // let the static file middleware or SPA fallback handle it. This avoids returning 401 for '/'.
            // Keep API and hub routes protected.
            if (!path.StartsWith("/api") && !path.StartsWith("/hubs"))
            {
                await _next(context);
                return;
            }

            if (authRequired && !(context.User?.Identity?.IsAuthenticated ?? false))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { message = "Authentication required" });
                return;
            }

            await _next(context);
        }

        private static string NormalizeApiVersionedPath(string path)
        {
            // Convert /api/v1/... (or /api/v1.0/...) -> /api/... for legacy path checks.
            if (!path.StartsWith("/api/v", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            var versionStart = "/api/v".Length;
            var slashAfterVersion = path.IndexOf('/', versionStart);
            if (slashAfterVersion <= 0)
            {
                return path;
            }

            return "/api" + path[slashAfterVersion..];
        }
    }
}

