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
