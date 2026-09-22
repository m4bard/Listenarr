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

namespace Listenarr.Infrastructure.Maintenance.Housekeeping;

/// <summary>Reads the operator's settings into the live options holder.</summary>
/// <remarks>
/// Same shape as the author identity repair loader, including the part that matters: a scope with
/// no configuration service in it, and a settings read that throws, both leave the values already
/// in use alone. For a sweep that deletes, keeping yesterday's settings is also the conservative
/// answer, because reverting to the shipped ones would silently restore a window the operator had
/// widened.
/// </remarks>
public static class HousekeepingOptionsLoader
{
    // Bounded at use rather than at save, matching the other loaders, and for the same known
    // reason: GET /settings echoes back whatever was stored, so clamping at save would show the
    // operator a number they did not type.
    private const int MinRetentionDays = 1;
    private const int MaxRetentionDays = 3650;

    public static async Task LoadAsync(
        IServiceProvider scopeProvider,
        HousekeepingOptionsHolder holder,
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
                    "No configuration service in this scope; keeping the housekeeping options already in use");
                return;
            }

            var settings = await configuration.GetApplicationSettingsAsync();
            cancellationToken.ThrowIfCancellationRequested();
            holder.Current = new HousekeepingOptions(
                ClampRetentionDays(settings.HousekeepingRetentionDays),
                settings.HousekeepingDryRun);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not read the housekeeping settings; keeping the options already in use");
        }
    }

    /// <summary>
    /// Zero survives the clamp because zero is a meaning rather than a bound: it is the family's
    /// own "disable automatic cleanup", and it is already what unlimited retention means
    /// elsewhere in this codebase. A negative lands on zero rather than on the floor, so a
    /// nonsense value fails towards keeping rows.
    /// </summary>
    private static int ClampRetentionDays(int configured) =>
        configured <= HousekeepingOptions.UnlimitedRetentionDays
            ? HousekeepingOptions.UnlimitedRetentionDays
            : Math.Clamp(configured, MinRetentionDays, MaxRetentionDays);
}
