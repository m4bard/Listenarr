using Listenarr.Api.Services.Search;
using Listenarr.Domain.Models;
using Listenarr.Infrastructure.Models;
using Microsoft.Extensions.Logging;

namespace Listenarr.Api.Services.Search.Strategies;

/// <summary>
/// Fetches metadata from the Audible-backed catalog provider.
/// </summary>
public class AudibleMetadataStrategy : IMetadataStrategy
{
    private readonly AudibleService _audibleService;
    private readonly MetadataConverters _metadataConverters;
    private readonly ILogger<AudibleMetadataStrategy> _logger;

    public string SourceName => "Audible";

    public AudibleMetadataStrategy(
        AudibleService audibleService,
        MetadataConverters metadataConverters,
        ILogger<AudibleMetadataStrategy> logger)
    {
        _audibleService = audibleService;
        _metadataConverters = metadataConverters;
        _logger = logger;
    }

    public bool CanHandle(ApiConfiguration source)
    {
        if (source == null) return false;

        var name = source.Name ?? string.Empty;
        var baseUrl = source.BaseUrl ?? string.Empty;

        return name.Contains("Audible", StringComparison.OrdinalIgnoreCase) ||
            baseUrl.Contains("api.audible", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<AudibleBookMetadata?> FetchMetadataAsync(string asin, ApiConfiguration source, string? originalSource)
    {
        _logger.LogDebug("Calling Audible metadata service for ASIN {Asin}", asin);
        var audibleData = await _audibleService.GetBookMetadataAsync(asin, "us", true);

        if (audibleData != null)
        {
            _logger.LogInformation(
                "Audible metadata returned data for ASIN {Asin}. Title: {Title}",
                asin,
                audibleData.Title ?? "null");
            var metadata = _metadataConverters.ConvertAudibleToMetadata(audibleData, asin, originalSource ?? "Audible");
            _logger.LogInformation("Successfully enriched ASIN {Asin} with metadata from {SourceName}", asin, source.Name);
            return metadata;
        }

        _logger.LogWarning("Audible metadata returned null for ASIN {Asin}", asin);

        // Retry once without cache when the initial lookup misses.
        try
        {
            _logger.LogInformation(
                "Audible metadata returned null for ASIN {Asin} (cache=true); retrying without cache",
                asin);
            var audibleRetry = await _audibleService.GetBookMetadataAsync(asin, "us", false);
            if (audibleRetry != null)
            {
                _logger.LogInformation(
                    "Audible metadata returned data for ASIN {Asin} on retry (no-cache). Title: {Title}",
                    asin,
                    audibleRetry.Title ?? "null");
                var metadata = _metadataConverters.ConvertAudibleToMetadata(audibleRetry, asin, originalSource ?? "Audible");
                _logger.LogInformation(
                    "Successfully enriched ASIN {Asin} with metadata from {SourceName} (no-cache)",
                    asin,
                    source.Name);
                return metadata;
            }

            _logger.LogWarning("Audible metadata returned null on retry for ASIN {Asin}", asin);
        }
        catch (Exception exRetry) when (exRetry is not OperationCanceledException && exRetry is not OutOfMemoryException && exRetry is not StackOverflowException)
        {
            _logger.LogWarning(exRetry, "Audible metadata retry without cache failed for ASIN {Asin}", asin);
        }

        return null;
    }
}
