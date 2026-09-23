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
import { describe, it, expect } from 'vitest'
import { getCalendarEventStatus, CALENDAR_EVENT_STATUSES } from '@/utils/calendarEventStatus'
import type { Audiobook } from '@/types'

// Readarr resolves the same five states in this exact order, and it matters: a book that
// has a file always shows as downloaded even if it is also unmonitored or somehow still
// "downloading" in the queue. frontend/src/Calendar/getStatusStyle.js:4-28 (Readarr) and
// frontend/src/Calendar/getStatusStyle.ts:4-34 (Sonarr) both check hasFile first, then
// downloading, then isMonitored, then the time-based missing/unreleased split. A commit on
// this lineage (819623c55, cited from tracker #189) already corrected a queue-first
// precedence copied from Radarr by mistake, so this test pins the order against Readarr and
// Sonarr, not Radarr.

const NOW = new Date('2026-06-15T00:00:00Z')

function book(overrides: Partial<Audiobook> = {}): Audiobook {
  return {
    id: 1,
    title: 'Test Book',
    monitored: true,
    status: 'no-file',
    files: [],
    ...overrides,
  } as Audiobook
}

describe('getCalendarEventStatus', () => {
  // Control: a monitored book, released in the past, with no file and not downloading is
  // the one case every precedence ordering agrees on. If this ever fails, the test
  // apparatus itself is broken, not just a precedence rule.
  it('control: a monitored, past-released, fileless, non-downloading book is missing', () => {
    const status = getCalendarEventStatus(
      book({ status: 'no-file' }),
      false,
      new Date('2026-01-01T00:00:00Z'),
      NOW,
    )
    expect(status).toBe('missing')
  })

  it('a book with a matching-quality file is downloaded, even when it is also unmonitored', () => {
    const status = getCalendarEventStatus(
      book({ status: 'quality-match', monitored: false }),
      false,
      new Date('2026-01-01T00:00:00Z'),
      NOW,
    )
    expect(status).toBe('downloaded')
  })

  it('a book with a below-cutoff file still counts as downloaded (it has a file on disk)', () => {
    const status = getCalendarEventStatus(
      book({ status: 'quality-mismatch' }),
      false,
      new Date('2026-01-01T00:00:00Z'),
      NOW,
    )
    expect(status).toBe('downloaded')
  })

  it("hasFile wins over downloading, matching the family precedence (not Radarr's queue-first order)", () => {
    const status = getCalendarEventStatus(
      book({ status: 'quality-match' }),
      true,
      new Date('2026-01-01T00:00:00Z'),
      NOW,
    )
    expect(status).toBe('downloaded')
  })

  it('an active download shows as downloading when there is no file yet', () => {
    const status = getCalendarEventStatus(
      book({ status: 'no-file' }),
      true,
      new Date('2026-01-01T00:00:00Z'),
      NOW,
    )
    expect(status).toBe('downloading')
  })

  it('downloading wins over unmonitored', () => {
    const status = getCalendarEventStatus(
      book({ status: 'no-file', monitored: false }),
      true,
      new Date('2026-01-01T00:00:00Z'),
      NOW,
    )
    expect(status).toBe('downloading')
  })

  it('an unmonitored book with no file and not downloading is unmonitored, regardless of release date', () => {
    const past = getCalendarEventStatus(
      book({ status: 'no-file', monitored: false }),
      false,
      new Date('2026-01-01T00:00:00Z'),
      NOW,
    )
    const future = getCalendarEventStatus(
      book({ status: 'no-file', monitored: false }),
      false,
      new Date('2027-01-01T00:00:00Z'),
      NOW,
    )
    expect(past).toBe('unmonitored')
    expect(future).toBe('unmonitored')
  })

  it('a monitored book with no file and a release date in the future is unreleased', () => {
    const status = getCalendarEventStatus(
      book({ status: 'no-file' }),
      false,
      new Date('2027-01-01T00:00:00Z'),
      NOW,
    )
    expect(status).toBe('unreleased')
  })

  it('a monitored book with no file and a release date exactly now is missing, not unreleased', () => {
    const status = getCalendarEventStatus(book({ status: 'no-file' }), false, NOW, NOW)
    expect(status).toBe('missing')
  })

  it('falls back to files/filePath when the server has not sent a status field', () => {
    const withFiles = getCalendarEventStatus(
      book({ status: undefined, files: [{ id: 1, path: '/x' }] }),
      false,
      new Date('2026-01-01T00:00:00Z'),
      NOW,
    )
    const withoutFiles = getCalendarEventStatus(
      book({ status: undefined, files: [] }),
      false,
      new Date('2026-01-01T00:00:00Z'),
      NOW,
    )
    expect(withFiles).toBe('downloaded')
    expect(withoutFiles).toBe('missing')
  })

  it('exposes the full status list in family precedence order for the legend to iterate', () => {
    expect(CALENDAR_EVENT_STATUSES).toEqual([
      'downloaded',
      'downloading',
      'unmonitored',
      'missing',
      'unreleased',
    ])
  })
})
