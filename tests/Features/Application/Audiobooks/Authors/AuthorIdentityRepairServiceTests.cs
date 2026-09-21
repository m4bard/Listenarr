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

// Every ASIN below is one the provider really returned during the triage that found this defect.
// B00O0C6Z26 is George Bodenheimer, which is what a fresh instance bound to the name
// "George Makepeace Towle - translator"; B000APTDDU really is Constance Garnett, which is what
// makes her the control rather than a second example.
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Audiobooks.Authors
{
    [Trait("Area", "Library")]
    [Trait("Name", "AuthorIdentityRepairServiceTests")]
    [Trait("Category", "Application")]
    public class AuthorIdentityRepairServiceTests : BaseTests
    {
        private static readonly HttpClient SharedHttpClient = new();

        private sealed class UnlimitedBudget : IMetadataRefreshBudget
        {
            public int RequestsSpent { get; private set; }

            public Task<bool> ChargeAsync(CancellationToken cancellationToken)
            {
                RequestsSpent++;
                return Task.FromResult(true);
            }

            public void ApplyThrottleSignal(TimeSpan? retryAfter)
            {
            }
        }

        private sealed class BudgetOf(int grants) : IMetadataRefreshBudget
        {
            public int RequestsSpent { get; private set; }

            public Task<bool> ChargeAsync(CancellationToken cancellationToken)
            {
                if (RequestsSpent >= grants)
                {
                    return Task.FromResult(false);
                }

                RequestsSpent++;
                return Task.FromResult(true);
            }

            public void ApplyThrottleSignal(TimeSpan? retryAfter)
            {
            }
        }

        private sealed class Harness
        {
            public Mock<IAudiobookRepository> Repository { get; } = new();
            public Mock<IMonitoredAuthorRepository> MonitoredAuthors { get; } = new();
            public Mock<AudibleService> Audible { get; } =
                new(SharedHttpClient, Mock.Of<ILogger<AudibleService>>()) { CallBase = false };
            public Mock<IAudnexusService> Audnexus { get; } = new();
            public Mock<IMetadataRefreshCoordinator> Coordinator { get; } = new();
            public AuthorIdentityRepairOptionsHolder Options { get; } = new();

            public Harness(bool dryRun = false, int maxRowsPerRun = 25, IMetadataRefreshBudget? budget = null)
            {
                Options.Current = new AuthorIdentityRepairOptions(
                    Enabled: true,
                    DryRun: dryRun,
                    IntervalHours: 24,
                    MaxRowsPerRun: maxRowsPerRun,
                    RecheckAfterDays: 30);

                Coordinator
                    .Setup(coordinator => coordinator.LeaseBudget(It.IsAny<TimeSpan>()))
                    .Returns(budget ?? new UnlimitedBudget());

                MonitoredAuthors
                    .Setup(repository => repository.GetAllAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<MonitoredAuthor>());

                // An empty queue unless a test seeds one. Moq answers an unconfigured
                // Task<List<T>> with a null list rather than an empty one, so leaving this out
                // fails as a NullReferenceException inside the pass, which reads as a defect in
                // the code under test rather than as a gap in the fixture.
                Repository
                    .Setup(repository => repository.GetAuthorCacheEntriesDueForIdentityCheckAsync(
                        It.IsAny<DateTime>(),
                        It.IsAny<int>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<AuthorCacheEntry>());

                // Nothing to clean unless a test says otherwise. The credit repair shares this
                // pass's schedule and preview switch and nothing else, so it is out of the way
                // of every test about identities.
                Repository
                    .Setup(repository => repository.CleanRoleSuffixesFromStoredAuthorsAsync(
                        It.IsAny<int>(),
                        It.IsAny<bool>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(StoredAuthorCreditCleanupResult.Nothing);
            }

            public Harness WithCreditsToClean(params StoredAuthorCreditChange[] changes)
            {
                Repository
                    .Setup(repository => repository.CleanRoleSuffixesFromStoredAuthorsAsync(
                        It.IsAny<int>(),
                        It.IsAny<bool>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new StoredAuthorCreditCleanupResult(changes));
                return this;
            }

            public Harness WithCachedRows(params AuthorCacheEntry[] rows)
            {
                Repository
                    .Setup(repository => repository.GetAuthorCacheEntriesDueForIdentityCheckAsync(
                        It.IsAny<DateTime>(),
                        It.IsAny<int>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync((DateTime _, int limit, CancellationToken __) => rows.Take(limit).ToList());
                return this;
            }

            public Harness WithMonitoredRows(params MonitoredAuthor[] rows)
            {
                MonitoredAuthors
                    .Setup(repository => repository.GetAllAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(rows.ToList());
                return this;
            }

            public AuthorIdentityRepairService Build() =>
                new(
                    Repository.Object,
                    MonitoredAuthors.Object,
                    Audible.Object,
                    Audnexus.Object,
                    Coordinator.Object,
                    Options,
                    TimeProvider.System,
                    Mock.Of<ILogger<AuthorIdentityRepairService>>());
        }

        private static AuthorCacheEntry CachedRow(int id, string name, string? asin) =>
            new()
            {
                Id = id,
                AuthorName = name,
                AuthorNameNormalized = StringUtils.NormalizeAuthorName(name),
                AuthorAsin = asin,
                Region = "us",
                Description = "a biography that came with the ASIN",
                ImageUrl = "https://example.invalid/portrait.jpg"
            };

        // A name the provider has no identifier for: Audible answers with a correctly named
        // credit carrying a null ASIN, audnexus answers with a crowd of people who share one
        // word of the name and claim nothing.
        private static void ProviderKnowsNobodyNamed(Harness harness, string name)
        {
            harness.Audible
                .Setup(service => service.LookupAuthorAsync(name, "us"))
                .ReturnsAsync(new AuthorLookupItem { Asin = null, Name = name });
            harness.Audnexus
                .Setup(service => service.SearchAuthorsAsync(name, "us"))
                .ReturnsAsync(new List<AudnexusAuthorSearchResult>
                {
                    new() { Asin = "B001K8SNEG", Name = "George Meegan" },
                    new() { Asin = "B000APBJ7S", Name = "George Plimpton" }
                });
        }

        private static void ProviderSays(Harness harness, string name, string asin)
        {
            harness.Audible
                .Setup(service => service.LookupAuthorAsync(name, "us"))
                .ReturnsAsync(new AuthorLookupItem
                {
                    Asin = asin,
                    Name = name,
                    Description = "the right biography",
                    Image = "https://example.invalid/right.jpg"
                });
        }

        [Fact]
        public async Task Run_RowHoldingAStrangersAsin_IsCorrectedToTheRightOne()
        {
            var harness = new Harness().WithCachedRows(CachedRow(7, "Blanche Bendahan", "B017TI5S3E"));
            ProviderSays(harness, "Blanche Bendahan", "B0BENDAHAN");

            var report = await harness.Build().RunAsync(CancellationToken.None);

            Assert.Equal(1, report.Corrected);
            harness.Repository.Verify(
                repository => repository.ApplyAuthorCacheIdentityAsync(
                    7,
                    "B0BENDAHAN",
                    "the right biography",
                    "https://example.invalid/right.jpg",
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Run_NameThatResolvesToNothing_HasItsAsinClearedRatherThanReplacedWithAGuess()
        {
            var harness = new Harness().WithCachedRows(
                CachedRow(9, "George Makepeace Towle - translator", "B00O0C6Z26"));
            ProviderKnowsNobodyNamed(harness, "George Makepeace Towle - translator");

            var report = await harness.Build().RunAsync(CancellationToken.None);

            Assert.Equal(1, report.Cleared);
            Assert.Equal(0, report.Corrected);

            // Null, and specifically not either of the Georges audnexus offered. Replacing one
            // stranger with another would have looked like a successful repair in every count
            // this pass reports.
            harness.Repository.Verify(
                repository => repository.ApplyAuthorCacheIdentityAsync(
                    9,
                    null,
                    null,
                    null,
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        // THE CONTROL. Without it every test above passes against a pass that rewrites every row
        // it touches, which is the failure mode nobody would notice until it had run.
        [Fact]
        public async Task Run_RowThatIsAlreadyRight_IsStampedAndNotRewritten()
        {
            var harness = new Harness().WithCachedRows(CachedRow(3, "Constance Garnett", "B000APTDDU"));
            ProviderSays(harness, "Constance Garnett", "B000APTDDU");

            var report = await harness.Build().RunAsync(CancellationToken.None);

            Assert.Equal(1, report.AlreadyCorrect);
            Assert.Equal(0, report.Changed);
            harness.Repository.Verify(
                repository => repository.ApplyAuthorCacheIdentityAsync(
                    It.IsAny<int>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            harness.Repository.Verify(
                repository => repository.StampAuthorCacheIdentityCheckedAsync(
                    3,
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Run_TheAsinComparisonIgnoresCase()
        {
            var harness = new Harness().WithCachedRows(CachedRow(3, "Constance Garnett", "b000aptddu"));
            ProviderSays(harness, "Constance Garnett", "B000APTDDU");

            Assert.Equal(1, (await harness.Build().RunAsync(CancellationToken.None)).AlreadyCorrect);
        }

        [Fact]
        public async Task Run_DryRun_ReportsWhatItWouldChangeAndWritesNothingAtAll()
        {
            var harness = new Harness(dryRun: true).WithCachedRows(
                CachedRow(9, "George Makepeace Towle - translator", "B00O0C6Z26"),
                CachedRow(3, "Constance Garnett", "B000APTDDU"));
            ProviderKnowsNobodyNamed(harness, "George Makepeace Towle - translator");
            ProviderSays(harness, "Constance Garnett", "B000APTDDU");

            var report = await harness.Build().RunAsync(CancellationToken.None);

            Assert.True(report.DryRun);
            Assert.Equal(2, report.Examined);
            Assert.Equal(1, report.Cleared);
            Assert.Equal(1, report.AlreadyCorrect);

            // Not one write, and that includes the cursor. A preview that stamped would change
            // the store, which would make "it changed nothing" a claim about the ASIN column
            // rather than about the database, and would stop the preview being repeatable.
            harness.Repository.Verify(
                repository => repository.ApplyAuthorCacheIdentityAsync(
                    It.IsAny<int>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            harness.Repository.Verify(
                repository => repository.StampAuthorCacheIdentityCheckedAsync(
                    It.IsAny<int>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
            harness.MonitoredAuthors.Verify(
                repository => repository.UpsertAsync(It.IsAny<MonitoredAuthor>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // The pass is only worth running if it cannot be talked out of its answer by the row it
        // is examining. The lookup path seeds itself from the persisted ASIN before it asks
        // anybody anything, so a pass built on it would read its own bad row back and confirm it.
        [Fact]
        public async Task Run_DoesNotReadTheAuthorCacheWhileResolving()
        {
            var harness = new Harness().WithCachedRows(
                CachedRow(9, "George Makepeace Towle - translator", "B00O0C6Z26"));
            ProviderKnowsNobodyNamed(harness, "George Makepeace Towle - translator");

            await harness.Build().RunAsync(CancellationToken.None);

            harness.Repository.Verify(
                repository => repository.GetCachedAuthorByNameAsync(It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
            harness.Repository.Verify(
                repository => repository.GetCachedAuthorByAsinAsync(It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
            harness.Repository.Verify(
                repository => repository.GetAuthorAsinByNameAsync(It.IsAny<string>()),
                Times.Never);
        }

        // The same property one level down, and this is the one a plausible implementation gets
        // wrong: passing the stored ASIN to the matcher as an identifier already held would let
        // the stranger's own audnexus row vouch for it, under a name that is not this author's.
        [Fact]
        public async Task Run_DoesNotLetTheStoredAsinVouchForItself()
        {
            var harness = new Harness().WithCachedRows(
                CachedRow(9, "George Makepeace Towle - translator", "B00O0C6Z26"));

            harness.Audible
                .Setup(service => service.LookupAuthorAsync("George Makepeace Towle - translator", "us"))
                .ReturnsAsync(new AuthorLookupItem { Asin = null, Name = "George Makepeace Towle - translator" });

            // Bodenheimer's own row, correctly named for Bodenheimer, carrying the very ASIN the
            // stored row holds.
            harness.Audnexus
                .Setup(service => service.SearchAuthorsAsync("George Makepeace Towle - translator", "us"))
                .ReturnsAsync(new List<AudnexusAuthorSearchResult>
                {
                    new() { Asin = "B00O0C6Z26", Name = "George Bodenheimer" }
                });

            var report = await harness.Build().RunAsync(CancellationToken.None);

            Assert.Equal(1, report.Cleared);
            Assert.Equal(0, report.AlreadyCorrect);
        }

        [Fact]
        public async Task Run_TakesNoMoreRowsThanItsCeiling()
        {
            var rows = Enumerable.Range(1, 10)
                .Select(id => CachedRow(id, $"Author {id}", $"B0000000{id:D2}"))
                .ToArray();
            var harness = new Harness(maxRowsPerRun: 3).WithCachedRows(rows);
            foreach (var row in rows)
            {
                ProviderSays(harness, row.AuthorName, row.AuthorAsin!);
            }

            Assert.Equal(3, (await harness.Build().RunAsync(CancellationToken.None)).Examined);
        }

        // The remainder of the ceiling goes to the monitored table, so one run cannot spend the
        // whole budget on cached rows and never reach the store an operator actually looks at.
        [Fact]
        public async Task Run_SpendsWhatIsLeftOfTheCeilingOnMonitoredAuthors()
        {
            var harness = new Harness(maxRowsPerRun: 3)
                .WithCachedRows(CachedRow(1, "Constance Garnett", "B000APTDDU"))
                .WithMonitoredRows(
                    new MonitoredAuthor { Id = 4, AuthorName = "Aria Sattva", AuthorAsin = "B00AU4XSH8", Region = "us" });
            ProviderSays(harness, "Constance Garnett", "B000APTDDU");
            ProviderKnowsNobodyNamed(harness, "Aria Sattva");

            var report = await harness.Build().RunAsync(CancellationToken.None);

            Assert.Equal(2, report.Examined);
            Assert.Equal(1, report.Cleared);
            harness.MonitoredAuthors.Verify(
                repository => repository.UpsertAsync(
                    It.Is<MonitoredAuthor>(row => row.Id == 4 && row.AuthorAsin == null && row.AuthorIdentityCheckedAt != null),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        // The monitored store is half of what this pass exists for and it is the smaller table by
        // orders of magnitude. Handing the ceiling out first-come meant the cache took all of it
        // on every run of any real library, and the monitored rows were never examined once.
        [Fact]
        public async Task Run_ACacheLargerThanTheCeiling_DoesNotStarveTheMonitoredStore()
        {
            var cached = Enumerable.Range(1, 100)
                .Select(id => CachedRow(id, $"Cached Author {id}", $"B000000{id:D3}"))
                .ToArray();
            var harness = new Harness(maxRowsPerRun: 8)
                .WithCachedRows(cached)
                .WithMonitoredRows(
                    new MonitoredAuthor { Id = 900, AuthorName = "Monitored Author", AuthorAsin = "B00MONITOR", Region = "us" });

            foreach (var row in cached)
            {
                ProviderSays(harness, row.AuthorName, row.AuthorAsin!);
            }

            ProviderSays(harness, "Monitored Author", "B00MONITOR");

            var report = await harness.Build().RunAsync(CancellationToken.None);

            Assert.Equal(8, report.Examined);
            Assert.Contains(report.Decisions, decision => decision.Store == "MonitoredAuthors" && decision.RowId == 900);

            // And the cache still gets the rest of the ceiling rather than a quarter of it.
            Assert.Equal(7, report.Decisions.Count(decision => decision.Store == "AuthorCacheEntries"));
        }

        // The control: an empty monitored store gives its whole share back to the cache, so
        // reserving one does not cost a run anything when there is nothing to reserve it for.
        [Fact]
        public async Task Run_NothingMonitored_GivesTheWholeCeilingToTheCache()
        {
            var cached = Enumerable.Range(1, 100)
                .Select(id => CachedRow(id, $"Cached Author {id}", $"B000000{id:D3}"))
                .ToArray();
            var harness = new Harness(maxRowsPerRun: 8).WithCachedRows(cached);
            foreach (var row in cached)
            {
                ProviderSays(harness, row.AuthorName, row.AuthorAsin!);
            }

            var report = await harness.Build().RunAsync(CancellationToken.None);

            Assert.Equal(8, report.Examined);
            Assert.Equal(8, report.Decisions.Count(decision => decision.Store == "AuthorCacheEntries"));
        }

        // A row checked inside the recheck window is not asked about again. Without this the
        // pass re-asks the provider about the same least recently checked rows on every cycle
        // for as long as the library is larger than the ceiling, learning nothing and spending
        // the shared budget to do it.
        [Fact]
        public async Task Run_AskedTheQueueForRowsOlderThanTheRecheckWindow()
        {
            var harness = new Harness();
            harness.Options.Current = harness.Options.Current with { RecheckAfterDays = 30 };

            await harness.Build().RunAsync(CancellationToken.None);

            var before = DateTime.UtcNow.AddDays(-30);
            harness.Repository.Verify(
                repository => repository.GetAuthorCacheEntriesDueForIdentityCheckAsync(
                    It.Is<DateTime>(cutoff => Math.Abs((cutoff - before).TotalMinutes) < 5),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        // The control for that: zero days means every row is due, which is what an operator
        // working through a known-bad library asks for.
        [Fact]
        public async Task Run_ARecheckWindowOfZero_AsksAboutEverything()
        {
            var harness = new Harness();
            harness.Options.Current = harness.Options.Current with { RecheckAfterDays = 0 };

            await harness.Build().RunAsync(CancellationToken.None);

            var now = DateTime.UtcNow;
            harness.Repository.Verify(
                repository => repository.GetAuthorCacheEntriesDueForIdentityCheckAsync(
                    It.Is<DateTime>(cutoff => Math.Abs((cutoff - now).TotalMinutes) < 5),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Run_OutOfBudget_StopsWithoutStampingTheRowItCouldNotAskAbout()
        {
            var harness = new Harness(budget: new BudgetOf(1)).WithCachedRows(
                CachedRow(1, "Constance Garnett", "B000APTDDU"),
                CachedRow(2, "Blanche Bendahan", "B017TI5S3E"));
            ProviderSays(harness, "Constance Garnett", "B000APTDDU");
            ProviderSays(harness, "Blanche Bendahan", "B0BENDAHAN");

            var report = await harness.Build().RunAsync(CancellationToken.None);

            Assert.True(report.BudgetExhausted);
            Assert.Equal(1, report.Examined);

            // The row it never got to must stay at the head of the queue, so it must not be
            // stamped. Stamping it would quietly retire a row nobody ever checked.
            harness.Repository.Verify(
                repository => repository.StampAuthorCacheIdentityCheckedAsync(
                    2,
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Run_SwitchedOff_DoesNothingAndAsksNobody()
        {
            var harness = new Harness().WithCachedRows(CachedRow(1, "Constance Garnett", "B000APTDDU"));
            harness.Options.Current = harness.Options.Current with { Enabled = false };

            var report = await harness.Build().RunAsync(CancellationToken.None);

            Assert.Equal(0, report.Examined);
            harness.Coordinator.Verify(
                coordinator => coordinator.LeaseBudget(It.IsAny<TimeSpan>()),
                Times.Never);
            harness.Audible.Verify(
                service => service.LookupAuthorAsync(It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        }

        // Budget, not a limiter of its own. If this ever stops being true the pass and the
        // metadata walk are two schedulers pointed at one provider, each politely pacing itself.
        [Fact]
        public async Task Run_SpendsOutOfTheSharedRefreshBudget()
        {
            var budget = new UnlimitedBudget();
            var harness = new Harness(budget: budget).WithCachedRows(
                CachedRow(1, "Constance Garnett", "B000APTDDU"));
            ProviderSays(harness, "Constance Garnett", "B000APTDDU");

            await harness.Build().RunAsync(CancellationToken.None);

            Assert.Equal(1, budget.RequestsSpent);
            harness.Coordinator.Verify(
                coordinator => coordinator.LeaseBudget(TimeSpan.FromHours(24)),
                Times.Once);
        }

        // An Audible outage must not read as "this author does not exist". The lookup returns
        // null both when the search found no products and when the provider could not be
        // reached, because the failure is swallowed below it and an unreachable Audible yields
        // an empty candidate list. Clearing on that would wipe the ASIN off every correct row
        // the pass reached, for as long as the outage lasted.
        [Fact]
        public async Task Run_AudibleAnsweringNothing_LeavesTheRowAloneAndStampsIt()
        {
            var harness = new Harness().WithCachedRows(CachedRow(5, "Constance Garnett", "B000APTDDU"));
            harness.Audible
                .Setup(service => service.LookupAuthorAsync("Constance Garnett", "us"))
                .ReturnsAsync((AuthorLookupItem?)null);

            var report = await harness.Build().RunAsync(CancellationToken.None);

            Assert.Equal(1, report.Unresolved);
            Assert.Equal(0, report.Cleared);
            Assert.Equal(0, report.Corrected);

            // Not written, so a correct row survives the outage.
            harness.Repository.Verify(
                repository => repository.ApplyAuthorCacheIdentityAsync(
                    It.IsAny<int>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);

            // Stamped anyway, so a name the provider genuinely has nothing for cannot sit at the
            // head of the queue forever and stop the pass ever reaching anything else.
            harness.Repository.Verify(
                repository => repository.StampAuthorCacheIdentityCheckedAsync(
                    5,
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);

            // And it never got as far as the second provider, because there was nothing to
            // corroborate.
            harness.Audnexus.Verify(
                service => service.SearchAuthorsAsync(It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        }

        // THE CONTROL for the pair above, and the two differ by one field. Audible answering with
        // the author and no identifier is positive evidence; Audible answering with nothing is
        // not. Same row, same audnexus reply, opposite verdicts.
        [Fact]
        public async Task Run_AudibleNamingTheAuthorWithNoAsin_IsEvidenceAndDoesClear()
        {
            var harness = new Harness().WithCachedRows(CachedRow(5, "Constance Garnett", "B000APTDDU"));
            harness.Audible
                .Setup(service => service.LookupAuthorAsync("Constance Garnett", "us"))
                .ReturnsAsync(new AuthorLookupItem { Asin = null, Name = "Constance Garnett" });
            harness.Audnexus
                .Setup(service => service.SearchAuthorsAsync("Constance Garnett", "us"))
                .ReturnsAsync(new List<AudnexusAuthorSearchResult>
                {
                    new() { Asin = "B00OV1JERO", Name = "Constance Gillam" }
                });

            var report = await harness.Build().RunAsync(CancellationToken.None);

            Assert.Equal(1, report.Cleared);
            Assert.Equal(0, report.Unresolved);
        }

        [Fact]
        public async Task Run_AudnexusNotAnswering_LeavesTheRowAloneAndStampsIt()
        {
            var harness = new Harness().WithCachedRows(CachedRow(5, "Constance Garnett", "B000APTDDU"));
            harness.Audible
                .Setup(service => service.LookupAuthorAsync("Constance Garnett", "us"))
                .ReturnsAsync(new AuthorLookupItem { Asin = null, Name = "Constance Garnett" });
            harness.Audnexus
                .Setup(service => service.SearchAuthorsAsync("Constance Garnett", "us"))
                .ReturnsAsync((List<AudnexusAuthorSearchResult>?)null);

            var report = await harness.Build().RunAsync(CancellationToken.None);

            Assert.Equal(1, report.Unresolved);
            Assert.Equal(0, report.Cleared);
            harness.Repository.Verify(
                repository => repository.StampAuthorCacheIdentityCheckedAsync(
                    5,
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        // An empty answer is still an answer: audnexus knows nobody even loosely by that name.
        [Fact]
        public async Task Run_AudnexusAnsweringWithNobodyAtAll_StillClears()
        {
            var harness = new Harness().WithCachedRows(CachedRow(5, "Constance Garnett", "B000APTDDU"));
            harness.Audible
                .Setup(service => service.LookupAuthorAsync("Constance Garnett", "us"))
                .ReturnsAsync(new AuthorLookupItem { Asin = null, Name = "Constance Garnett" });
            harness.Audnexus
                .Setup(service => service.SearchAuthorsAsync("Constance Garnett", "us"))
                .ReturnsAsync(new List<AudnexusAuthorSearchResult>());

            Assert.Equal(1, (await harness.Build().RunAsync(CancellationToken.None)).Cleared);
        }

        [Fact]
        public async Task Run_ARowWithNoNameAtAll_IsLeftAloneWithoutSpendingARequest()
        {
            var harness = new Harness().WithCachedRows(CachedRow(5, "   ", "B000APTDDU"));

            var report = await harness.Build().RunAsync(CancellationToken.None);

            Assert.Equal(1, report.Unresolved);
            harness.Audible.Verify(
                service => service.LookupAuthorAsync(It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        }

        // The credit repair is bounded by the same ceiling and gated by the same preview switch,
        // and by nothing else. It asks the provider nothing, so a run with no budget left still
        // does it: spelling a credit correctly needs no request.
        [Fact]
        public async Task Run_OutOfBudget_StillCleansTheCreditsItAlreadyHolds()
        {
            var harness = new Harness(budget: new BudgetOf(0))
                .WithCachedRows(CachedRow(1, "Constance Garnett", "B000APTDDU"))
                .WithCreditsToClean(new StoredAuthorCreditChange(
                    42,
                    new[] { "Fyodor Dostoevsky", "Constance Garnett - translator" },
                    new[] { "Fyodor Dostoevsky", "Constance Garnett" }));

            var report = await harness.Build().RunAsync(CancellationToken.None);

            Assert.True(report.BudgetExhausted);
            Assert.Equal(0, report.Examined);
            Assert.Equal(1, report.CreditsCleaned);
            harness.Repository.Verify(
                repository => repository.CleanRoleSuffixesFromStoredAuthorsAsync(
                    It.IsAny<int>(),
                    true,
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        // The control for the pair above: the same call, the same run, and the flag that decides
        // whether it writes follows the preview switch rather than being hardcoded either way.
        [Fact]
        public async Task Run_DryRun_AsksTheCreditRepairToExamineWithoutApplying()
        {
            var harness = new Harness(dryRun: true)
                .WithCreditsToClean(new StoredAuthorCreditChange(
                    42,
                    new[] { "Fyodor Dostoevsky", "Constance Garnett - translator" },
                    new[] { "Fyodor Dostoevsky", "Constance Garnett" }));

            var report = await harness.Build().RunAsync(CancellationToken.None);

            Assert.True(report.DryRun);
            Assert.Equal(1, report.CreditsCleaned);
            harness.Repository.Verify(
                repository => repository.CleanRoleSuffixesFromStoredAuthorsAsync(
                    It.IsAny<int>(),
                    false,
                    It.IsAny<CancellationToken>()),
                Times.Once);
            harness.Repository.Verify(
                repository => repository.CleanRoleSuffixesFromStoredAuthorsAsync(
                    It.IsAny<int>(),
                    true,
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task Run_AProviderThatThrows_ConcludesNothingAndWritesNothing()
        {
            var harness = new Harness().WithCachedRows(CachedRow(1, "Constance Garnett", "B000APTDDU"));
            harness.Audible
                .Setup(service => service.LookupAuthorAsync("Constance Garnett", "us"))
                .ThrowsAsync(new HttpRequestException("the provider is down"));

            var report = await harness.Build().RunAsync(CancellationToken.None);

            Assert.True(report.BudgetExhausted);
            Assert.Equal(0, report.Examined);
            harness.Repository.Verify(
                repository => repository.ApplyAuthorCacheIdentityAsync(
                    It.IsAny<int>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }
    }
}
