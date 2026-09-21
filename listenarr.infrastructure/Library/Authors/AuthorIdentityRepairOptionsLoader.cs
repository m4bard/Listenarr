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

/// <summary>Reads the operator's settings into the live options holder.</summary>
/// <remarks>
/// Same shape as the metadata refresh loader beside it, including the part that matters: a scope
/// with no configuration service in it, and a settings read that throws, both leave the values
/// already in use alone. A pass is better off with yesterday's settings than with the shipped
/// ones, because one of the shipped ones is "enabled: false" and silently reverting to it would
/// look exactly like the schedule having stopped.
/// </remarks>
public static class AuthorIdentityRepairOptionsLoader
{
    // Bounds at use rather than at save, matching the refresh loader, and the same known
    // constraint applies: GET /settings echoes back whatever was stored. A ceiling of one row is
    // the floor because a pass that examines nothing cannot be told from one that is switched
    // off, and 500 is well past anything the hourly budget could feed in a day.
    private const int MinIntervalHours = 1;
    private const int MaxIntervalHours = 168;
    private const int MinMaxRowsPerRun = 1;
    private const int MaxMaxRowsPerRun = 500;

    // Zero is meaningful and is the floor on purpose: it asks about every row on every run,
    // which is what an operator working through a known-bad library wants and is exactly the
    // behaviour the cutoff was added to stop happening by default.
    private const int MinRecheckAfterDays = 0;
    private const int MaxRecheckAfterDays = 3650;

    public static async Task LoadAsync(
        IServiceProvider scopeProvider,
        AuthorIdentityRepairOptionsHolder holder,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeProvider);
        ArgumentNullException.ThrowIfNull(holder);

        try
        {
            var configuration = scopeProvider.GetService<IConfigurationService>();
            if (configuration == null)
            {
                logger.LogDebug(
                    "No configuration service in this scope; keeping the author identity repair options already in use");
                return;
            }

            var settings = await configuration.GetApplicationSettingsAsync();
            cancellationToken.ThrowIfCancellationRequested();
            holder.Current = new AuthorIdentityRepairOptions(
                settings.AuthorIdentityRepairEnabled,
                settings.AuthorIdentityRepairDryRun,
                Math.Clamp(settings.AuthorIdentityRepairIntervalHours, MinIntervalHours, MaxIntervalHours),
                Math.Clamp(settings.AuthorIdentityRepairMaxRowsPerRun, MinMaxRowsPerRun, MaxMaxRowsPerRun),
                Math.Clamp(settings.AuthorIdentityRepairRecheckAfterDays, MinRecheckAfterDays, MaxRecheckAfterDays));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not read the author identity repair settings; keeping the options already in use");
        }
    }
}
