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
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Metadata.Refresh;

/// <summary>
/// Walks a book's identifiers and regions until the provider answers, then applies what came
/// back under the audiobook's exclusive lock. The API endpoint and the scheduled run share this.
/// </summary>
public sealed partial class MetadataRefreshService : IMetadataRefreshService
{
    private const int MaxAsinLookupAttempts = 8;
    private const int MaxIsbnConversionAttempts = 5;
    private const int MaxTransientRetriesPerBook = 2;

    private readonly IAudiobookRepository _repository;
    private readonly IAudiobookMetadataService _metadataService;
    private readonly MetadataConverters _metadataConverters;
    private readonly IImageCacheService _imageCacheService;
    private readonly IAudiobookOperationCoordinator _operationCoordinator;
    private readonly IMoveQueueService _moveQueueService;
    private readonly ILogger<MetadataRefreshService> _logger;
    private readonly IAsinLookupService? _asinLookupService;

    public MetadataRefreshService(
        IAudiobookRepository repository,
        IAudiobookMetadataService metadataService,
        MetadataConverters metadataConverters,
        IImageCacheService imageCacheService,
        IAudiobookOperationCoordinator operationCoordinator,
        IMoveQueueService moveQueueService,
        ILogger<MetadataRefreshService> logger,
        IAsinLookupService? asinLookupService = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
        _metadataConverters = metadataConverters ?? throw new ArgumentNullException(nameof(metadataConverters));
        _imageCacheService = imageCacheService ?? throw new ArgumentNullException(nameof(imageCacheService));
        _operationCoordinator = operationCoordinator ?? throw new ArgumentNullException(nameof(operationCoordinator));
        _moveQueueService = moveQueueService ?? throw new ArgumentNullException(nameof(moveQueueService));
        _logger = logger;
        _asinLookupService = asinLookupService;
    }

    public async Task<MetadataRefreshResult> RefreshAsync(
        int audiobookId,
        IMetadataRefreshBudget budget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(budget);
        cancellationToken.ThrowIfCancellationRequested();

        var audiobook = await _repository.GetByIdAsync(audiobookId);
        cancellationToken.ThrowIfCancellationRequested();

        if (audiobook == null)
        {
            return new MetadataRefreshResult(MetadataRefreshOutcome.Failed, budget.RequestsSpent);
        }

        var expectedMetadataState = CreateMetadataStateFingerprint(audiobook);
        var effectiveIdentifiers = AudiobookIdentifierMapper.GetEffectiveIdentifiers(audiobook);
        var asinIdentifiers = effectiveIdentifiers
            .Where(i => i.Type == AudiobookExternalIdentifierType.Asin)
            .OrderByDescending(i => i.IsPrimary)
            .ThenBy(i => i.Source)
            .ThenBy(i => i.ValueNormalized)
            .ToList();

        var isbnIdentifiers = effectiveIdentifiers
            .Where(i => i.Type == AudiobookExternalIdentifierType.Isbn)
            .OrderByDescending(i => i.IsPrimary)
            .ThenBy(i => i.Source)
            .ThenBy(i => i.ValueNormalized)
            .ToList();

        if (!asinIdentifiers.Any() && !isbnIdentifiers.Any())
        {
            return new MetadataRefreshResult(MetadataRefreshOutcome.Skipped, budget.RequestsSpent);
        }

        var triedAsinKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var triedAsinDebug = new List<object>();
        var triedIsbnDebug = new List<string>();
        var asinLookupAttempts = 0;
        var isbnConversionAttempts = 0;
        var asinLookupAttemptCapHit = false;
        var isbnConversionAttemptCapHit = false;
        var transientFailures = 0;
        var budgetExhausted = false;
        var deferred = false;

        AudibleBookResponse? providerMetadata = null;
        string? providerSource = null;
        string? resolvedAsin = null;
        string? resolvedRegion = null;

        async Task<bool> TryMetadataLookupByAsinAsync(string asin, string? preferredRegion, string via)
        {
            if (!AudiobookIdentifierNormalizer.TryNormalize(
                    AudiobookExternalIdentifierType.Asin,
                    asin,
                    out var normalizedAsin,
                    out _))
            {
                return false;
            }

            foreach (var region in EnumerateRefreshRegions(preferredRegion))
            {
                var regionValue = string.IsNullOrWhiteSpace(region) ? "us" : region!;
                var key = $"{normalizedAsin}|{regionValue}";
                if (!triedAsinKeys.Add(key))
                {
                    continue;
                }

                triedAsinDebug.Add(new { asin = normalizedAsin, region = regionValue, via });

                AudiobookMetadataEnvelope? metadataEnvelope;
                while (true)
                {
                    if (asinLookupAttempts >= MaxAsinLookupAttempts)
                    {
                        asinLookupAttemptCapHit = true;
                        return false;
                    }

                    asinLookupAttempts++;

                    if (!await budget.ChargeAsync(cancellationToken))
                    {
                        budgetExhausted = true;
                        return false;
                    }

                    try
                    {
                        metadataEnvelope = await _metadataService.GetMetadataAsync(normalizedAsin, regionValue, cache: false);
                        cancellationToken.ThrowIfCancellationRequested();
                        break;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                    {
                        transientFailures++;
                        budget.ApplyThrottleSignal(RetryAfterFrom(ex));
                        _logger.LogWarning(
                            ex,
                            "Metadata refresh lookup failed for audiobook {AudiobookId} ({Title}) ASIN {Asin} region {Region}, failure {Failure} of {Ceiling}",
                            audiobook.Id,
                            audiobook.Title,
                            normalizedAsin,
                            regionValue,
                            transientFailures,
                            MaxTransientRetriesPerBook + 1);
                        if (transientFailures > MaxTransientRetriesPerBook)
                        {
                            deferred = true;
                            return false;
                        }
                    }
                }

                if (metadataEnvelope == null)
                {
                    continue;
                }

                providerMetadata = metadataEnvelope.Metadata;
                providerSource = metadataEnvelope.Source;
                resolvedAsin = string.IsNullOrWhiteSpace(metadataEnvelope.Metadata.Asin)
                    ? normalizedAsin
                    : metadataEnvelope.Metadata.Asin;
                resolvedRegion = regionValue;
                return true;
            }

            return false;
        }

        // The conversion is a provider request like any other, so it is charged and retried on the
        // same ceiling. Null means "move to the next ISBN"; the flags say whether to stop instead.
        async Task<string?> TryConvertIsbnToAsinAsync(string isbnValue)
        {
            while (true)
            {
                if (isbnConversionAttempts >= MaxIsbnConversionAttempts)
                {
                    isbnConversionAttemptCapHit = true;
                    return null;
                }

                if (_asinLookupService == null)
                {
                    return null;
                }

                isbnConversionAttempts++;

                if (!await budget.ChargeAsync(cancellationToken))
                {
                    budgetExhausted = true;
                    return null;
                }

                try
                {
                    var (success, asinFromIsbn, _) = await _asinLookupService.GetAsinFromIsbnAsync(isbnValue);
                    cancellationToken.ThrowIfCancellationRequested();
                    return success ? asinFromIsbn : null;
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    transientFailures++;
                    budget.ApplyThrottleSignal(RetryAfterFrom(ex));
                    _logger.LogWarning(
                        ex,
                        "Metadata refresh ASIN conversion failed for audiobook {AudiobookId} ISBN {Isbn}, failure {Failure} of {Ceiling}",
                        audiobook.Id,
                        isbnValue,
                        transientFailures,
                        MaxTransientRetriesPerBook + 1);
                    if (transientFailures > MaxTransientRetriesPerBook)
                    {
                        deferred = true;
                        return null;
                    }
                }
            }
        }

        foreach (var asinIdentifier in asinIdentifiers)
        {
            var asinValue = FirstNonEmpty(asinIdentifier.ValueRaw, asinIdentifier.ValueNormalized);
            if (string.IsNullOrWhiteSpace(asinValue)) continue;

            if (await TryMetadataLookupByAsinAsync(asinValue, asinIdentifier.Region, "asin"))
            {
                break;
            }

            if (asinLookupAttemptCapHit || budgetExhausted || deferred)
            {
                break;
            }
        }

        if (providerMetadata == null && !budgetExhausted && !deferred)
        {
            if (_asinLookupService == null)
            {
                _logger.LogWarning("IAsinLookupService not available for ISBN fallback during metadata rescan of audiobook {AudiobookId}", audiobook.Id);
            }

            foreach (var isbnIdentifier in isbnIdentifiers)
            {
                var isbnValue = FirstNonEmpty(isbnIdentifier.ValueNormalized, isbnIdentifier.ValueRaw);
                if (string.IsNullOrWhiteSpace(isbnValue)) continue;

                if (!triedIsbnDebug.Contains(isbnValue, StringComparer.OrdinalIgnoreCase))
                {
                    triedIsbnDebug.Add(isbnValue);
                }

                var asinFromIsbn = await TryConvertIsbnToAsinAsync(isbnValue);
                if (isbnConversionAttemptCapHit || budgetExhausted || deferred)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(asinFromIsbn))
                {
                    continue;
                }

                if (await TryMetadataLookupByAsinAsync(asinFromIsbn, null, "isbn"))
                {
                    break;
                }

                if (asinLookupAttemptCapHit || budgetExhausted || deferred)
                {
                    break;
                }
            }
        }

        if (deferred || budgetExhausted)
        {
            return new MetadataRefreshResult(MetadataRefreshOutcome.Deferred, budget.RequestsSpent);
        }

        if (providerMetadata == null || string.IsNullOrWhiteSpace(resolvedAsin))
        {
            _logger.LogDebug(
                "Metadata rescan found no metadata for audiobook {AudiobookId}. TriedAsins={TriedAsins}; TriedIsbns={TriedIsbns}; AsinLookups={AsinLookups}/{AsinCap}; IsbnConversions={IsbnConversions}/{IsbnCap}; Capped={Capped}",
                audiobook.Id,
                triedAsinDebug,
                triedIsbnDebug,
                asinLookupAttempts,
                MaxAsinLookupAttempts,
                isbnConversionAttempts,
                MaxIsbnConversionAttempts,
                asinLookupAttemptCapHit || isbnConversionAttemptCapHit);

            return new MetadataRefreshResult(MetadataRefreshOutcome.NotFound, budget.RequestsSpent);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var convertedMetadata = _metadataConverters.ConvertAudibleToMetadata(
            providerMetadata,
            resolvedAsin,
            string.IsNullOrWhiteSpace(providerSource) ? "Audible" : providerSource!);

        var applyResult = await _operationCoordinator.ExecuteExclusiveAsync(
            audiobookId,
            async token =>
            {
                await _moveQueueService.EnsureFilesystemMutationAllowedAsync(audiobookId, token);
                token.ThrowIfCancellationRequested();
                return await ApplyRefreshResultAsync(
                    audiobookId,
                    convertedMetadata,
                    expectedMetadataState,
                    token);
            },
            cancellationToken);

        if (applyResult.Status == MetadataRefreshApplyStatus.NotFound)
        {
            return new MetadataRefreshResult(MetadataRefreshOutcome.Failed, budget.RequestsSpent);
        }

        if (applyResult.Status == MetadataRefreshApplyStatus.Conflict)
        {
            return new MetadataRefreshResult(MetadataRefreshOutcome.Conflict, budget.RequestsSpent);
        }

        var updatedAudiobook = applyResult.Audiobook!;
        _logger.LogInformation(
            "Metadata rescan updated audiobook {AudiobookId} ({Title}) using {Source} ASIN {Asin} region {Region}",
            updatedAudiobook.Id,
            updatedAudiobook.Title,
            providerSource ?? "unknown",
            resolvedAsin,
            resolvedRegion ?? "us");

        return new MetadataRefreshResult(
            MetadataRefreshOutcome.Updated,
            budget.RequestsSpent,
            providerSource,
            resolvedAsin,
            resolvedRegion ?? "us");
    }

    private static TimeSpan? RetryAfterFrom(Exception exception)
    {
        // A provider that pushed back names a status; today the Audible client swallows it and
        // returns null instead, so this reads whatever a future client raises rather than
        // pretending the current one does.
        if (exception is not HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } request)
        {
            return null;
        }

        return request.Data["Retry-After"] is TimeSpan retryAfter ? retryAfter : null;
    }
}
