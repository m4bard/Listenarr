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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Listenarr.Api.Filters
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class RequireApiKeyWhenAuthenticationEnabledAttribute : TypeFilterAttribute
    {
        public RequireApiKeyWhenAuthenticationEnabledAttribute()
            : base(typeof(RequireApiKeyWhenAuthenticationEnabledFilter))
        {
        }
    }

    public sealed class RequireApiKeyWhenAuthenticationEnabledFilter : IAsyncActionFilter
    {
        private readonly IAuthenticationRequirementService _authenticationRequirementService;

        public RequireApiKeyWhenAuthenticationEnabledFilter(IAuthenticationRequirementService authenticationRequirementService)
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

            if (SecurityRequestUtils.IsApiKeyAuthenticated(context.HttpContext))
            {
                await next();
                return;
            }

            context.Result = context.HttpContext.User?.Identity?.IsAuthenticated == true
                ? new StatusCodeResult(StatusCodes.Status403Forbidden)
                : new UnauthorizedResult();
        }
    }
}
