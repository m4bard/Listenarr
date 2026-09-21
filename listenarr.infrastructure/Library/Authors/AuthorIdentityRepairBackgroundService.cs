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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Authors;

/// <summary>The cycle body, kept out of the hosted service so it can be tested without a host.</summary>
public interface IAuthorIdentityRepairProcessor
{
    Task RunCycleAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Puts the author identity repair pass on the task surface, daily, and lets it be brought
/// forward by hand.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an interval and not manual only.</b> The family's answer for stored data that has gone
/// wrong is Housekeeping, and in Readarr's TaskManager that is a scheduled task at 24 * 60
/// minutes, alongside RefreshAuthorCommand on the same daily period. One of its housekeepers,
/// UpdateCleanTitleForAuthor, rewrites a derived author field on every row that disagrees with
/// it, daily, with no preview and nothing to switch on. So a daily sweep is the family-shaped
/// default, and the same file is the argument: Readarr's explicit author refresh is a Command,
/// which this surface has no equivalent of, and "manual only" is not expressible here at all --
/// a registered task is always driven by its own loop, and the allowlist only decides whether it
/// may additionally be started on demand.
/// </para>
/// <para>
/// What carries the difference instead is the preview. Every Readarr housekeeper recomputes
/// something locally from data already held; this one asks a provider who somebody is, and acts
/// on the answer. So it ships switched off, and the state it is switched into is the one that
/// writes nothing. A daily schedule over a preview costs a bounded number of provider requests
/// and changes not one row.
/// </para>
/// <para>
/// <b>Manual runs are allowed</b> because the pass is bounded per cycle and its remainder is
/// left at the head of the queue, so bringing it forward is how an operator works through a
/// backlog without waiting a day per batch. The allowlist's own rule is that a task which
/// deletes or prunes must not be on it; this one clears an identifier that was somebody else's
/// and never removes a row, an author or a book.
/// </para>
/// </remarks>
public class AuthorIdentityRepairBackgroundService(
    ILogger<AuthorIdentityRepairBackgroundService> logger,
    IAuthorIdentityRepairProcessor processor,
    IWorkerCycleRunner cycleRunner,
    AuthorIdentityRepairOptionsHolder options,
    IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Before the announcement, so the line names the operator's interval rather than the
        // shipped one.
        using (var scope = scopeFactory.CreateScope())
        {
            await AuthorIdentityRepairOptionsLoader.LoadAsync(
                scope.ServiceProvider,
                options,
                logger,
                stoppingToken);
        }

        var current = options.Current;
        logger.LogInformation(
            "AuthorIdentityRepairBackgroundService started. Author identities will be {Mode} every {Hours} hours",
            current.Enabled
                ? (current.DryRun ? "previewed" : "repaired")
                : "left alone (the pass is switched off)",
            current.IntervalHours);

        await cycleRunner.RunPeriodicAsync(
            nameof(AuthorIdentityRepairBackgroundService),
            initialDelay: TimeSpan.FromMinutes(15),
            intervalProvider: () => TimeSpan.FromHours(Math.Max(1, options.Current.IntervalHours)),
            runCycle: processor.RunCycleAsync,
            stoppingToken,
            // Bounded per cycle and resumable, so an operator working through a backlog brings
            // the next batch forward instead of waiting a day for it. It corrects and clears
            // identifiers; it deletes nothing.
            manualTrigger: ScheduledTaskManualTrigger.Allowed);

        logger.LogInformation("AuthorIdentityRepairBackgroundService stopped");
    }
}

public class AuthorIdentityRepairProcessor(
    ILogger<AuthorIdentityRepairProcessor> logger,
    AuthorIdentityRepairOptionsHolder options,
    IServiceScopeFactory scopeFactory) : IAuthorIdentityRepairProcessor
{
    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        // Every cycle, not once at start. An operator who turns the pass on, or turns the
        // preview off, must not have to restart the process for it to take effect, and the
        // manual trigger replays this same body.
        await AuthorIdentityRepairOptionsLoader.LoadAsync(
            scope.ServiceProvider,
            options,
            logger,
            cancellationToken);

        if (!options.Current.Enabled)
        {
            logger.LogDebug("Author identity repair is switched off; this cycle did nothing");
            return;
        }

        var service = scope.ServiceProvider.GetRequiredService<IAuthorIdentityRepairService>();
        await service.RunAsync(cancellationToken);
    }
}
