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
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class RequireApiKeyManagementAccessAttribute : TypeFilterAttribute
    {
        public RequireApiKeyManagementAccessAttribute()
            : base(typeof(RequireApiKeyManagementAccessFilter))
        {
        }
    }

    public sealed class RequireApiKeyManagementAccessFilter : IAsyncActionFilter
    {
        private readonly IAuthenticationRequirementService _authenticationRequirementService;

        public RequireApiKeyManagementAccessFilter(IAuthenticationRequirementService authenticationRequirementService)
        {
            _authenticationRequirementService = authenticationRequirementService;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var httpContext = context.HttpContext;
            if (_authenticationRequirementService.IsAuthenticationRequired())
            {
                var user = httpContext.User;
                if (user?.Identity?.IsAuthenticated == true &&
                    user.IsInRole("Administrator") &&
                    !SecurityRequestUtils.IsApiKeyAuthenticated(httpContext))
                {
                    await next();
                    return;
                }

                context.Result = user?.Identity?.IsAuthenticated == true
                    ? new ObjectResult(new { message = "Administrator session required" }) { StatusCode = StatusCodes.Status403Forbidden }
                    : new UnauthorizedObjectResult(new { message = "Authentication required" });
                return;
            }

            if (SecurityRequestUtils.IsLocalOrPrivateRequest(httpContext))
            {
                await next();
                return;
            }

            context.Result = new ObjectResult(new
            {
                message = "API key management is only available from local or private-network clients when authentication is disabled"
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}
