using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Listenarr.Api.Services;

public static class SensitiveEndpointAccessGuard
{
    public static IActionResult? RequireLocalOrAdmin(HttpContext? context, ILogger? logger, string endpointName)
    {
        if (SecurityRequestUtils.IsLocalOrPrivateRequest(context) || SecurityRequestUtils.IsAuthenticatedAdminOrApiKey(context))
        {
            return null;
        }

        logger?.LogWarning(
            "Blocked sensitive endpoint {Endpoint} for remote unauthenticated caller from {RemoteIp}",
            endpointName,
            context?.Connection?.RemoteIpAddress?.ToString() ?? "unknown");

        return new ObjectResult(new
        {
            message = "This endpoint is restricted to localhost/private-network callers or an authenticated admin/API key."
        })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }
}
