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

/** A series entry as the search endpoint and the product lookup both return it. */
export interface SeriesEntry {
  asin?: string
  name?: string
  position?: string
}

/** Series data in the shape AudibleBookMetadata expects, memberships and legacy scalars alike. */
export interface SeriesFields {
  seriesMemberships?: AudiobookSeriesMembership[]
  series?: string
  seriesNumber?: string
  seriesAsin?: string
}

// An Audible digital ASIN is the literal prefix B0 followed by eight alphanumerics. A bare
// ten-character check is not enough: single-word series names of that length, Foundation
// among them, pass it and then get stored as if they were identifiers.
const ASIN_PATTERN = /^B0[A-Z0-9]{8}$/i

export function looksLikeAsin(value: string | undefined | null): boolean {
  const trimmed = value?.trim()
  return Boolean(trimmed && ASIN_PATTERN.test(trimmed))
}

/**
 * A book can belong to more than one series, so every entry becomes a membership, ordered,
 * with the first marked primary. The legacy scalars come off that primary membership so the
 * two can never disagree.
 */
export function buildSeriesFields(entries: SeriesEntry[] | undefined | null): SeriesFields {
  const seriesMemberships: AudiobookSeriesMembership[] = (entries ?? [])
    .filter((entry) => (entry?.name ?? '').trim().length > 0)
    .map((entry, index) => {
      const asin = entry.asin?.trim()
      return {
        seriesName: (entry.name ?? '').trim(),
        seriesNumber: entry.position?.trim() || undefined,
        // The search fallback branch fills `asin` with the series *name* when the ASIN
        // re-fetch fails, so only keep a value that actually looks like an ASIN.
        seriesAsin: looksLikeAsin(asin) ? asin : undefined,
        isPrimary: index === 0,
        sortOrder: index,
      }
    })

  const primary = seriesMemberships[0]
  return primary
    ? {
        seriesMemberships,
        series: primary.seriesName,
        seriesNumber: primary.seriesNumber,
        seriesAsin: primary.seriesAsin,
      }
    : {}
}

// Build a lexicographically-comparable key from a series position number so a plain string
// sort (localeCompare) yields reading order. Each tier is led by a digit so the tiers sort
// deterministically across locales (a leading symbol like "~" does NOT reliably sort after
// digits — that was the original bug for missing positions):
//   tier 1 = fully-numeric positions ("1", "2.5", "10"), ordered numerically via zero-padding;
//   tier 2 = other non-empty positions ("1-2", "1a"), ordered by their text, after the numbers;
//   tier 3 = missing positions, always sorted last.
export function seriesPositionSortKey(value: string | null | undefined): string {
  const raw = (value || '').trim()
  if (!raw) return '3'
  if (/^\d+(\.\d+)?$/.test(raw)) {
    const [intPart, fracPart = ''] = raw.split('.')
    return `1${intPart.padStart(8, '0')}${fracPart ? `.${fracPart}` : ''}`
  }
  return `2${raw.toLowerCase()}`
}

/**
 * Inline style for one cover in an overlapping series cover mosaic. The covers fan out
 * across the left half of a 2:1 card, each one stacked under the cover before it.
 */
export function seriesCoverMosaicStyle(index: number, count: number) {
  const left = count <= 1 ? 25 : (index * 50) / Math.max(1, count - 1)
  const zIndex = count <= 1 ? 1 : Math.max(1, 100 - index)

  return {
    width: '50%',
    height: '100%',
    top: '0%',
    left: `${left}%`,
    zIndex,
    boxShadow: 'rgba(17, 17, 17, 0.4) 4px 0px 10px',
    borderRadius: '12px',
  }
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
