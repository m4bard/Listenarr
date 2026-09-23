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

using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Configuration.Core
{
    /// <summary>
    /// The startup configuration half of the configuration service: the settings the process
    /// reads before the database is available (port, SSL, whether the login screen is on) plus
    /// the admin-lockout guard that has to run before a save can enable it. Kept in its own part,
    /// beside ClientConfigurations, Emails and Helpers, rather than growing the main file past
    /// the size the architecture guard allows. Startup configuration never touches
    /// settingsRepository, apiConfigRepository, downloadClientRepository or rootFolderRepository,
    /// which is what makes it a clean boundary from the ApplicationSettings and Prowlarr-import
    /// methods left in the main file.
    /// </summary>
    public partial class ConfigurationService
    {
        public Task<StartupConfig> GetStartupConfigAsync()
        {
            try
            {
                var config = startupConfigService.GetConfig();
                return Task.FromResult(config ?? new StartupConfig());
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogError(ex, "Error retrieving startup configuration");
                return Task.FromResult(new StartupConfig());
            }
        }

        public async Task SaveStartupConfigAsync(StartupConfig config)
        {
            try
            {
                var currentConfig = startupConfigService.GetConfig();

                // The GET that populates the settings screen replaces each
                // secret with ApiResponseRedactor.RedactedValue for callers the
                // redaction gate does not exempt, and the screen posts that same
                // document straight back when the operator saves. Treat the
                // sentinel as "unchanged" and keep what is already stored, the
                // way SaveApplicationSettingsAsync and
                // SaveProwlarrImportSettingsAsync already do for the Prowlarr
                // key. Without this the literal sentinel lands in config.json
                // and the real value is gone.
                //
                // Only the sentinel is special-cased. A blank or absent field
                // still means what it has always meant here, which is clear the
                // value, so an operator who empties the API key box can still
                // do so.
                if (config != null && currentConfig != null)
                {
                    if (string.Equals(config.ApiKey, ApiResponseRedactor.RedactedValue, StringComparison.Ordinal))
                    {
                        config.ApiKey = currentConfig.ApiKey;
                    }

                    if (string.Equals(config.SslCertPassword, ApiResponseRedactor.RedactedValue, StringComparison.Ordinal))
                    {
                        config.SslCertPassword = currentConfig.SslCertPassword;
                    }
                }

                // Defense-in-depth backstop against the auth-enable lockout.
                // SaveApplicationSettingsAsync's throw-on-failure (above) only
                // covers the case where admin credentials were *supplied* but
                // provisioning failed. The settings DTO clears blank fields
                // before save, so a user who flips the login-screen toggle
                // with empty (or username-only) credentials silently skips
                // provisioning entirely — and without this check would still
                // reach the startup-config write below, locking themselves
                // out of an instance that has no working admin to log in as.
                //
                // Only enforced on the *transition* from auth-disabled to
                // auth-enabled. Once auth is already on, the admin must
                // already exist (or no one could have toggled it on through
                // this same check), and every subsequent unrelated save
                // — API key regenerations, port changes, log-level tweaks —
                // shouldn't have to re-prove the admin row is still there.
                // Demotion or deletion of the last admin row while auth is
                // enabled is a separate concern and belongs in the user
                // management path, not here.
                if (config != null && config.IsAuthenticationEnabled())
                {
                    var wasAuthEnabled = currentConfig?.IsAuthenticationEnabled() == true;
                    if (!wasAuthEnabled)
                    {
                        var admins = await userService.GetAdminUsersAsync();
                        if (admins == null || admins.Count == 0)
                        {
                            throw new InvalidOperationException(
                                "Cannot enable the login screen: no admin user exists. " +
                                "Set an admin username and password in the same save to " +
                                "create one, or leave the login screen disabled.");
                        }
                    }
                }

                await startupConfigService.SaveAsync(config!);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogError(ex, "Error saving startup configuration");
                throw;
            }
        }
    }
}
