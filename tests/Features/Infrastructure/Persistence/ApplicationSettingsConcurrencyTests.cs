/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Application.Common.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Tests.Features.Infrastructure.Persistence;

public sealed class ApplicationSettingsConcurrencyTests : IAsyncLifetime
{
    private readonly string _databasePath =
        Path.Join(Path.GetTempPath(), "listenarr-tests", $"settings-concurrency-{Guid.NewGuid():N}.db");
    private DbContextOptions<ListenArrDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        _options = new DbContextOptionsBuilder<ListenArrDbContext>()
            .UseSqlite($"Data Source={_databasePath};Pooling=False")
            .Options;
        await using var db = new ListenArrDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ConcurrentInitialSave_ReturnsSingletonSettingsForBothCallers()
    {
        await using var db1 = new ListenArrDbContext(_options);
        await using var db2 = new ListenArrDbContext(_options);
        var repository1 = new EfApplicationSettingsRepository(db1);
        var repository2 = new EfApplicationSettingsRepository(db2);

        var results = await Task.WhenAll(
            repository1.SaveAsync(new ApplicationSettings()),
            repository2.SaveAsync(new ApplicationSettings()));

        Assert.All(results, settings => Assert.Equal(1, settings.Id));
        await using var verificationDb = new ListenArrDbContext(_options);
        Assert.Equal(1, await verificationDb.ApplicationSettings.CountAsync());
    }

    [Fact]
    public async Task StaleSettingsUpdate_ThrowsStableConflict()
    {
        await using (var seedDb = new ListenArrDbContext(_options))
        {
            await new EfApplicationSettingsRepository(seedDb).SaveAsync(new ApplicationSettings());
        }

        await using var db1 = new ListenArrDbContext(_options);
        await using var db2 = new ListenArrDbContext(_options);
        var repository1 = new EfApplicationSettingsRepository(db1);
        var repository2 = new EfApplicationSettingsRepository(db2);
        var first = await repository1.GetAsync();
        var stale = await repository2.GetAsync();
        Assert.NotNull(first);
        Assert.NotNull(stale);

        first!.OutputPath = "first";
        await repository1.SaveAsync(first);
        stale!.OutputPath = "stale";

        var exception = await Assert.ThrowsAsync<ApplicationConflictException>(
            () => repository2.SaveAsync(stale));

        Assert.Equal("settings_concurrency_conflict", exception.Code);
    }
}
