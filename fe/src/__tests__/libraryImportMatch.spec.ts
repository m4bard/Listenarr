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
  return { asin: 'B0TEST', title, authors: author ? [{ name: author }] : [] } as SearchResult
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
  ])('treats %s and %s as the same title', (a, b) => {
    expect(titleSimilarity(a, b)).toBe(1)
  })

  it('scores titles sharing a word below the floor', () => {
    expect(titleSimilarity('Little Men', 'A Little Princess')).toBeLessThan(TITLE_SIMILARITY_FLOOR)
    expect(titleSimilarity('Tarzan of the Apes', 'The Return of Tarzan')).toBeLessThan(
      TITLE_SIMILARITY_FLOOR,
    )
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
    const other = result('Dagon', 'Someone Else')
    const right = result('Dagon', 'H. P. Lovecraft')
    expect(pickBestMatch([other, right], 'Dagon', 'H. P. Lovecraft')).toBe(right)
  })

  it('keeps the matching author when a later candidate has an equal title', () => {
    const right = result('Dagon', 'H. P. Lovecraft')
    const other = result('Dagon', 'Someone Else')
    expect(pickBestMatch([right, other], 'Dagon', 'H. P. Lovecraft')).toBe(right)
  })

  it('prefers the first of equal candidates', () => {
    const first = result('Dagon', 'H. P. Lovecraft')
    const second = result('Dagon', 'H.P. Lovecraft')
    expect(pickBestMatch([first, second], 'Dagon', 'H. P. Lovecraft')).toBe(first)
  })
})
