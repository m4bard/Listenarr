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

using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Application.Downloads.Status
{
    /// <summary>
    /// The download client failure ladder, rung by rung, against Readarr's constants for download
    /// clients: the shared ten-rung table (ThingiProvider/Status/EscalationBackOff.cs) capped at rung
    /// 5, one hour (Download/DownloadClientStatusService.cs:19), a five-minute grace after the first
    /// failure (:18), and a fifteen-minute startup window that caps any block at rung 2
    /// (ThingiProvider/Status/ProviderStatusServiceBase.cs:33,127-134). Rung values are asserted
    /// exactly, not only for direction, because the cooldown an operator waits through is the value.
    /// </summary>
    [Trait("Area", "DownloadClientStatus")]
    [Trait("Name", "DownloadClientStatusServiceTests")]
    [Trait("Category", "Application")]
    public class DownloadClientStatusServiceTests : BaseTests
    {
        private const string ClientId = "seedbox";
        private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        [Trait("Scenario", "FirstFailureInsideGrace")]
        public async Task RecordFailure_FirstFailure_RecordsTheRunButDoesNotBlockInsideTheInitialGrace()
        {
            var harness = new Harness();

            await harness.Service.RecordFailureAsync(ClientId);

            var status = harness.Stored(ClientId);
            Assert.NotNull(status);
            Assert.Equal(1, status!.EscalationLevel);
            Assert.Equal(Now.UtcDateTime, status.InitialFailure);
            Assert.Equal(Now.UtcDateTime, status.MostRecentFailure);

            // Readarr does not block a download client until it has been failing for five minutes,
            // so one dropped connection does not take a client out of rotation.
            Assert.Null(status.DisabledTill);
            Assert.Empty(await harness.Service.GetBlockedClientIdsAsync());
        }

        [Fact]
        [Trait("Scenario", "Escalation")]
        public async Task RecordFailure_PastTheInitialGrace_ClimbsOneRungPerFailureAndCapsAtOneHour()
        {
            var harness = new Harness();
            await harness.Service.RecordFailureAsync(ClientId);

            TimeSpan[] expected =
            [
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(15),
                TimeSpan.FromMinutes(30),
                TimeSpan.FromHours(1),
                // The cap: rung 5 is as far as a download client goes, where an indexer carries on
                // to a day. A client that has been down for an hour is retried every hour.
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(1)
            ];

            for (var i = 0; i < expected.Length; i++)
            {
                // Past any block each time, so the client would genuinely have been tried again.
                harness.Clock.Advance(TimeSpan.FromHours(2));
                await harness.Service.RecordFailureAsync(ClientId);

                var status = harness.Stored(ClientId)!;
                Assert.Equal(Math.Min(i + 2, DownloadClientStatusService.MaximumEscalationLevel), status.EscalationLevel);
                Assert.Equal(harness.Clock.GetUtcNow().UtcDateTime + expected[i], status.DisabledTill);
                Assert.Equal(Now.UtcDateTime, status.InitialFailure);
            }

            Assert.Equal(new[] { ClientId }, await harness.Service.GetBlockedClientIdsAsync());
        }

        [Fact]
        [Trait("Scenario", "DeEscalation")]
        public async Task RecordSuccess_WalksDownOneRungAndUnblocks()
        {
            var harness = new Harness();
            harness.Seed(new DownloadClientStatus
            {
                ClientId = ClientId,
                InitialFailure = Now.UtcDateTime.AddHours(-3),
                MostRecentFailure = Now.UtcDateTime.AddMinutes(-1),
                EscalationLevel = 4,
                DisabledTill = Now.UtcDateTime.AddMinutes(29)
            });
            Assert.Equal(new[] { ClientId }, await harness.Service.GetBlockedClientIdsAsync());

            await harness.Service.RecordSuccessAsync(ClientId);

            // Decrement, not reset, which is Readarr's choice (ProviderStatusServiceBase.cs:76): a
            // client flapping between failure and success still accumulates.
            var status = harness.Stored(ClientId)!;
            Assert.Equal(3, status.EscalationLevel);
            Assert.Null(status.DisabledTill);
            Assert.Empty(await harness.Service.GetBlockedClientIdsAsync());

            await harness.Service.RecordSuccessAsync(ClientId);
            Assert.Equal(2, harness.Stored(ClientId)!.EscalationLevel);
        }

        [Fact]
        [Trait("Scenario", "SteadyState")]
        public async Task RecordSuccess_OnAHealthyClient_WritesNothing()
        {
            var harness = new Harness();

            await harness.Service.RecordSuccessAsync(ClientId);

            // The steady state has to stay free: every successful queue poll records a success.
            Assert.Equal(0, harness.Repository.Writes);
            Assert.Null(harness.Stored(ClientId));
        }

        [Fact]
        [Trait("Scenario", "StartupWindow")]
        public async Task RecordFailure_InsideTheStartupWindow_DoesNotEscalateAndCapsTheBlock()
        {
            var inside = new Harness(startedAgo: TimeSpan.FromMinutes(1));
            var outside = new Harness();
            foreach (var harness in new[] { inside, outside })
            {
                harness.Seed(new DownloadClientStatus
                {
                    ClientId = ClientId,
                    InitialFailure = Now.UtcDateTime.AddDays(-1),
                    MostRecentFailure = Now.UtcDateTime.AddHours(-2),
                    EscalationLevel = 4
                });
                await harness.Service.RecordFailureAsync(ClientId);
            }

            // A container that comes up before its network does would otherwise bury a client for
            // an hour on the first poll after every restart.
            var capped = inside.Stored(ClientId)!;
            Assert.Equal(4, capped.EscalationLevel);
            Assert.Equal(Now.UtcDateTime + TimeSpan.FromMinutes(5), capped.DisabledTill);

            // Control: the same history outside the window climbs and takes the full rung.
            var full = outside.Stored(ClientId)!;
            Assert.Equal(5, full.EscalationLevel);
            Assert.Equal(Now.UtcDateTime + TimeSpan.FromHours(1), full.DisabledTill);
        }

        [Fact]
        [Trait("Scenario", "UnknownClient")]
        public async Task RecordFailure_ForAClientThatIsNotSaved_StoresNothing()
        {
            var harness = new Harness();
            harness.Repository.KnownClients.Remove(ClientId);

            // A connection test from the add form runs against a client that has no row yet.
            await harness.Service.RecordFailureAsync(ClientId);

            Assert.Null(harness.Stored(ClientId));
        }

        private sealed class Harness
        {
            public Harness(TimeSpan? startedAgo = null)
            {
                Clock = new TestClock(Now);
                var startedAt = Now - (startedAgo ?? DownloadClientBackoffStartupWindow.Duration + TimeSpan.FromMinutes(1));
                Service = new DownloadClientStatusService(
                    Repository,
                    new DownloadClientBackoffStartupWindow(new TestClock(startedAt)),
                    Clock,
                    NullLogger<DownloadClientStatusService>.Instance);
            }

            public TestClock Clock { get; }

            public InMemoryStatusRepository Repository { get; } = new([ClientId]);

            public DownloadClientStatusService Service { get; }

            public DownloadClientStatus? Stored(string clientId) =>
                Repository.Rows.TryGetValue(clientId, out var row) ? row : null;

            public void Seed(DownloadClientStatus status) => Repository.Rows[status.ClientId] = status;
        }
    }

    /// <summary>A clock the test moves by hand.</summary>
    internal sealed class TestClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    /// <summary>
    /// Status rows in a dictionary, copied on the way in and out so a test cannot pass by the
    /// service mutating an object the repository still holds.
    /// </summary>
    internal sealed class InMemoryStatusRepository(IEnumerable<string> knownClients) : IDownloadClientStatusRepository
    {
        public HashSet<string> KnownClients { get; } = new(knownClients, StringComparer.Ordinal);

        public Dictionary<string, DownloadClientStatus> Rows { get; } = new(StringComparer.Ordinal);

        public int Writes { get; private set; }

        public Task<List<DownloadClientStatus>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult(Rows.Values.Select(Copy).ToList());

        public Task<DownloadClientStatus?> GetByClientIdAsync(string clientId, CancellationToken ct = default) =>
            Task.FromResult(Rows.TryGetValue(clientId, out var row) ? Copy(row) : null);

        public Task<bool> UpsertAsync(DownloadClientStatus status, CancellationToken ct = default)
        {
            if (!Rows.ContainsKey(status.ClientId) && !KnownClients.Contains(status.ClientId))
            {
                return Task.FromResult(false);
            }

            Writes++;
            Rows[status.ClientId] = Copy(status);
            return Task.FromResult(true);
        }

        private static DownloadClientStatus Copy(DownloadClientStatus s) => new()
        {
            ClientId = s.ClientId,
            InitialFailure = s.InitialFailure,
            MostRecentFailure = s.MostRecentFailure,
            EscalationLevel = s.EscalationLevel,
            DisabledTill = s.DisabledTill
        };
    }
}
