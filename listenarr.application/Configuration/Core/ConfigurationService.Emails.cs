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
    /// The email notification target half of the configuration service, kept in its own part
    /// beside ClientConfigurations and Helpers rather than growing the main file past the size
    /// the architecture guard allows.
    /// </summary>
    public partial class ConfigurationService
    {
        /// <summary>
        /// Keeps the stored SMTP password for any incoming email target that hands the redaction
        /// sentinel back, and refuses to store the sentinel itself.
        /// </summary>
        /// <remarks>
        /// A settings read replaces every SMTP password with
        /// <see cref="ApiResponseRedactor.RedactedValue"/>, and the settings screen posts that same
        /// document back when the operator saves, so without this the literal word REDACTED would be
        /// written over the password and the real one lost. That has happened here before, to other
        /// secrets, which is why this matches rather than extends the rule already applied to
        /// ProwlarrApiKeyEncrypted above.
        /// <para>
        /// Two cases the scalar rule does not have to think about. Entries are matched by Id, since
        /// a list can be reordered, added to and deleted from between the read and the save. And an
        /// entry carrying the sentinel with no stored counterpart, which is what a newly added
        /// target looks like if a client echoes the sentinel into it, is left blank: storing the
        /// sentinel is the defect, and there is nothing to recover.
        /// </para>
        /// <para>
        /// A blank password is written through as blank. The read always returns the sentinel for a
        /// password that is set, so blank cannot mean "the client did not have the value"; it can
        /// only mean the operator cleared it, and an operator who removes authentication has to be
        /// able to do so.
        /// </para>
        /// </remarks>
        private static void PreserveRedactedEmailPasswords(
            List<EmailConfiguration> incoming,
            List<EmailConfiguration>? existing)
        {
            foreach (var email in incoming)
            {
                if (!string.Equals(email.Password, ApiResponseRedactor.RedactedValue, StringComparison.Ordinal))
                {
                    continue;
                }

                var stored = existing?.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, email.Id, StringComparison.Ordinal));

                email.Password = stored?.Password ?? string.Empty;
            }
        }

        public async Task<List<EmailConfiguration>> GetEmailConfigurationsAsync()
        {
            try
            {
                var settings = await GetApplicationSettingsAsync();
                return settings?.Emails ?? new List<EmailConfiguration>();
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogError(ex, "Error retrieving email notification configurations");
                return new List<EmailConfiguration>();
            }
        }
    }
}
