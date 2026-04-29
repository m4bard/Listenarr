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
export const SECURITY_WARNING_BANNER_PREF_KEY = 'listenarr.hideNoAuthSecurityBanner'
export const SECURITY_WARNING_BANNER_PREF_EVENT =
  'listenarr:security-warning-banner-preference-changed'

export function getSecurityWarningBannerHiddenPreference(): boolean {
  try {
    return window.localStorage.getItem(SECURITY_WARNING_BANNER_PREF_KEY) === 'true'
  } catch {
    return false
  }
}

export function setSecurityWarningBannerHiddenPreference(hidden: boolean): void {
  try {
    window.localStorage.setItem(SECURITY_WARNING_BANNER_PREF_KEY, hidden ? 'true' : 'false')
  } catch {
    // Ignore storage failures (private mode, disabled storage, quota, etc.)
  }

  try {
    window.dispatchEvent(
      new CustomEvent<boolean>(SECURITY_WARNING_BANNER_PREF_EVENT, {
        detail: hidden,
      }),
    )
  } catch {
    // Ignore dispatch failures
  }
}
