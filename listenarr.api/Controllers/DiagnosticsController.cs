using Listenarr.Api.Services;
using Listenarr.Domain.Models;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DiagnosticsController : ControllerBase
    {
        private readonly IConfigurationService _configurationService;
        private readonly NotificationService _notificationService;
        private readonly ILogger<DiagnosticsController> _logger;

        public DiagnosticsController(IConfigurationService configurationService, NotificationService notificationService, ILogger<DiagnosticsController> logger)
        {
            _configurationService = configurationService;
            _notificationService = notificationService;
            _logger = logger;
        }

        public class TestNotificationRequest
        {
            public string? Trigger { get; set; }
            public object? Data { get; set; }
            public string? WebhookId { get; set; }
        }

        [HttpPost("test-notification")]
        public async Task<ActionResult<object>> TestNotification([FromBody] TestNotificationRequest req)
        {
            try
            {
                if (req == null) return BadRequest(new { success = false, message = "Missing request body" });
                if (string.IsNullOrWhiteSpace(req.Trigger)) return BadRequest(new { success = false, message = "Missing trigger" });

                var settings = await _configurationService.GetApplicationSettingsAsync();
                if (settings == null)
                {
                    return StatusCode(500, new { success = false, message = "Application settings unavailable" });
                }

                string? targetUrl = null;
                if (!string.IsNullOrWhiteSpace(req.WebhookId) && settings.Webhooks != null)
                {
                    var match = settings.Webhooks.FirstOrDefault(w => string.Equals(w.Id, req.WebhookId, StringComparison.OrdinalIgnoreCase));
                    if (match != null) targetUrl = match.Url;
                }

                // Fallback to legacy WebhookUrl
                if (string.IsNullOrWhiteSpace(targetUrl)) targetUrl = settings.WebhookUrl;

                if (string.IsNullOrWhiteSpace(targetUrl))
                {
                    return BadRequest(new { success = false, message = "No webhook URL configured or webhook id not found" });
                }

                await _notificationService.SendNotificationAsync(req.Trigger, req.Data ?? new { }, targetUrl, new List<string> { req.Trigger });

                return Ok(new { success = true, message = "Test notification sent" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DiagnosticsController.TestNotification");
                return StatusCode(500, new { success = false, message = "Failed to send test notification", error = ex.Message });
            }
        }
    }
}
// Diagnostics controller removed because Playwright support is no longer available.
