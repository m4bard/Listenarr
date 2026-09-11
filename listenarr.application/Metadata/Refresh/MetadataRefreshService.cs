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
    // Distinct identifier-and-region pairs a book may be looked for in, not HTTP requests.
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

        // The budget is the run's, so its counter is cumulative across every book the run has
        // already touched. What this book cost is the difference either side of the walk.
        var spentAtEntry = budget.RequestsSpent;
        cancellationToken.ThrowIfCancellationRequested();

        var audiobook = await _repository.GetByIdAsync(audiobookId);
        cancellationToken.ThrowIfCancellationRequested();

        if (audiobook == null)
        {
            return new MetadataRefreshResult(MetadataRefreshOutcome.Failed, budget.RequestsSpent - spentAtEntry);
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
            return new MetadataRefreshResult(MetadataRefreshOutcome.Skipped, budget.RequestsSpent - spentAtEntry);
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

        // Provider requests that were made, and how many of those came back with a verdict
        // rather than an exception. Nothing else in the walk can tell an absent book from an
        // unreachable provider: the metadata service answers null for a book it has never heard
        // of, and raises when it could not ask. So a walk that ends with nothing is only
        // evidence about the book if something answered, and only then may it be stamped.
        var providerCalls = 0;
        var providerAnswers = 0;

        // Pushback narrows the run once per book, on the first 429 the book sees. Both halves
        // of that matter. Once, because halving per attempt compounds: three 429s on one book
        // take a run from sixty an hour to seven before the second book is reached, and what
        // the budget is meant to do is halve for the rest of the cycle, singular. And on the
        // first one, not at the retry ceiling, because a 429 that a retry recovers from is
        // still the provider asking for less; ignoring it drops the Retry-After it named and
        // sends the walk straight back at a host that just said no.
        var sawPushback = false;
        var pushbackSignalled = false;

        void NotePushback(Exception exception)
        {
            if (!TryReadPushback(exception, out var retryAfter))
            {
                return;
            }

            sawPushback = true;
            if (pushbackSignalled)
            {
                return;
            }

            pushbackSignalled = true;
            budget.ApplyThrottleSignal(retryAfter);
        }

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

                // Counted once per identifier and region rather than once per HTTP attempt. The
                // cap is there to bound how many places a book is looked for; charging a retry
                // of the same place against it meant a flaky provider ran the book out of
                // identifiers after three regions instead of eight.
                if (asinLookupAttempts >= MaxAsinLookupAttempts)
                {
                    asinLookupAttemptCapHit = true;
                    return false;
                }

                asinLookupAttempts++;

                AudiobookMetadataEnvelope? metadataEnvelope = null;
                while (true)
                {
                    if (!await budget.ChargeAsync(cancellationToken))
                    {
                        budgetExhausted = true;
                        return false;
                    }

                    try
                    {
                        providerCalls++;
                        metadataEnvelope = await _metadataService.GetMetadataAsync(normalizedAsin, regionValue, cache: false);
                        providerAnswers++;
                        cancellationToken.ThrowIfCancellationRequested();
                        break;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                    {
                        transientFailures++;
                        NotePushback(ex);
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
                            // Pushback is about the provider and not about this region, so
                            // asking it somewhere else is the one thing it has just asked us
                            // not to do. The book waits for the next cycle instead.
                            if (sawPushback)
                            {
                                deferred = true;
                                return false;
                            }

                            // Any other fault belongs to the region. The endpoint has always
                            // moved on to the next one, and a book whose uk lookup would have
                            // answered must not be lost to a us connection that would not open.
                            break;
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
                    providerCalls++;
                    var (success, asinFromIsbn, _) = await _asinLookupService.GetAsinFromIsbnAsync(isbnValue);
                    providerAnswers++;
                    cancellationToken.ThrowIfCancellationRequested();
                    return success ? asinFromIsbn : null;
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    transientFailures++;
                    NotePushback(ex);
                    _logger.LogWarning(
                        ex,
                        "Metadata refresh ASIN conversion failed for audiobook {AudiobookId} ISBN {Isbn}, failure {Failure} of {Ceiling}",
                        audiobook.Id,
                        isbnValue,
                        transientFailures,
                        MaxTransientRetriesPerBook + 1);
                    if (transientFailures > MaxTransientRetriesPerBook)
                    {
                        if (sawPushback)
                        {
                            deferred = true;
                        }

                        // Null moves the caller on to the next ISBN unless deferred says stop.
                        // One identifier that will not convert says nothing about the others,
                        // and ending the walk on it loses every identifier behind it.
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
            return new MetadataRefreshResult(
                MetadataRefreshOutcome.Deferred,
                budget.RequestsSpent - spentAtEntry,
                ProviderAnswers: providerAnswers);
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

            // Every region and identifier is exhausted. Whether that is an absent book or an
            // unreachable provider is the difference between stamping the book for a month and
            // trying it again next cycle.
            //
            // Nothing was asked at all: every identifier failed to normalize, or the only ones
            // present were ISBNs with no resolver wired. No provider had any part in it, so it
            // is settled locally rather than reported as a provider verdict. It is not Skipped,
            // though: this book does carry identifiers, and a caller that says otherwise to a
            // person sends them looking for a field that is already filled in.
            if (providerCalls == 0)
            {
                return new MetadataRefreshResult(
                    MetadataRefreshOutcome.Unusable,
                    budget.RequestsSpent - spentAtEntry);
            }

            // Otherwise the walk only says something about the book if something answered. A run
            // that asked and was never answered is deferred, whatever the shape of the silence.
            return new MetadataRefreshResult(
                transientFailures > 0 || providerAnswers == 0
                    ? MetadataRefreshOutcome.Deferred
                    : MetadataRefreshOutcome.NotFound,
                budget.RequestsSpent - spentAtEntry,
                ProviderAnswers: providerAnswers);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var convertedMetadata = _metadataConverters.ConvertAudibleToMetadata(
            providerMetadata,
            resolvedAsin,
            string.IsNullOrWhiteSpace(providerSource) ? "Audible" : providerSource!);

        // Charged here, outside the exclusive section below, and the grant carried in. The
        // charge can wait: a spacing floor, or a whole Retry-After the provider named. That wait
        // taken under the book's exclusive lock blocks every import, move and rename queued
        // against the same book behind a rate limiter they have no part in.
        //
        // The cost of charging early is a token spent on a book whose apply then turns out to
        // conflict or to have been deleted. That is rare, it is one request, and it errs towards
        // counting requests that did not happen rather than missing ones that did.
        var coverChargeGranted =
            !string.IsNullOrWhiteSpace(convertedMetadata.ImageUrl)
            && await budget.ChargeAsync(cancellationToken);

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
                    coverChargeGranted,
                    token);
            },
            cancellationToken);

        if (applyResult.Status == MetadataRefreshApplyStatus.NotFound)
        {
            return new MetadataRefreshResult(MetadataRefreshOutcome.Failed, budget.RequestsSpent - spentAtEntry);
        }

        if (applyResult.Status == MetadataRefreshApplyStatus.Conflict)
        {
            return new MetadataRefreshResult(MetadataRefreshOutcome.Conflict, budget.RequestsSpent - spentAtEntry);
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
            budget.RequestsSpent - spentAtEntry,
            providerSource,
            resolvedAsin,
            resolvedRegion ?? "us",
            providerAnswers);
    }
}
