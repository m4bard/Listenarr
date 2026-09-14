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
import type { Audiobook, AudiobookSeriesMembership } from '@/types'

type SeriesBearer = Pick<Audiobook, 'series' | 'seriesNumber' | 'seriesMemberships'>

/**
 * All series a book belongs to, formatted for display (e.g. "Publication Order #1,
 * Chronological Order #3"). Uses every series membership so a multi-series book shows all of
 * them; falls back to the legacy single series/number when no memberships are present.
 */
export function formatSeriesMemberships(book: SeriesBearer): string {
  const memberships = book.seriesMemberships
  if (memberships && memberships.length > 0) {
    const parts = memberships.map(formatMembership).filter(Boolean)
    if (parts.length > 0) return parts.join(', ')
  }

  const legacyName = (book.series || '').trim()
  if (!legacyName) return ''
  const legacyNumber = (book.seriesNumber || '').trim()
  return legacyNumber ? `${legacyName} #${legacyNumber}` : legacyName
}

function formatMembership(membership: AudiobookSeriesMembership): string {
  const name = (membership.seriesName || '').trim()
  if (!name) return ''
  const number = (membership.seriesNumber || '').trim()
  return number ? `${name} #${number}` : name
}

/**
 * Whether a series position covers more than one book, which is what marks an omnibus or
 * box set. Audible gives such an edition a position like "1-4" and the value is carried as
 * text all the way through, so this is the reliable signal for a library record.
 *
 * Mirrors ReleaseShapeDetector.IsBundleSeriesNumber on the backend, down to the cases that
 * make it awkward: "1.5" is a novella between two books, "2, Dramatized" is a real Audible
 * position for one book, and "20,000" is one grouped number rather than a list of two.
 */
export function isBundleSeriesNumber(seriesNumber?: string | null): boolean {
  const trimmed = (seriesNumber || '').trim()
  if (!trimmed) return false

  // One number, however written, is one book.
  if (/^\d{1,3}(,\d{3})*(\.\d+)?$/.test(trimmed)) return false

  if (/\d+\s*[-\u2010-\u2015]\s*\d+/.test(trimmed)) return true

  const numericParts = trimmed
    .split(/[,;&+]/)
    .map((part) => part.trim())
    .filter((part) => part.length > 0 && /^\d+(\.\d+)?$/.test(part))
  return numericParts.length >= 2
}
