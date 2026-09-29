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
import {
  normalizeAuthorKey,
  normalizeCollectionText,
  normalizeIdentifier,
} from '@/utils/collectionText'

/** One series a book belongs to, resolved to the identity the library groups it under. */
export interface SeriesRef {
  /** Stable grouping key: `asin:<ASIN>` for an identified series, `name:<name key>` otherwise. */
  key: string
  /** The membership's trimmed, as-stored display name. */
  name: string
  /** The series identifier the key is built on, when there is one. */
  asin?: string
  seriesNumber?: string
}

interface SeriesEntry {
  name: string
  nameKey: string
  asin: string
  seriesNumber?: string
}

/**
 * The comparison key for a series display name. normalizeCollectionText folds case, accents,
 * punctuation and spacing, but it keeps only [a-z0-9], so a Cyrillic, Greek, CJK or Arabic name
 * comes out empty or as its digits alone ("Метро 2033" would equal "2033"). When the folded form
 * has no letter left, the name is compared as written instead: NFC, lowercased, spacing collapsed.
 */
export function seriesNameKey(name: string | undefined | null): string {
  const trimmed = (name || '').trim()
  if (!trimmed) return ''
  const folded = normalizeCollectionText(trimmed)
  if (/[a-z]/.test(folded)) return folded
  return trimmed.normalize('NFC').toLowerCase().replace(/\s+/g, ' ')
}

// A book's series as stored: every membership with a name, else the legacy single-series column.
function seriesEntries(book: Audiobook): SeriesEntry[] {
  const entries: SeriesEntry[] = []
  for (const membership of book.seriesMemberships || []) {
    const name = (membership.seriesName || '').trim()
    if (!name) continue
    entries.push({
      name,
      nameKey: seriesNameKey(name),
      asin: normalizeIdentifier(membership.seriesAsin),
      seriesNumber: membership.seriesNumber,
    })
  }
  if (entries.length > 0) return entries

  const legacy = (book.series || '').trim()
  return legacy
    ? [{ name: legacy, nameKey: seriesNameKey(legacy), asin: '', seriesNumber: book.seriesNumber }]
    : []
}

export function seriesKeyForAsin(asin: string | undefined | null): string {
  return `asin:${normalizeIdentifier(asin)}`
}

export function seriesKeyForName(name: string | undefined | null): string {
  return `name:${seriesNameKey(name)}`
}

/**
 * Resolves the series a book belongs to into series identities, consistently across one library.
 *
 * A series is identified by its SeriesAsin wherever a membership carries one, so two different
 * series that share a display name (an original and a translated edition, say) stay apart, and
 * spelling variants of one identified series come together. A membership with no identifier is
 * keyed by its name, except that it is filed under an identified series of the same name when
 * exactly one such series has a book by the same authors: rows written before identifiers were
 * stored would otherwise split every series in two the moment one of its rows gained one. The
 * author check is what keeps a shared name alone from being taken as evidence (another author's
 * "Foundation" is not this one). With no such series, or more than one, the membership keeps its
 * own name key rather than being guessed into any of them.
 */
export function createSeriesIdentityResolver(books: readonly Audiobook[]) {
  // name key -> identifier -> author-set keys of the books carrying that identifier under that name
  const identifiedByName = new Map<string, Map<string, Set<string>>>()
  for (const book of books) {
    const authorKey = normalizeAuthorKey(book.authors)
    for (const entry of seriesEntries(book)) {
      if (!entry.asin) continue
      let byAsin = identifiedByName.get(entry.nameKey)
      if (!byAsin) {
        byAsin = new Map<string, Set<string>>()
        identifiedByName.set(entry.nameKey, byAsin)
      }
      let authors = byAsin.get(entry.asin)
      if (!authors) {
        authors = new Set<string>()
        byAsin.set(entry.asin, authors)
      }
      if (authorKey) authors.add(authorKey)
    }
  }

  function resolveEntryAsin(entry: SeriesEntry, authorKey: string): string {
    if (entry.asin) return entry.asin
    if (!authorKey) return ''
    const byAsin = identifiedByName.get(entry.nameKey)
    if (!byAsin) return ''
    const sameAuthors = [...byAsin].filter(([, authors]) => authors.has(authorKey))
    return sameAuthors.length === 1 ? sameAuthors[0]![0] : ''
  }

  /** Every distinct series the book belongs to, first-seen membership winning per identity. */
  function seriesForBook(book: Audiobook): SeriesRef[] {
    const authorKey = normalizeAuthorKey(book.authors)
    const refs: SeriesRef[] = []
    const seen = new Set<string>()
    for (const entry of seriesEntries(book)) {
      const asin = resolveEntryAsin(entry, authorKey)
      const key = asin ? seriesKeyForAsin(asin) : `name:${entry.nameKey}`
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
   * name, whatever its authors. One entry means the name is unambiguous in this library.
   */
  function asinsForName(name: string | undefined | null): string[] {
    return [...(identifiedByName.get(seriesNameKey(name))?.keys() ?? [])]
  }

  return { seriesForBook, asinsForName }
}
