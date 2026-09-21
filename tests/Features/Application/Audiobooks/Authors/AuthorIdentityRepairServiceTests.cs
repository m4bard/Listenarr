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
namespace Listenarr.Tests.Features.Application.Audiobooks.Authors
{
    public class AuthorIdentityRepairServiceTests
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
                    MaxRowsPerRun: maxRowsPerRun);

                Coordinator
                    .Setup(coordinator => coordinator.LeaseBudget(It.IsAny<TimeSpan>()))
                    .Returns(budget ?? new UnlimitedBudget());

                MonitoredAuthors
                    .Setup(repository => repository.GetAllAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<MonitoredAuthor>());
            }

            public Harness WithCachedRows(params AuthorCacheEntry[] rows)
            {
                Repository
                    .Setup(repository => repository.GetAuthorCacheEntriesDueForIdentityCheckAsync(
                        It.IsAny<int>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync((int limit, CancellationToken _) => rows.Take(limit).ToList());
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
