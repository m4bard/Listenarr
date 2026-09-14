/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.ActivityHistory.Jobs
{
    [Trait("Name", "HistoryRetentionCleanupProcessorTests")]
    [Trait("Category", "BackgroundWorkers")]
    public sealed class HistoryRetentionCleanupProcessorTests : BaseTests
    {
        [Fact]
        public async Task RunCycleAsync_RemovesOnlyEntriesOlderThanConfiguredRetention()
        {
            // Arrange
            var settings = new ApplicationSettingsBuilder().Build();
            settings.HistoryRetentionDays = 5;
            await _applicationSettingsRepository.SaveAsync(settings);

            var oldEntry = await _historyRepository.AddAsync(new History
            {
                EventType = "Imported",
                Timestamp = DateTime.UtcNow.AddDays(-10),
            });
            var youngEntry = await _historyRepository.AddAsync(new History
            {
                EventType = "Imported",
                Timestamp = DateTime.UtcNow.AddDays(-1),
            });

            // Act
            await _provider.GetRequiredService<IHistoryRetentionCleanupProcessor>()
                .RunCycleAsync(CancellationToken.None);

            // Assert
            Assert.Null(await _historyRepository.GetByIdAsync(oldEntry.Id));
            Assert.NotNull(await _historyRepository.GetByIdAsync(youngEntry.Id));
        }

        [Fact]
        public async Task RunCycleAsync_WhenRetentionIsZero_LeavesAllHistoryInPlace()
        {
            // Arrange: zero means unlimited retention, so even very old entries must survive.
            var settings = new ApplicationSettingsBuilder().Build();
            settings.HistoryRetentionDays = 0;
            await _applicationSettingsRepository.SaveAsync(settings);

            var veryOldEntry = await _historyRepository.AddAsync(new History
            {
                EventType = "Imported",
                Timestamp = DateTime.UtcNow.AddYears(-5),
            });

            // Act
            await _provider.GetRequiredService<IHistoryRetentionCleanupProcessor>()
                .RunCycleAsync(CancellationToken.None);

            // Assert
            Assert.NotNull(await _historyRepository.GetByIdAsync(veryOldEntry.Id));
        }
    }
}
