/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
namespace Listenarr.Api.Security;

public static class HttpSecurityRequestUtils
{
    public static bool IsLoopbackRequest(HttpContext? context)
    {
        var ip = context?.Connection?.RemoteIpAddress;
        if (ip == null)
        {
            return true;
        }

        return SecurityRequestUtils.IsLoopback(ip);
    }

    public static bool IsLocalOrPrivateRequest(HttpContext? context)
    {
        var ip = context?.Connection?.RemoteIpAddress;
        if (ip == null)
        {
            return true;
        }

        return SecurityRequestUtils.IsPrivateOrLoopback(ip);
    }

    public static bool IsAuthenticatedAdminOrApiKey(HttpContext? context)
    {
        var user = context?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (user.IsInRole("Administrator"))
        {
            return true;
        }

        var authMethod = user.FindFirst("AuthMethod")?.Value;
        return !string.IsNullOrWhiteSpace(authMethod)
               && string.Equals(authMethod, "ApiKey", StringComparison.Ordinal);
    }

    public static bool IsApiKeyAuthenticated(HttpContext? context)
    {
        var user = context?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var authMethod = user.FindFirst("AuthMethod")?.Value;
        return !string.IsNullOrWhiteSpace(authMethod)
               && string.Equals(authMethod, "ApiKey", StringComparison.Ordinal);
    }

    public static bool ShouldRedactSecretsForCaller(HttpContext? context)
    {
        if (IsAuthenticatedAdminOrApiKey(context))
        {
            return false;
        }

        // A private source address is not evidence of authorisation, so it only
        // stands in for a credential while the login screen is switched off.
        // That is the same condition RequireApiKeyManagementAccessFilter applies
        // to the API key endpoints: below it, private callers are trusted; above
        // it, everyone presents an admin session or the API key. Without this,
        // an operator who turns authentication on still hands every secret the
        // redactors cover to any caller whose packets arrive from RFC1918,
        // link-local or IPv6 ULA space.
        return IsAuthenticationRequired(context) || !IsLocalOrPrivateRequest(context);
    }

    // Unresolvable configuration redacts rather than discloses. The address
    // check above fails open on a null RemoteIpAddress and this deliberately
    // does not copy that.
    private static bool IsAuthenticationRequired(HttpContext? context)
    {
        var startupConfigService = context?.RequestServices?.GetService<IStartupConfigService>();
        return startupConfigService?.IsAuthenticationRequired() ?? true;
    }
}
