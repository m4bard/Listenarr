/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using Listenarr.Application.Search.Scheduling;
using Listenarr.Infrastructure.Persistence.Repositories;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;
using Listenarr.Tests.Features.Infrastructure.Persistence;
using Listenarr.Tests.Features.Infrastructure.Persistence.LargeLibrary;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.HostedServices.Search.Sweep
{
    /// <summary>
    /// Drives the real automatic-search processor over a migrated SQLite database, with a settable
    /// clock, a scripted indexer provider behind the real pass-through gate, and a stub server that
    /// counts the requests that reach it. Scoring and the download start are mocked; a result's
    /// <see cref="BaseSearchResult.Source"/> says how scoring treats it.
    /// </summary>
    public abstract class AutomaticSearchSweepTestBase : BaseTests
    {
        protected static readonly DateTime StartUtc = new(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);
        protected const string AcceptableSource = "acceptable";
        protected const string RejectedSource = "rejected";
        protected const string ScoringThrowsSource = "scoring-throws";
        protected const string StartThrowsSource = "start-throws";

        private static readonly IReadOnlyList<LargeLibraryBook> CorpusBooks = LargeLibraryFixtureBuilder.LoadBooks()
            .DistinctBy(book => (book.Title, book.Authors.FirstOrDefault(), book.Series))
            .ToList();

        private AutomaticSearchScheduleTestDatabase? _database;
        private int _nextCorpusBook;
        private int _qualityProfileId;
        private IQualityProfileService? _quality;

        internal SweepClock Clock { get; } = new(StartUtc);
        internal StubIndexerServer Server { get; private set; } = null!;
        internal ScriptedGate Gate { get; private set; } = null!;
        internal ScriptedSearchProvider Indexers { get; private set; } = null!;
        internal RecordingLogger<AutomaticSearchProcessor> Log { get; } = new();
        protected Mock<IDownloadService> Downloads { get; } = new();

        /// <summary>Real providers served beside the scripted one; applied by <see cref="BuildHost"/>.</summary>
        internal List<IIndexerSearchProvider> AdditionalProviders { get; } = new();

        /// <summary>Wraps the schedule store the processor resolves, when a test sets it.</summary>
        internal Func<IAutomaticSearchScheduleStore, IAutomaticSearchScheduleStore>? DecorateStore { get; set; }

        internal AutomaticSearchScheduleTestDatabase Database => _database!;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            Server = new StubIndexerServer(Clock);
            Gate = new ScriptedGate(Clock);
            Indexers = new ScriptedSearchProvider(Gate, Server);
            _database = new AutomaticSearchScheduleTestDatabase(FileService.GetTempPath());
            await _database.MigrateAsync();

            var quality = new Mock<IQualityProfileService>();
            quality
                .Setup(q => q.ScoreSearchResults(It.IsAny<List<SearchResult>>(), It.IsAny<QualityProfile>()))
                .ReturnsAsync((List<SearchResult> results, QualityProfile _) =>
                    results.Any(result => result.Source == ScoringThrowsSource)
                        ? throw new InvalidOperationException("scoring failed")
                        : results.Select(Score).ToList());
            Downloads
                .Setup(d => d.StartDownloadAsync(It.IsAny<SearchResult>(), It.IsAny<string>(), It.IsAny<int?>()))
                .ReturnsAsync((SearchResult result, string _, int? _) =>
                    result.Source == StartThrowsSource ? throw new InvalidOperationException("client refused") : "download");

            BuildHost(quality.Object);

            await using var context = _database.CreateContext();
            var profile = new QualityProfile { Name = "Any" };
            context.QualityProfiles.Add(profile);
            await context.SaveChangesAsync();
            _qualityProfileId = profile.Id;
        }

        /// <summary>
        /// (Re)builds the service provider over the same database, as a process restart would.
        /// </summary>
        protected void BuildHost(IQualityProfileService? quality = null)
        {
            _quality = quality ?? _quality;
            var factory = new DatabaseFactory(_database!);
            Init(builder => builder
                .WithSingleton<IDbContextFactory<ListenArrDbContext>>(factory)
                .WithSingleton<TimeProvider>(Clock)
                .WithSingleton<IIndexerRequestGate>(Gate)
                .WithMocks(new[] { Indexers }
                    .Concat(AdditionalProviders)
                    .Select(provider => ServiceDescriptor.Singleton(provider)))
                .WithSingleton(_quality!)
                .WithSingleton(Downloads.Object)
                .WithMocks(
                    ServiceDescriptor.Scoped<ListenArrDbContext>(_ => factory.CreateDbContext()),
                    ServiceDescriptor.Scoped<IAutomaticSearchScheduleStore>(provider =>
                    {
                        var store = new EfAutomaticSearchScheduleStore(provider.GetRequiredService<ListenArrDbContext>());
                        return DecorateStore?.Invoke(store) ?? store;
                    })));
        }

        protected async Task ConfigureAsync(int maxBooks = 1000, int maxRequests = 10000)
        {
            await using var context = _database!.CreateContext();
            var settings = await context.ApplicationSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new ApplicationSettings();
                context.ApplicationSettings.Add(settings);
            }

            settings.OutputPath = FileService.GetTempPath();
            settings.Version = Math.Max(settings.Version, 1);
            settings.AutomaticSearchBookDelaySeconds = 0;
            settings.AutomaticSearchCooldownHours = 24;
            settings.AutomaticSearchMaxBooksPerCycle = maxBooks;
            settings.AutomaticSearchMaxRequestsPerCycle = maxRequests;
            await context.SaveChangesAsync();
        }

        protected async Task<int> AddIndexerAsync(string name, string? additionalSettings = null)
        {
            await using var context = _database!.CreateContext();
            var indexer = new IndexerBuilder()
                .WithName(name)
                .WithImplementation(ScriptedSearchProvider.Implementation)
                .WithUrl("https://indexer.invalid")
                .WithEnabled()
                .Build();
            indexer.EnableAutomaticSearch = true;
            indexer.AdditionalSettings = additionalSettings;
            context.Indexers.Add(indexer);
            await context.SaveChangesAsync();
            return indexer.Id;
        }

        /// <summary>
        /// Adds monitored books, each a different corpus entry, due at <paramref name="dueUtc"/>.
        /// Returns each book's id and the query the sweep sends for it.
        /// </summary>
        protected async Task<List<SeededBook>> AddBooksAsync(int count, DateTime dueUtc)
        {
            await using var context = _database!.CreateContext();
            var added = new List<Audiobook>();
            for (var index = 0; index < count; index++)
            {
                var corpus = CorpusBooks[_nextCorpusBook++ % CorpusBooks.Count];
                var builder = new AudiobookBuilder().WithTitle(corpus.Title).WithMonitored();
                if (corpus.Authors.Count > 0)
                {
                    builder.WithAuthor(corpus.Authors[0]);
                }

                var book = builder.Build();
                book.Series = corpus.Series;
                book.QualityProfileId = _qualityProfileId;
                book.AutomaticSearchDueUtc = dueUtc;
                added.Add(book);
            }

            context.Audiobooks.AddRange(added);
            await context.SaveChangesAsync();
            return added.Select(book => new SeededBook(book.Id, QueryFor(book), FallbackQueryFor(book), ReleaseNameFor(book))).ToList();
        }

        protected Task RunCycleAsync(CancellationToken cancellationToken = default) =>
            new AutomaticSearchProcessor(Log, _provider.GetRequiredService<IServiceScopeFactory>())
                .RunCycleAsync(cancellationToken);

        /// <summary>The queries the sweep sent since the last call, in order.</summary>
        protected List<(int IndexerId, string Query, IndexerRequestKind Kind)> TakeSearches()
        {
            var taken = new List<(int, string, IndexerRequestKind)>();
            while (Indexers.Searches.TryDequeue(out var search))
            {
                taken.Add(search);
            }

            return taken;
        }

        internal static IndexerScript Returns(string source, int requests = 1, string downloadType = DirectDownloadMetadataKeys.ClientId) =>
            new(requests, new[] { new IndexerSearchResult { Title = "release", Source = source, DownloadType = downloadType } });

        // As the indexer receives it: the workflow sanitizes every query before dispatch.
        private static string QueryFor(Audiobook book) =>
            IndexerQuerySanitizer.Sanitize(new AutomaticSearchResultClassifier(NullLogger.Instance).BuildSearchQuery(book));

        private static string FallbackQueryFor(Audiobook book) =>
            IndexerQuerySanitizer.Sanitize(new AutomaticSearchResultClassifier(NullLogger.Instance).BuildTitleFallbackSearchQuery(book));

        // A release layout the fallback's title guard accepts: "Author - Title".
        private static string ReleaseNameFor(Audiobook book) =>
            string.Join(" - ", new[] { book.Authors?.FirstOrDefault(), book.Title }.Where(part => !string.IsNullOrEmpty(part)));

        private static QualityScore Score(SearchResult result) => new()
        {
            SearchResult = result,
            TotalScore = 100,
            RejectionReasons = result.Source == RejectedSource ? new List<string> { "rejected by the test" } : new List<string>()
        };

        protected sealed record SeededBook(int Id, string Query, string FallbackQuery, string ReleaseName);

        private sealed class DatabaseFactory(AutomaticSearchScheduleTestDatabase database) : IDbContextFactory<ListenArrDbContext>
        {
            public ListenArrDbContext CreateDbContext() => database.CreateContext();
        }
    }
}
