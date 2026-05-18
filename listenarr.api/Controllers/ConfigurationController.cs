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

using Listenarr.Api.Attributes;
using Listenarr.Application.Interfaces;
using Listenarr.Application.Notification;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Listenarr.Api.Controllers
{
    [ApiController]
    [Route("api/v{version:apiVersion}/configuration")]
    [RequireAdminOrApiKeyWhenAuthenticationEnabled]
    public partial class ConfigurationController : ControllerBase
    {
        private readonly IConfigurationService _configurationService;
        private readonly ILogger<ConfigurationController> _logger;
        private readonly IUserService _userService;
        private readonly IHubContext<SettingsHub> _settingsHub;
        private readonly IDownloadClientGateway _downloadClientGateway;
        private readonly NotificationService _notificationService;

        public ConfigurationController(
            IConfigurationService configurationService,
            ILogger<ConfigurationController> logger,
            IUserService userService,
            IHubContext<SettingsHub> settingsHub,
            IDownloadClientGateway downloadClientGateway,
            NotificationService notificationService)
        {
            _configurationService = configurationService;
            _logger = logger;
            _userService = userService;
            _settingsHub = settingsHub;
            _downloadClientGateway = downloadClientGateway;
            _notificationService = notificationService;
        }
    }
}
