/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 */
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.Search
{
    public partial class SearchController : ControllerBase
    {
        /// <summary>
        /// Search for audiobook series by name using the Audible catalog provider.
        /// </summary>
        /// <param name="name">Series name to search for.</param>
        /// <param name="region">Audible marketplace region (default: us).</param>
        [HttpGet("audible/series")]
        public async Task<ActionResult<object>> SearchAudibleSeries([FromQuery] string name, [FromQuery] string region = "us")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name)) return BadRequest("name query parameter is required");
                var res = await _audibleService.SearchSeriesByNameAsync(name, region);
                if (res == null) return NotFound();
                return Ok(res);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "Error proxying Audible series search for name {Name}", name);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get all books in a series by the series ASIN.
        /// </summary>
        /// <param name="asin">Audible series ASIN.</param>
        /// <param name="region">Audible marketplace region (default: us).</param>
        [HttpGet("audible/series/books/{asin}")]
        public async Task<ActionResult<object>> GetAudibleSeriesBooks(string asin, [FromQuery] string region = "us")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(asin)) return BadRequest("asin is required");
                var res = await _audibleService.GetBooksBySeriesAsinAsync(asin, region);
                if (res == null) return NotFound();
                return Ok(res);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "Error proxying Audible series books for ASIN {Asin}", asin);
                return StatusCode(500, "Internal server error");
            }
        }
    }
}
