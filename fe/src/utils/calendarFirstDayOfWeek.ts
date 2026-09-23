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

/**
 * 0 = Sunday, 1 = Monday, matching Date.prototype.getDay()'s convention (and the calendar
 * grid's own weekDays array), not the ISO weekday numbering Intl.Locale's weekInfo uses.
 */
export type FirstDayOfWeek = 0 | 1

export const FIRST_DAY_OF_WEEK_STORAGE_KEY = 'listenarr.calendar.firstDayOfWeek'

/**
 * Minimal shape of the weekInfo object Intl.Locale.prototype.getWeekInfo() returns.
 * `firstDay` is ISO weekday numbering: 1 = Monday .. 7 = Sunday. Not in the project's
 * configured TS lib target (ES2022 + DOM; getWeekInfo lands in lib.esnext.intl.d.ts), so
 * this is read through an untyped access rather than importing the newer lib.
 */
interface LocaleWeekInfo {
  firstDay: number
}

/**
 * Readarr and Sonarr default FirstDayOfWeek from the server's culture, not a hardcoded
 * Sunday: src/NzbDrone.Core/Configuration/ConfigService.cs:307-311 (Readarr) and :322-326
 * (Sonarr), both `GetValueInt("FirstDayOfWeek", (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek)`.
 *
 * This piece is deliberately frontend-only (tracker #189: "do not add a backend setting"),
 * so there is no server CultureInfo to read. The closest client-side equivalent is the
 * browser's own locale, via Intl.Locale's weekInfo extension. When that API is unavailable
 * (older engines; it postdates the project's configured TS lib target) this falls back to
 * Sunday, matching both families' own behaviour under the invariant/en-US culture.
 */
export function resolveDefaultFirstDayOfWeek(localeTag?: string): FirstDayOfWeek {
  try {
    const LocaleCtor = (Intl as unknown as { Locale?: new (tag: string) => unknown }).Locale
    if (typeof LocaleCtor !== 'function') {
      return 0
    }

    const tag = localeTag ?? (typeof navigator !== 'undefined' ? navigator.language : 'en-US')
    const locale = new LocaleCtor(tag) as {
      getWeekInfo?: () => LocaleWeekInfo
      weekInfo?: LocaleWeekInfo
    }

    const weekInfo =
      typeof locale.getWeekInfo === 'function' ? locale.getWeekInfo() : locale.weekInfo

    if (!weekInfo || typeof weekInfo.firstDay !== 'number') {
      return 0
    }

    // ISO firstDay: 1 = Monday, 7 = Sunday. Only Sunday/Monday are offered as options
    // (G7's stated minimum), so any other ISO first day (e.g. Saturday, firstDay 6, used
    // by some Middle Eastern locales) falls back to Sunday rather than silently degrading
    // to a value the UI cannot represent.
    if (weekInfo.firstDay === 1) return 1
    if (weekInfo.firstDay === 7) return 0
    return 0
  } catch {
    return 0
  }
}

export function getFirstDayOfWeekPreference(): FirstDayOfWeek {
  try {
    const stored = window.localStorage.getItem(FIRST_DAY_OF_WEEK_STORAGE_KEY)
    if (stored === '0') return 0
    if (stored === '1') return 1
  } catch {
    // Ignore storage failures (private mode, disabled storage, quota, etc.) and fall
    // through to the resolved default below.
  }

  return resolveDefaultFirstDayOfWeek()
}

export function setFirstDayOfWeekPreference(day: FirstDayOfWeek): void {
  try {
    window.localStorage.setItem(FIRST_DAY_OF_WEEK_STORAGE_KEY, String(day))
  } catch {
    // Ignore storage failures (private mode, disabled storage, quota, etc.)
  }
}
