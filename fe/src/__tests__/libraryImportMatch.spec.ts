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
import { describe, expect, it } from 'vitest'
import { TITLE_SIMILARITY_FLOOR, pickBestMatch, titleSimilarity } from '@/utils/libraryImportMatch'
import type { SearchResult } from '@/types'

function result(title: string, author?: string): SearchResult {
  return { asin: 'B00LW3J8RA', title, authors: author ? [{ name: author }] : [] } as SearchResult
}

describe('titleSimilarity', () => {
  it.each([
    ['The Time Machine', 'time machine'],
    ['Jane Eyre', 'JANE EYRE!'],
    ['07 - Jane Eyre', 'Jane Eyre'],
    ['Jane Eyre (Unabridged)', 'Jane Eyre'],
    ['Arsene Lupin', 'Arsène Lupin'],
    ['The Marvelous Land of Oz', 'The Marvelous Land of Oz: Oz, Book 2'],
    ['The Marvelous Land of Oz: Oz, Book 2', 'The Marvelous Land of Oz'],
    ['The Marvelous Land of Oz; Oz, Book 2', 'The Marvelous Land of Oz'],
    ["Uncle Tom's Cabin", 'Uncle Toms Cabin'],
    ['Uncle Tom\u2019s Cabin', "Uncle Tom's Cabin"],
  ])('treats %s and %s as the same title', (a, b) => {
    expect(titleSimilarity(a, b)).toBe(1)
  })

  it('scores titles sharing a word below the floor', () => {
    expect(titleSimilarity('Little Men', 'A Little Princess')).toBeLessThan(TITLE_SIMILARITY_FLOOR)
    expect(titleSimilarity('Tarzan of the Apes', 'The Return of Tarzan')).toBeLessThan(
      TITLE_SIMILARITY_FLOOR,
    )
  })

  it('scores two titles with different subtitles under one series prefix below the floor', () => {
    expect(
      titleSimilarity(
        'Sherlock Holmes: The Hound of the Baskervilles',
        'Sherlock Holmes: A Study in Scarlet',
      ),
    ).toBeLessThan(TITLE_SIMILARITY_FLOOR)
    expect(
      titleSimilarity('Barsoom; The Gods of Mars', 'Barsoom; The Warlord of Mars'),
    ).toBeLessThan(TITLE_SIMILARITY_FLOOR)
  })

  it.each([
    ['Around the World in Eighty Days Part 1', 'Around the World in Eighty Days Part 2'],
    ['Around the World in Eighty Days Book One', 'Around the World in Eighty Days Book Two'],
    ['Around the World in Eighty Days Volume I', 'Around the World in Eighty Days Volume II'],
    ['Around the World in Eighty Days Vol. 3', 'Around the World in Eighty Days Volume 4'],
    ['Around the World in Eighty Days (Part 1)', 'Around the World in Eighty Days (Part 2)'],
    ['Around the World in Eighty Days [Part 1]', 'Around the World in Eighty Days [Part 2]'],
    ['Around the World in Eighty Days (Book One)', 'Around the World in Eighty Days [Book Two]'],
  ])('scores %s and %s as different volumes of a long title', (a, b) => {
    expect(titleSimilarity(a, b)).toBe(0)
  })

  it.each([
    ['Around the World in Eighty Days Part 1', 'Around the World in Eighty Days Part 1'],
    ['Around the World in Eighty Days Vol. 1', 'Around the World in Eighty Days Volume One'],
    ['Around the World in Eighty Days Part I', 'Around the World in Eighty Days Part 1'],
    ['Around the World in Eighty Days', 'Around the World in Eighty Days Part 1'],
  ])('keeps %s and %s as the same volume', (a, b) => {
    expect(titleSimilarity(a, b)).toBeGreaterThanOrEqual(TITLE_SIMILARITY_FLOOR)
  })

  it.each([
    ['Dracula - Unabridged', 'Dracula'],
    ['Dracula [Unabridged]', 'Dracula'],
    [
      'Daniel Defoe Box Set: A Journal of the Plague Year & The Apparition of Mrs. Veal',
      'Daniel Defoe Box Set: A Journal of the Plague Year and The Apparition of Mrs. Veal',
    ],
  ])('treats %s and %s as the same title', (a, b) => {
    expect(titleSimilarity(a, b)).toBe(1)
  })

  it('scores titles with nothing in common as 0', () => {
    expect(titleSimilarity('Dagon', 'Moby Dick')).toBe(0)
  })
})

describe('pickBestMatch', () => {
  it('returns null for no results', () => {
    expect(pickBestMatch([], 'Dagon', 'H. P. Lovecraft')).toBeNull()
  })

  it('takes a matching title whose author has no name', () => {
    const match = result('Dagon')
    expect(pickBestMatch([match], 'Dagon', 'H. P. Lovecraft')).toBe(match)
  })

  it('takes a matching title when the scan found no author', () => {
    const match = result('Dagon', 'H. P. Lovecraft')
    expect(pickBestMatch([match], 'Dagon')).toBe(match)
  })

  it('prefers the matching author among equal titles', () => {
    const other = result('Dagon', 'Charlotte Bront\u00eb')
    const right = result('Dagon', 'H. P. Lovecraft')
    expect(pickBestMatch([other, right], 'Dagon', 'H. P. Lovecraft')).toBe(right)
  })

  it('keeps the matching author when a later candidate has an equal title', () => {
    const right = result('Dagon', 'H. P. Lovecraft')
    const other = result('Dagon', 'Charlotte Bront\u00eb')
    expect(pickBestMatch([right, other], 'Dagon', 'H. P. Lovecraft')).toBe(right)
  })

  it('prefers the first of equal candidates', () => {
    const first = result('Dagon', 'H. P. Lovecraft')
    const second = result('Dagon', 'H.P. Lovecraft')
    expect(pickBestMatch([first, second], 'Dagon', 'H. P. Lovecraft')).toBe(first)
  })

  it('rejects a different volume of a long title and accepts the same one', () => {
    const volumeOne = result('Around the World in Eighty Days Part 1')
    const volumeTwo = result('Around the World in Eighty Days Part 2')
    expect(pickBestMatch([volumeTwo], 'Around the World in Eighty Days Part 1')).toBeNull()
    expect(pickBestMatch([volumeTwo, volumeOne], 'Around the World in Eighty Days Part 1')).toBe(
      volumeOne,
    )
  })

  it.each([
    ['exact first', 0],
    ['exact last', 1],
  ])('prefers the exact title over a subtitled one (%s)', (_name, exactAt) => {
    const subtitled = [result('Sherlock Holmes: A Study in Scarlet', 'Arthur Conan Doyle')]
    const exact = result('Sherlock Holmes', 'H. Rider Haggard')
    const results = [...subtitled]
    results.splice(exactAt, 0, exact)
    expect(pickBestMatch(results, 'Sherlock Holmes', 'Arthur Conan Doyle')).toBe(exact)
  })

  it.each(['1984', 'Catch-22', 'Fahrenheit 451', '2001: A Space Odyssey'])(
    'selects a number-bearing title that matches itself (%s)',
    (title) => {
      const other = result('A Little Princess')
      const same = result(title)
      expect(titleSimilarity(title, title)).toBe(1)
      expect(pickBestMatch([other, same], title)).toBe(same)
    },
  )

  it('accepts a bare series candidate for a detected title with a subtitle (deliberate trade-off)', () => {
    const series = result('Dune')
    expect(pickBestMatch([series], 'Dune: Messiah')).toBe(series)
  })

  describe('known limits, currently accepted and not claimed correct', () => {
    it.each([
      [
        'Around the World in Eighty Days Book 1 Part 2',
        'Around the World in Eighty Days Book 1 Part 3',
      ],
      ['Around the World in Eighty Days Book1', 'Around the World in Eighty Days Book2'],
      ['Around the World in Eighty Days Chapter 1', 'Around the World in Eighty Days Chapter 2'],
      ['Around the World in Eighty Days Volumes 1', 'Around the World in Eighty Days Volumes 2'],
      ['Around the World in Eighty Days Pt. 1', 'Around the World in Eighty Days Pt. 2'],
      ['Around the World in Eighty Days Volume xxi', 'Around the World in Eighty Days Volume xxii'],
      [
        'Around the World in Eighty Days Book twenty one',
        'Around the World in Eighty Days Book twenty two',
      ],
      ['Around the World in Eighty Days 1', 'Around the World in Eighty Days 2'],
      ['Around the World in Eighty Days Part 1', 'Around the World in Eighty Days'],
    ])('KnownLimit: %s vs %s is not told apart', (a, b) => {
      expect(titleSimilarity(a, b)).toBeGreaterThanOrEqual(TITLE_SIMILARITY_FLOOR)
    })
  })
})
