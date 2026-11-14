/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Tests.Common;
using Listenarr.Tests.Features.Infrastructure.HostedServices.Search.Sweep;
using Listenarr.Tests.Features.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Listenarr.Tests.Features.Infrastructure.Persistence.AutomaticSearchResetTestLibrary;

namespace Listenarr.Tests.Features.Api.Features.Library;

/// <summary>
/// S-T8: an edit to something a book's automatic search is built from makes the book due now,
/// with no pair rows and a failure count of 0, through the library's own edit paths. Each case
/// runs on migrated SQLite with the interceptor the application registers, and reads the
/// schedule back through a fresh context, so a reset that does not land fails.
/// </summary>
[Trait("Name", "LibraryMetadataChangeResetTests")]
[Trait("Category", "LibraryController")]
public sealed class LibraryMetadataChangeResetTests : BaseTests
{
    private readonly SweepClock _clock = new(NowUtc);
    private AutomaticSearchScheduleTestDatabase _database = null!;
    private AutomaticSearchResetTestLibrary _library = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _database = new AutomaticSearchScheduleTestDatabase(FileService.GetTempPath());
        _library = new AutomaticSearchResetTestLibrary(_database);
        await _library.InitializeAsync();
        Init(builder => builder
            .WithSingleton<TimeProvider>(_clock)
            .WithSingleton<IDbContextFactory<ListenArrDbContext>>(provider => new ContextFactory(_database, provider))
            .WithMocks(ServiceDescriptor.Scoped<ListenArrDbContext>(provider =>
                provider.GetRequiredService<IDbContextFactory<ListenArrDbContext>>().CreateDbContext())));
    }

    [Fact]
    public async Task MetadataChange_ResetsEligibility_TitleEdit()
    {
        var edited = await _library.SeedBookInBackoffAsync();
        var bystander = await _library.SeedBookInBackoffAsync();

        var result = await _provider.GetRequiredService<LibraryUpdateWorkflow>()
            .UpdateAsync(edited, new AudiobookUpdateRequest { Title = "The Gods of Mars" });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(Reset, await _library.ReadScheduleAsync(edited));
        Assert.Equal(Untouched, await _library.ReadScheduleAsync(bystander));
        Assert.Equal("The Gods of Mars", await ReadTitleAsync(edited));
    }

    [Fact]
    public async Task MetadataChange_ResetsEligibility_BulkQualityProfileChange()
    {
        var first = await _library.SeedBookInBackoffAsync();
        var second = await _library.SeedBookInBackoffAsync();
        var bystander = await _library.SeedBookInBackoffAsync();

        var result = await _provider.GetRequiredService<LibraryBulkEditWorkflow>().BulkUpdateAsync(
            new LibraryController.BulkUpdateRequest
            {
                Ids = [first, second],
                Updates = new Dictionary<string, object> { ["qualityProfileId"] = _library.OtherProfileId }
            });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(Reset, await _library.ReadScheduleAsync(first));
        Assert.Equal(Reset, await _library.ReadScheduleAsync(second));
        Assert.Equal(Untouched, await _library.ReadScheduleAsync(bystander));
    }

    [Fact]
    public async Task MetadataChange_ResetsEligibility_MonitoredSwitchedOn()
    {
        var switchedOn = await _library.SeedBookInBackoffAsync(monitored: false);

        var result = await _provider.GetRequiredService<LibraryUpdateWorkflow>()
            .UpdateAsync(switchedOn, new AudiobookUpdateRequest { Monitored = true });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(Reset, await _library.ReadScheduleAsync(switchedOn));
        Assert.True((await _database.ReadBookAsync(switchedOn)).Monitored);
    }

    [Fact]
    public async Task MetadataChange_IdenticalTitleWrittenBack_ChangesNothing()
    {
        var book = await _library.SeedBookInBackoffAsync();

        // As a rescan does: the same title is written back alongside a field the search ignores.
        var result = await _provider.GetRequiredService<LibraryUpdateWorkflow>().UpdateAsync(
            book,
            new AudiobookUpdateRequest { Title = Title, Description = "A rescan refreshed the description." });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(Untouched, await _library.ReadScheduleAsync(book));
    }

    [Fact]
    public async Task MetadataChange_UnmonitoredBookEdited_ChangesNothing()
    {
        var book = await _library.SeedBookInBackoffAsync(monitored: false);

        var result = await _provider.GetRequiredService<LibraryUpdateWorkflow>()
            .UpdateAsync(book, new AudiobookUpdateRequest { Title = "The Gods of Mars" });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(Untouched, await _library.ReadScheduleAsync(book));
    }

    private async Task<string?> ReadTitleAsync(int bookId)
    {
        await using var context = _database.CreateContext();
        return await context.Audiobooks.AsNoTracking()
            .Where(candidate => candidate.Id == bookId)
            .Select(candidate => candidate.Title)
            .SingleAsync();
    }

    /// <summary>Contexts on the test database carrying the interceptor the application registers.</summary>
    private sealed class ContextFactory(AutomaticSearchScheduleTestDatabase database, IServiceProvider provider)
        : IDbContextFactory<ListenArrDbContext>
    {
        public ListenArrDbContext CreateDbContext() =>
            database.CreateContext(provider.GetRequiredService<AutomaticSearchResetInterceptor>());
    }
}
