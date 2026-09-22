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
import type { AudiobookSeriesMembership } from '@/types'
import { seriesPositionSortKey } from '@/utils/seriesUtils'
import { normalizeCollectionText } from '@/utils/textUtils'

/**
 * The part of a collection row the series grouping needs. Library rows carry series
 * memberships; rows that only exist in the remote catalog carry a single series name and,
 * sometimes, a position.
 */
export interface AuthorSeriesBook {
  key: string
  title: string
  inLibrary: boolean
  monitored?: boolean
  imageUrl?: string
  series?: string
  seriesNumber?: string
  seriesMemberships?: AudiobookSeriesMembership[]
}

export interface AuthorSeriesMember {
  book: AuthorSeriesBook
  position: string
}

export interface AuthorSeriesGroup {
  key: string
  name: string
  asin: string
  members: AuthorSeriesMember[]
  libraryCount: number
  totalCount: number
}

interface SeriesReference {
  name: string
  asin: string
  position: string
}

interface GroupAccumulator {
  key: string
  name: string
  asin: string
  members: AuthorSeriesMember[]
  seenBookKeys: Set<string>
}

function seriesReferencesFor(book: AuthorSeriesBook): SeriesReference[] {
  const memberships = book.seriesMemberships ?? []
  const fromMemberships = memberships
    .filter((membership) => (membership.seriesName || '').trim().length > 0)
    .map((membership) => toReference(membership))
  if (fromMemberships.length > 0) return fromMemberships

  const legacyName = (book.series || '').trim()
  if (!legacyName) return []
  return [{ name: legacyName, asin: '', position: (book.seriesNumber || '').trim() }]
}

function toReference(membership: AudiobookSeriesMembership): SeriesReference {
  return {
    name: membership.seriesName.trim(),
    asin: (membership.seriesAsin || '').trim().toUpperCase(),
    position: (membership.seriesNumber || '').trim(),
  }
}

function asinsByNormalizedName(books: readonly AuthorSeriesBook[]): Map<string, string> {
  const asins = new Map<string, string>()
  for (const book of books) {
    for (const reference of seriesReferencesFor(book)) {
      const normalizedName = normalizeCollectionText(reference.name)
      if (!normalizedName || !reference.asin || asins.has(normalizedName)) continue
      asins.set(normalizedName, reference.asin)
    }
  }
  return asins
}

/**
 * Groups the books of an author collection into the series they belong to.
 *
 * Identity is the series ASIN when one is known, and the normalized series name otherwise, so
 * that "Ledgerwood Cycle" and "the ledgerwood cycle" are one series. A catalog row carries a
 * name but no ASIN, so a name that some library row does give an ASIN for is resolved to that
 * ASIN first; without this a series would split into an owned half and a not-added half.
 */
export function groupAuthorSeries(books: readonly AuthorSeriesBook[]): AuthorSeriesGroup[] {
  const knownAsins = asinsByNormalizedName(books)
  const groups = new Map<string, GroupAccumulator>()

  for (const book of books) {
    for (const reference of seriesReferencesFor(book)) {
      const normalizedName = normalizeCollectionText(reference.name)
      if (!normalizedName) continue

      const asin = reference.asin || knownAsins.get(normalizedName) || ''
      const key = asin ? `asin:${asin}` : `name:${normalizedName}`

      let group = groups.get(key)
      if (!group) {
        group = { key, name: reference.name, asin, members: [], seenBookKeys: new Set<string>() }
        groups.set(key, group)
      }

      if (group.seenBookKeys.has(book.key)) continue
      group.seenBookKeys.add(book.key)
      group.members.push({ book, position: reference.position })
    }
  }

  return Array.from(groups.values())
    .map((group) => finalizeGroup(group))
    .sort((a, b) => a.name.localeCompare(b.name))
}

function finalizeGroup(group: GroupAccumulator): AuthorSeriesGroup {
  const members = [...group.members].sort(compareMembers)
  return {
    key: group.key,
    name: group.name,
    asin: group.asin,
    members,
    libraryCount: members.filter((member) => member.book.inLibrary).length,
    totalCount: members.length,
  }
}

function compareMembers(a: AuthorSeriesMember, b: AuthorSeriesMember): number {
  const byPosition = seriesPositionSortKey(a.position).localeCompare(
    seriesPositionSortKey(b.position),
  )
  if (byPosition !== 0) return byPosition
  return a.book.title.localeCompare(b.book.title)
}

/**
 * A group key turned into something usable as an HTML id. Keys are either "asin:<asin>" or
 * "name:<normalized name>", and a normalized name contains spaces, which are not valid in an
 * id fragment and break the aria-controls reference. Normalization has already collapsed every
 * run of punctuation to a single space, so two different keys cannot slugify to the same id.
 */
export function authorSeriesElementId(prefix: string, groupKey: string): string {
  const slug = groupKey
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
  return `${prefix}-${slug || 'series'}`
}
