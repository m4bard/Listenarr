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
import type { Audiobook } from '@/types'

/**
 * The calendar's per-event status, resolved in the same precedence order Readarr and
 * Sonarr use for their calendar event chips: a file on disk wins over everything else,
 * then an in-flight download, then the monitored flag, then whether the release date has
 * passed. See {@link getCalendarEventStatus} for the citations.
 */
export type CalendarEventStatus =
  | 'downloaded'
  | 'downloading'
  | 'unmonitored'
  | 'missing'
  | 'unreleased'

/** Family precedence order, also the order the legend renders in. */
export const CALENDAR_EVENT_STATUSES: CalendarEventStatus[] = [
  'downloaded',
  'downloading',
  'unmonitored',
  'missing',
  'unreleased',
]

type StatusInput = Pick<Audiobook, 'status' | 'files' | 'filePath' | 'monitored'>

/**
 * Whether the audiobook has a file on disk, regardless of whether that file meets the
 * quality cutoff. Trusts the backend's server-computed `status` first (the `/library` list
 * endpoint runs AudiobookStatusEvaluator.ComputeStatus, same as
 * src/utils/audiobookStatus.ts's computeAudiobookStatus), and only falls back to the raw
 * files/filePath fields when the server has not sent a status.
 */
function hasFile(book: StatusInput): boolean {
  if (book.status === 'quality-match' || book.status === 'quality-mismatch') {
    return true
  }
  if (book.status === 'no-file' || book.status === 'downloading') {
    return false
  }

  const hasFiles = Array.isArray(book.files) ? book.files.length > 0 : false
  const hasPrimaryFile = !!(book.filePath && book.filePath.toString().trim() !== '')
  return hasFiles || hasPrimaryFile
}

/**
 * Resolves a calendar event's status.
 *
 * Precedence cited from the family, checked in this order:
 * 1. hasFile -> 'downloaded'. Readarr: frontend/src/Calendar/getStatusStyle.js:8-10
 *    (`if (percentOfBooks === 100) return 'downloaded'`). Sonarr:
 *    frontend/src/Calendar/getStatusStyle.ts:13-15 (`if (hasFile) return 'downloaded'`).
 * 2. downloading -> 'downloading'. Readarr :16-18, Sonarr :17-19.
 * 3. !monitored -> 'unmonitored'. Readarr :20-22, Sonarr :21-23.
 * 4. time vs release date -> 'missing' once released, 'unreleased' before. Readarr :24-27
 *    (`currentTime.isAfter(startTime) ? 'missing' : 'unreleased'`), Sonarr :25-31
 *    (onAir/missing/unaired split, collapsed here since audiobooks have no air window).
 *
 * This is the corrected order: a commit on this lineage (819623c55, per tracker #189)
 * fixed a queue-first precedence that had been copied from Radarr by mistake, where
 * downloading was checked ahead of hasFile. Radarr is deliberately not cited as precedent
 * here.
 */
export function getCalendarEventStatus(
  book: StatusInput,
  isDownloading: boolean,
  releaseDate: Date,
  now: Date = new Date(),
): CalendarEventStatus {
  if (hasFile(book)) {
    return 'downloaded'
  }

  if (isDownloading) {
    return 'downloading'
  }

  if (!book.monitored) {
    return 'unmonitored'
  }

  if (now.getTime() >= releaseDate.getTime()) {
    return 'missing'
  }

  return 'unreleased'
}

const STATUS_LABELS: Record<CalendarEventStatus, string> = {
  downloaded: 'Downloaded',
  downloading: 'Downloading',
  unmonitored: 'Unmonitored',
  missing: 'Missing',
  unreleased: 'Unreleased',
}

export function formatCalendarEventStatus(status: CalendarEventStatus): string {
  return STATUS_LABELS[status]
}
