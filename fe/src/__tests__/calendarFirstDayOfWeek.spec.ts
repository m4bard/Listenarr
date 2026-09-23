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
import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import {
  FIRST_DAY_OF_WEEK_STORAGE_KEY,
  getFirstDayOfWeekPreference,
  setFirstDayOfWeekPreference,
  resolveDefaultFirstDayOfWeek,
} from '@/utils/calendarFirstDayOfWeek'

// Readarr and Sonarr both default FirstDayOfWeek from the server's own culture rather than
// a hardcoded Sunday: src/NzbDrone.Core/Configuration/ConfigService.cs:307-311 (Readarr)
// and :322-326 (Sonarr), both
// `GetValueInt("FirstDayOfWeek", (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek)`.
// This is a frontend-only, no-backend-setting piece (see tracker #189), so there is no
// server culture to read. The closest client-side equivalent is the browser's own locale,
// via Intl.Locale's weekInfo. Where that is unavailable, it falls back to Sunday, which
// matches both families' own CultureInfo fallback for the invariant/en-US culture.

describe('resolveDefaultFirstDayOfWeek', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  // Control: an unsupported/absent Intl.Locale must not throw, and must fall back to the
  // same Sunday default the app shipped with before this change.
  it('control: falls back to Sunday when Intl.Locale.getWeekInfo is unavailable', () => {
    vi.stubGlobal('Intl', { ...Intl, Locale: undefined })
    expect(resolveDefaultFirstDayOfWeek('en-US')).toBe(0)
  })

  it('resolves Monday for a locale whose week starts on Monday (ISO firstDay 1)', () => {
    class FakeLocale {
      getWeekInfo() {
        return { firstDay: 1, weekend: [6, 7], minimalDays: 4 }
      }
    }
    vi.stubGlobal('Intl', { ...Intl, Locale: FakeLocale })
    expect(resolveDefaultFirstDayOfWeek('de-DE')).toBe(1)
  })

  it('resolves Sunday for a locale whose week starts on Sunday (ISO firstDay 7)', () => {
    class FakeLocale {
      getWeekInfo() {
        return { firstDay: 7, weekend: [6, 7], minimalDays: 1 }
      }
    }
    vi.stubGlobal('Intl', { ...Intl, Locale: FakeLocale })
    expect(resolveDefaultFirstDayOfWeek('en-US')).toBe(0)
  })

  it('falls back to Sunday when the locale constructor throws', () => {
    class ThrowingLocale {
      constructor() {
        throw new Error('bad locale tag')
      }
    }
    vi.stubGlobal('Intl', { ...Intl, Locale: ThrowingLocale })
    expect(resolveDefaultFirstDayOfWeek('not-a-real-locale')).toBe(0)
  })
})

describe('first day of week preference storage', () => {
  beforeEach(() => {
    window.localStorage.clear()
  })

  it('reads back a persisted Monday preference', () => {
    setFirstDayOfWeekPreference(1)
    expect(window.localStorage.getItem(FIRST_DAY_OF_WEEK_STORAGE_KEY)).toBe('1')
    expect(getFirstDayOfWeekPreference()).toBe(1)
  })

  it('reads back a persisted Sunday preference', () => {
    setFirstDayOfWeekPreference(1)
    setFirstDayOfWeekPreference(0)
    expect(getFirstDayOfWeekPreference()).toBe(0)
  })

  it('falls back to the resolved default when nothing is stored', () => {
    expect(window.localStorage.getItem(FIRST_DAY_OF_WEEK_STORAGE_KEY)).toBeNull()
    expect(getFirstDayOfWeekPreference()).toBe(resolveDefaultFirstDayOfWeek())
  })

  it('ignores a corrupted stored value and falls back to the default', () => {
    window.localStorage.setItem(FIRST_DAY_OF_WEEK_STORAGE_KEY, 'not-a-number')
    expect(getFirstDayOfWeekPreference()).toBe(resolveDefaultFirstDayOfWeek())
  })
})
