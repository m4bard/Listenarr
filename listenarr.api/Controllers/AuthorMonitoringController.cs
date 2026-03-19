using Listenarr.Api.Services;
using Listenarr.Domain.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Controllers
{
    [ApiController]
    [Route("api/v{version:apiVersion}/authors/monitoring")]
    [Tags("Authors")]
    public class AuthorMonitoringController : ControllerBase
    {
        private readonly IAuthorMonitoringService _authorMonitoringService;
        private readonly ILogger<AuthorMonitoringController> _logger;

        public AuthorMonitoringController(
            IAuthorMonitoringService authorMonitoringService,
            ILogger<AuthorMonitoringController> logger)
        {
            _authorMonitoringService = authorMonitoringService;
            _logger = logger;
        }

        [HttpGet("status")]
        [ProducesResponseType(typeof(AuthorMonitoringStatusResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<AuthorMonitoringStatusResponse>> GetStatus(
            [FromQuery] string name,
            [FromQuery] string region = "us",
            [FromQuery] string language = "all",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest("Author name is required");
            }

            try
            {
                var monitoredAuthor = await _authorMonitoringService.GetMonitoredAuthorAsync(
                    name,
                    region,
                    language,
                    cancellationToken);

                return Ok(new AuthorMonitoringStatusResponse
                {
                    IsMonitored = monitoredAuthor != null,
                    MonitoredAuthor = monitoredAuthor == null ? null : ToResponse(monitoredAuthor)
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "Failed to get author monitoring status for {Author}", name);
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error");
            }
        }

        [HttpPost]
        [ProducesResponseType(typeof(MonitorAuthorResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<MonitorAuthorResponse>> MonitorAuthor(
            [FromBody] MonitorAuthorRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest("Author name is required");
            }

            try
            {
                var result = await _authorMonitoringService.MonitorAuthorAsync(request, cancellationToken);
                if (result.MonitoredAuthor == null)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, "Failed to monitor author");
                }

                return Ok(new MonitorAuthorResponse
                {
                    Message = "Author monitoring enabled",
                    MonitoredAuthor = ToResponse(result.MonitoredAuthor),
                    AddedCount = result.SyncResult.AddedCount,
                    ExistingCount = result.SyncResult.ExistingCount,
                    FailedCount = result.SyncResult.FailedCount,
                    ErrorMessage = result.SyncResult.ErrorMessage
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "Failed to enable monitoring for author {Author}", request.Name);
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error");
            }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> UnmonitorAuthor(int id, CancellationToken cancellationToken = default)
        {
            try
            {
                var removed = await _authorMonitoringService.UnmonitorAuthorAsync(id, cancellationToken);
                if (!removed)
                {
                    return NotFound();
                }

                return Ok(new { message = "Author monitoring disabled" });
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "Failed to disable monitoring for author {AuthorId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error");
            }
        }

        private static MonitoredAuthorResponse ToResponse(MonitoredAuthor monitoredAuthor)
        {
            return new MonitoredAuthorResponse
            {
                Id = monitoredAuthor.Id,
                AuthorName = monitoredAuthor.AuthorName,
                AuthorAsin = monitoredAuthor.AuthorAsin,
                Region = monitoredAuthor.Region,
                Language = monitoredAuthor.Language,
                CreatedAt = monitoredAuthor.CreatedAt,
                UpdatedAt = monitoredAuthor.UpdatedAt,
                LastCheckedAt = monitoredAuthor.LastCheckedAt,
                LastSuccessfulSyncAt = monitoredAuthor.LastSuccessfulSyncAt,
                LastError = monitoredAuthor.LastError
            };
        }

        public sealed class AuthorMonitoringStatusResponse
        {
            public bool IsMonitored { get; set; }

            public MonitoredAuthorResponse? MonitoredAuthor { get; set; }
        }

        public sealed class MonitorAuthorResponse
        {
            public string Message { get; set; } = string.Empty;

            public MonitoredAuthorResponse MonitoredAuthor { get; set; } = new();

            public int AddedCount { get; set; }

            public int ExistingCount { get; set; }

            public int FailedCount { get; set; }

            public string? ErrorMessage { get; set; }
        }

        public sealed class MonitoredAuthorResponse
        {
            public int Id { get; set; }

            public string AuthorName { get; set; } = string.Empty;

            public string? AuthorAsin { get; set; }

            public string Region { get; set; } = "us";

            public string Language { get; set; } = "all";

            public DateTime CreatedAt { get; set; }

            public DateTime UpdatedAt { get; set; }

            public DateTime? LastCheckedAt { get; set; }

            public DateTime? LastSuccessfulSyncAt { get; set; }

            public string? LastError { get; set; }
        }
    }
}
