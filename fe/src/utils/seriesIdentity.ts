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
import { normalizeCollectionText, normalizeIdentifier } from '@/utils/collectionText'

/** One series a book belongs to, resolved to the identity the library groups it under. */
export interface SeriesRef {
  /** Stable grouping key: `asin:<ASIN>` for an identified series, `name:<normalized>` otherwise. */
  key: string
  /** The membership's trimmed, as-stored display name. */
  name: string
  /** The series identifier the key is built on, when there is one. */
  asin?: string
  seriesNumber?: string
}

interface SeriesEntry {
  name: string
  normalizedName: string
  asin: string
  seriesNumber?: string
}

// A book's series as stored: every membership with a name, else the legacy single-series column.
function seriesEntries(book: Audiobook): SeriesEntry[] {
  const entries: SeriesEntry[] = []
  for (const membership of book.seriesMemberships || []) {
    const name = (membership.seriesName || '').trim()
    const normalizedName = normalizeCollectionText(name)
    if (!normalizedName) continue
    entries.push({
      name,
      normalizedName,
      asin: normalizeIdentifier(membership.seriesAsin),
      seriesNumber: membership.seriesNumber,
    })
  }
  if (entries.length > 0) return entries

  const legacy = (book.series || '').trim()
  const normalizedLegacy = normalizeCollectionText(legacy)
  return normalizedLegacy
    ? [
        {
          name: legacy,
          normalizedName: normalizedLegacy,
          asin: '',
          seriesNumber: book.seriesNumber,
        },
      ]
    : []
}

export function seriesKeyForAsin(asin: string | undefined | null): string {
  return `asin:${normalizeIdentifier(asin)}`
}

export function seriesKeyForName(name: string | undefined | null): string {
  return `name:${normalizeCollectionText(name)}`
}

/**
 * Resolves the series a book belongs to into series identities, consistently across one library.
 *
 * A series is identified by its SeriesAsin wherever a membership carries one, so two different
 * series that share a display name (an original and a translated edition, say) stay apart, and
 * spelling variants of one identified series come together. A membership with no identifier is
 * keyed by its normalized name, except that when the library holds exactly one identified series
 * of that normalized name it is filed under that series: rows written before identifiers were
 * stored would otherwise split every series in two the moment one of its rows gained one. When
 * the name matches two or more identified series there is no way to tell which is meant, so the
 * membership keeps its own name key rather than being guessed into either.
 */
export function createSeriesIdentityResolver(books: readonly Audiobook[]) {
  const asinsByName = new Map<string, Set<string>>()
  for (const book of books) {
    for (const entry of seriesEntries(book)) {
      if (!entry.asin) continue
      let asins = asinsByName.get(entry.normalizedName)
      if (!asins) {
        asins = new Set<string>()
        asinsByName.set(entry.normalizedName, asins)
      }
      asins.add(entry.asin)
    }
  }

  function resolveEntryAsin(entry: SeriesEntry): string {
    if (entry.asin) return entry.asin
    const candidates = asinsByName.get(entry.normalizedName)
    return candidates && candidates.size === 1 ? [...candidates][0]! : ''
  }

  /** Every distinct series the book belongs to, first-seen membership winning per identity. */
  function seriesForBook(book: Audiobook): SeriesRef[] {
    const refs: SeriesRef[] = []
    const seen = new Set<string>()
    for (const entry of seriesEntries(book)) {
      const asin = resolveEntryAsin(entry)
      const key = asin ? seriesKeyForAsin(asin) : seriesKeyForName(entry.name)
      if (seen.has(key)) continue
      seen.add(key)
      refs.push({
        key,
        name: entry.name,
        asin: asin || undefined,
        seriesNumber: entry.seriesNumber,
      })
    }
    return refs
  }

  /**
   * The identifiers the library associates with a display name: every identified series of that
   * normalized name. One entry means the name is unambiguous in this library.
   */
  function asinsForName(name: string | undefined | null): string[] {
    return [...(asinsByName.get(normalizeCollectionText(name)) ?? [])]
  }

  return { seriesForBook, asinsForName }
}
