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

namespace Listenarr.Tests.Features.Application.Search.Contracts;

[Trait("Name", "CycleRequestAllowanceTests")]
[Trait("Category", "Application")]
public sealed class CycleRequestAllowanceTests : BaseTests
{
    private const int ThreadCount = 64;
    private const int MaxRequests = 10;

    [Fact]
    public void TryTake_ParallelCallers_TakeExactlyTheMaximum()
    {
        // Given
        var allowance = new CycleRequestAllowance(MaxRequests);

        // When
        var successes = RunContendedTakes([allowance]);

        // Then
        Assert.Equal(MaxRequests, successes[0]);
        Assert.Equal(MaxRequests, allowance.Taken);
    }

    [Fact]
    public void TryTake_RepeatedContention_NeverTakesMoreThanTheMaximum()
    {
        // Given
        const int iterations = 1000;
        var allowances = Enumerable.Range(0, iterations)
            .Select(_ => new CycleRequestAllowance(MaxRequests))
            .ToArray();

        // When
        var successes = RunContendedTakes(allowances);

        // Then
        var violations = Enumerable.Range(0, iterations)
            .Where(i => successes[i] != MaxRequests || allowances[i].Taken != MaxRequests)
            .Select(i => $"iteration {i}: {successes[i]} taken, Taken reports {allowances[i].Taken}")
            .ToArray();
        Assert.True(
            violations.Length == 0,
            $"Allowance of {MaxRequests} overshot or undershot:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void TryTake_ZeroMaximum_TakesNothing()
    {
        // Given
        var allowance = new CycleRequestAllowance(0);

        // When
        var taken = allowance.TryTake();

        // Then
        Assert.False(taken);
        Assert.Equal(0, allowance.Taken);
    }

    [Fact]
    public void Constructor_NegativeMaximum_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CycleRequestAllowance(-1));
    }

    /// <summary>
    /// Runs <see cref="ThreadCount"/> threads that each call TryTake once on every allowance in
    /// turn, released together by a barrier so the calls on one allowance contend.
    /// </summary>
    private static int[] RunContendedTakes(IReadOnlyList<CycleRequestAllowance> allowances)
    {
        var successes = new int[allowances.Count];
        using var barrier = new Barrier(ThreadCount);
        var threads = Enumerable.Range(0, ThreadCount)
            .Select(_ => new Thread(() =>
            {
                for (var i = 0; i < allowances.Count; i++)
                {
                    barrier.SignalAndWait();
                    if (allowances[i].TryTake())
                    {
                        Interlocked.Increment(ref successes[i]);
                    }
                }
            })
            { IsBackground = true })
            .ToArray();

        foreach (var thread in threads)
        {
            thread.Start();
        }

        foreach (var thread in threads)
        {
            Assert.True(thread.Join(TimeSpan.FromMinutes(1)), "A contending thread did not finish.");
        }

        return successes;
    }
}
