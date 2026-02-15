using Listenarr.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Listenarr.Api.Controllers
{
    [ApiController]
    [Route("api/metadata")]
    public class MetadataController : ControllerBase
    {
        private readonly IAudiobookMetadataService _metadataService;
        private readonly ILogger<MetadataController> _logger;
        private readonly AudimetaService _audimetaService;
        private readonly IImageCacheService _imageCacheService;
        private readonly IMemoryCache _cache;

        public MetadataController(
            IAudiobookMetadataService metadataService,
            AudimetaService audimetaService,
            IImageCacheService imageCacheService,
            IMemoryCache cache,
            ILogger<MetadataController> logger)
        {
            _metadataService = metadataService;
            _audimetaService = audimetaService;
            _imageCacheService = imageCacheService;
            _cache = cache;
            _logger = logger;
        }

        /// <summary>
        /// Get audiobook metadata from configured metadata sources by ASIN.
        /// </summary>
        [HttpGet("{asin}")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<object>> GetMetadata(
            string asin,
            [FromQuery] string region = "us",
            [FromQuery] bool cache = true)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(asin))
                {
                    return BadRequest("ASIN is required");
                }

                var result = await _metadataService.GetMetadataAsync(asin, region, cache);
                if (result == null)
                {
                    return NotFound($"No metadata found for ASIN: {asin}");
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching metadata for ASIN: {Asin}", asin);
                return StatusCode(500, $"Error fetching metadata: {ex.Message}");
            }
        }

        /// <summary>
        /// Get audiobook metadata from audimeta.de by ASIN.
        /// </summary>
        [HttpGet("audimeta/{asin}")]
        [ProducesResponseType(typeof(AudimetaBookResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<AudimetaBookResponse>> GetAudimetaMetadata(
            string asin,
            [FromQuery] string region = "us",
            [FromQuery] bool cache = true)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(asin))
                {
                    return BadRequest("ASIN parameter is required");
                }

                var result = await _metadataService.GetAudimetaMetadataAsync(asin, region, cache);
                if (result == null)
                {
                    return NotFound($"No metadata found for ASIN: {asin}");
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching audimeta metadata for ASIN: {Asin}", asin);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Lookup an author by name via Audimeta and ensure the author image is cached under authors folder.
        /// Returns an object with `asin`, `name`, `image` and `cachedPath` (relative path under config/cache/images/authors).
        /// </summary>
        [HttpGet("author")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<object>> LookupAuthor([FromQuery] string name, [FromQuery] string region = "us")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name)) return BadRequest("Author name is required");

                var normalizedName = name.Trim();
                var cacheKey = $"author-lookup:{region}:{normalizedName.ToLowerInvariant()}";

                if (_cache.TryGetValue(cacheKey, out AuthorLookupCacheEntry? cachedEntry) && cachedEntry != null)
                {
                    if (cachedEntry.NotFound)
                    {
                        return NotFound("Author not found");
                    }

                    string? cachedPath = cachedEntry.CachedPath;
                    if (!string.IsNullOrWhiteSpace(cachedEntry.Asin))
                    {
                        var diskPath = await _imageCacheService.GetCachedImagePathAsync(cachedEntry.Asin);
                        if (!string.IsNullOrWhiteSpace(diskPath))
                        {
                            cachedPath = "/" + diskPath.TrimStart('/');
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(cachedPath))
                    {
                        return Ok(new
                        {
                            asin = cachedEntry.Asin,
                            name = cachedEntry.Name ?? normalizedName,
                            image = cachedEntry.Image,
                            cachedPath = cachedPath
                        });
                    }
                }

                var info = await _audimetaService.LookupAuthorAsync(normalizedName, region);
                if (info == null)
                {
                    _cache.Set(cacheKey, new AuthorLookupCacheEntry
                    {
                        NotFound = true,
                        Name = normalizedName
                    }, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(6) });

                    return NotFound("Author not found");
                }

                string? cached = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(info.Asin))
                    {
                        // Attempt to ensure author image is cached under authors storage
                        cached = await _imageCacheService.MoveToAuthorLibraryStorageAsync(info.Asin, info.Image);
                        if (!string.IsNullOrWhiteSpace(cached)) cached = "/" + cached.TrimStart('/');
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cache author image for {Author}", name);
                }

                var result = new {
                    asin = info.Asin,
                    name = info.Name,
                    image = info.Image,
                    cachedPath = cached
                };

                _cache.Set(cacheKey, new AuthorLookupCacheEntry
                {
                    Asin = info.Asin,
                    Name = info.Name ?? normalizedName,
                    Image = info.Image,
                    CachedPath = cached,
                    NotFound = false
                }, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(12) });

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error looking up author: {Name}", name);
                return StatusCode(500, "Internal server error");
            }
        }

        private sealed class AuthorLookupCacheEntry
        {
            public string? Asin { get; set; }
            public string? Name { get; set; }
            public string? Image { get; set; }
            public string? CachedPath { get; set; }
            public bool NotFound { get; set; }
        }
    }
}
