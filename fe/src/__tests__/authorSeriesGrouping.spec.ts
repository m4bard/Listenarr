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
import {
  authorSeriesElementId,
  groupAuthorSeries,
  type AuthorSeriesBook,
} from '@/components/domain/collection/authorSeriesGrouping'

function libraryBook(
  key: string,
  title: string,
  memberships: AuthorSeriesBook['seriesMemberships'],
  monitored = true,
): AuthorSeriesBook {
  return { key, title, inLibrary: true, monitored, seriesMemberships: memberships }
}

function catalogBook(
  key: string,
  title: string,
  series: string,
  seriesNumber?: string,
): AuthorSeriesBook {
  return { key, title, inLibrary: false, series, seriesNumber }
}

describe('groupAuthorSeries', () => {
  it('keys on the series ASIN even when the names differ', () => {
    const groups = groupAuthorSeries([
      libraryBook('a', 'Book One', [
        { seriesName: 'Ledgerwood Cycle', seriesAsin: 'B0SERIES01', seriesNumber: '1' },
      ]),
      libraryBook('b', 'Book Two', [
        {
          seriesName: 'The Ledgerwood Cycle (Boxed)',
          seriesAsin: 'B0SERIES01',
          seriesNumber: '2',
        },
      ]),
    ])

    expect(groups).toHaveLength(1)
    expect(groups[0]!.key).toBe('asin:B0SERIES01')
    expect(groups[0]!.name).toBe('Ledgerwood Cycle')
    expect(groups[0]!.members.map((member) => member.book.key)).toEqual(['a', 'b'])
  })

  it('falls back to a normalized name and merges case and whitespace variants', () => {
    const groups = groupAuthorSeries([
      catalogBook('a', 'Book One', 'Marrow Vault', '1'),
      catalogBook('b', 'Book Two', '  the MARROW   vault ', '2'),
      catalogBook('c', 'Book Three', 'The Marrow Vault', '3'),
    ])

    expect(groups.map((group) => group.key)).toEqual(['name:marrow vault', 'name:the marrow vault'])
    expect(groups[1]!.name).toBe('the MARROW   vault')
    expect(groups[1]!.members.map((member) => member.book.key)).toEqual(['b', 'c'])
  })

  it('resolves a catalog row without an ASIN into the ASIN group of the same series name', () => {
    const groups = groupAuthorSeries([
      libraryBook('a', 'Book One', [
        { seriesName: 'Marrow Vault', seriesAsin: 'B0SERIES02', seriesNumber: '1' },
      ]),
      catalogBook('b', 'Book Two', 'marrow vault', '2'),
    ])

    expect(groups).toHaveLength(1)
    expect(groups[0]!.key).toBe('asin:B0SERIES02')
    expect(groups[0]!.libraryCount).toBe(1)
    expect(groups[0]!.totalCount).toBe(2)
  })

  it('lists a book that belongs to several series under each of them', () => {
    const groups = groupAuthorSeries([
      libraryBook('a', 'Shared Book', [
        { seriesName: 'Publication Order', seriesAsin: 'B0SERIES03', seriesNumber: '1' },
        { seriesName: 'Chronological Order', seriesAsin: 'B0SERIES04', seriesNumber: '4' },
      ]),
    ])

    expect(groups.map((group) => group.name)).toEqual(['Chronological Order', 'Publication Order'])
    expect(groups[0]!.members[0]!.position).toBe('4')
    expect(groups[1]!.members[0]!.position).toBe('1')
  })

  it('orders books by series position and sorts non-numeric positions after numeric ones', () => {
    const groups = groupAuthorSeries([
      catalogBook('missing', 'No Position', 'Marrow Vault'),
      catalogBook('ten', 'Tenth', 'Marrow Vault', '10'),
      catalogBook('novella', 'Novella', 'Marrow Vault', '1.5'),
      catalogBook('text', 'Companion', 'Marrow Vault', '2a'),
      catalogBook('two', 'Second', 'Marrow Vault', '2'),
      catalogBook('one', 'First', 'Marrow Vault', '1'),
    ])

    expect(groups[0]!.members.map((member) => member.book.key)).toEqual([
      'one',
      'novella',
      'two',
      'ten',
      'text',
      'missing',
    ])
  })

  it('counts library rows against the total for each series', () => {
    const groups = groupAuthorSeries([
      libraryBook('a', 'Owned One', [{ seriesName: 'Marrow Vault', seriesNumber: '1' }]),
      libraryBook('b', 'Owned Two', [{ seriesName: 'Marrow Vault', seriesNumber: '2' }]),
      catalogBook('c', 'Wanted', 'Marrow Vault', '3'),
    ])

    expect(groups[0]!.libraryCount).toBe(2)
    expect(groups[0]!.totalCount).toBe(3)
    expect(groups[0]!.asin).toBe('')
  })

  it('ignores books with no series information at all', () => {
    expect(groupAuthorSeries([{ key: 'a', title: 'Standalone', inLibrary: true }])).toEqual([])
    expect(
      groupAuthorSeries([{ key: 'b', title: 'Blank', inLibrary: false, series: '   ' }]),
    ).toEqual([])
  })
})

describe('authorSeriesElementId', () => {
  const validHtmlId = /^[A-Za-z][A-Za-z0-9-]*$/

  it('turns a name key with spaces into a valid id', () => {
    const id = authorSeriesElementId('author-series-books', 'name:the marrow vault')
    expect(id).toBe('author-series-books-name-the-marrow-vault')
    expect(id).toMatch(validHtmlId)
  })

  it('turns an ASIN key into a valid id', () => {
    const id = authorSeriesElementId('author-series-books', 'asin:B0SERIES01')
    expect(id).toBe('author-series-books-asin-b0series01')
    expect(id).toMatch(validHtmlId)
  })

  it('produces a valid id for every key the grouping can emit', () => {
    const groups = groupAuthorSeries([
      libraryBook('a', 'Owned', [
        { seriesName: 'Marrow Vault: Year One', seriesAsin: 'B0SERIES05', seriesNumber: '1' },
      ]),
      catalogBook('b', 'Wanted', "Tidewater's Papers (Part 2)", '2'),
    ])

    for (const group of groups) {
      expect(authorSeriesElementId('author-series-books', group.key)).toMatch(validHtmlId)
    }
  })

  it('keeps distinct keys distinct and never returns a bare prefix', () => {
    expect(authorSeriesElementId('panel', 'name:a b')).not.toBe(
      authorSeriesElementId('panel', 'name:ab'),
    )
    expect(authorSeriesElementId('panel', '')).toBe('panel-series')
  })
})
