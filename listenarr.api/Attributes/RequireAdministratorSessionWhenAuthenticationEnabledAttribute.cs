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

using Listenarr.Application.Interfaces;
using Listenarr.Application.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Listenarr.Api.Attributes
{
    /// <summary>
    /// Requires the caller to hold an active administrator session (not an API key) when
    /// authentication is enabled. Used to protect endpoints that must only be accessible
    /// via interactive login — for example, account management and API key CRUD operations.
    /// When authentication is disabled, all requests are allowed.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class RequireAdministratorSessionWhenAuthenticationEnabledAttribute : TypeFilterAttribute
    {
        /// <summary>Initialises a new instance of <see cref="RequireAdministratorSessionWhenAuthenticationEnabledAttribute"/>.</summary>
        public RequireAdministratorSessionWhenAuthenticationEnabledAttribute()
            : base(typeof(RequireAdministratorSessionWhenAuthenticationEnabledFilter))
        {
        }
    }

    /// <summary>Action filter that backs <see cref="RequireAdministratorSessionWhenAuthenticationEnabledAttribute"/>.</summary>
    public sealed class RequireAdministratorSessionWhenAuthenticationEnabledFilter : IAsyncActionFilter
    {
        private readonly IAuthenticationRequirementService _authenticationRequirementService;

        public RequireAdministratorSessionWhenAuthenticationEnabledFilter(IAuthenticationRequirementService authenticationRequirementService)
        {
            _authenticationRequirementService = authenticationRequirementService;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (!_authenticationRequirementService.IsAuthenticationRequired())
            {
                await next();
                return;
            }

            var user = context.HttpContext.User;
            if (user?.Identity?.IsAuthenticated == true &&
                user.IsInRole("Administrator") &&
                !SecurityRequestUtils.IsApiKeyAuthenticated(context.HttpContext))
            {
                await next();
                return;
            }

            context.Result = user?.Identity?.IsAuthenticated == true
                ? new StatusCodeResult(StatusCodes.Status403Forbidden)
                : new UnauthorizedResult();
        }
    }
}
