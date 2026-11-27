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
// Start Matching's automatic pick: a search result is only taken as the match when its
// title (and author, when both sides have one) resembles what the scan detected. The search answers a
// pool of loose candidates; when none of them is the book, the row must stay unmatched for the user
// to pick, instead of taking the first candidate.
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { SearchResult } from '@/types'

const advancedSearch = vi.fn()
const getApplicationSettings = vi.fn()

vi.mock('@/services/api', () => ({
  apiService: {
    advancedSearch,
    getApplicationSettings,
    getSavedUnmatchedFiles: vi.fn(),
  },
}))

vi.mock('@/services/signalr', () => ({
  signalRService: { onUnmatchedScanComplete: vi.fn(() => () => {}) },
}))

vi.mock('@/utils/logger', () => ({ logger: { debug: vi.fn() } }))

// A scanned row as an `Author/Series/NN - Title` folder with correct embedded tags gives it
function scannedBook(folder: string, title: string, author: string) {
  const fullPath = `/audiobooks/${author}/${folder}/01.mp3`
  return {
    id: fullPath,
    fullPath,
    sourceFiles: [fullPath],
    folderPath: `/audiobooks/${author}/${folder}`,
    relativePath: `${author}/${folder}`,
    folderName: folder,
    detectedTitle: title,
    detectedAuthor: author,
    format: 'MP3',
    fileCount: 1,
    selectedMatch: null,
    hasSearched: false,
    isSearching: false,
    selected: false,
  }
}

function result(asin: string, title: string, author?: string): SearchResult {
  return {
    asin,
    title,
    // Title search results often carry authors without names (only the full record has them)
    authors: author ? [{ name: author }] : [{ asin: 'B000AUTHOR' }],
  } as SearchResult
}

async function matchOne(book: ReturnType<typeof scannedBook>, candidates: SearchResult[]) {
  const { useLibraryImportStore } = await import('@/stores/libraryImport')
  const store = useLibraryImportStore()
  advancedSearch.mockResolvedValue(candidates)
  store.items = { [book.id]: book }

  store.startProcessing()
  await vi.waitFor(() => expect(store.isProcessing).toBe(false))

  const row = store.items[book.id]!
  expect(row.hasSearched).toBe(true)
  return row
}

describe('library import automatic matching (issue #1237)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    setActivePinia(createPinia())
    getApplicationSettings.mockResolvedValue({ defaultSearchRegion: 'us' })
  })

  it('leaves a book unmatched when no candidate shares its title or author', async () => {
    const row = await matchOne(
      scannedBook('01 - The Three Musketeers', 'The Three Musketeers', 'Alexandre Dumas'),
      [result('B071S17YLK', 'Pride and Prejudice', 'Jane Austen')],
    )

    expect(row.selectedMatch).toBeNull()
    expect(row.selected).toBe(false)
  })

  it('leaves a book unmatched when the only candidate has an unrelated title and no author name', async () => {
    const row = await matchOne(scannedBook('The Time Machine', 'The Time Machine', 'H. G. Wells'), [
      result('B00LW3J8RA', 'Dagon'),
    ])

    expect(row.selectedMatch).toBeNull()
    expect(row.selected).toBe(false)
  })

  it('does not fall back to the first candidate when none is by the detected author', async () => {
    const row = await matchOne(scannedBook('Jane Eyre', 'Jane Eyre', 'Charlotte Bront\u00eb'), [
      result('B0036N9OKA', 'Moby Dick', 'Herman Melville'),
      result('B002V5CW08', 'Robinson Crusoe', 'Daniel Defoe'),
    ])

    expect(row.selectedMatch).toBeNull()
  })

  it('rejects a candidate that only shares one word of the title', async () => {
    const row = await matchOne(scannedBook('Little Men', 'Little Men', 'Louisa May Alcott'), [
      result('B002UZMQCI', 'A Little Princess', 'Frances Hodgson Burnett'),
    ])

    expect(row.selectedMatch).toBeNull()
  })

  it('prefers the candidate with the detected title over another book by the same author', async () => {
    const row = await matchOne(
      scannedBook('01 - Tarzan of the Apes', 'Tarzan of the Apes', 'Edgar Rice Burroughs'),
      [
        result('B01GIO3GFW', 'The Return of Tarzan', 'Edgar Rice Burroughs'),
        result('B002V5B7TK', 'Tarzan of the Apes', 'Edgar Rice Burroughs'),
      ],
    )

    expect(row.selectedMatch?.asin).toBe('B002V5B7TK')
  })

  it('does not take another book of the same series by the same author', async () => {
    const row = await matchOne(
      scannedBook('01 - Tarzan of the Apes', 'Tarzan of the Apes', 'Edgar Rice Burroughs'),
      [result('B01GIO3GFW', 'The Return of Tarzan', 'Edgar Rice Burroughs')],
    )

    expect(row.selectedMatch).toBeNull()
  })

  it('matches on the folder name when the scan detected no title', async () => {
    const book = {
      ...scannedBook('03 - The Gods of Mars', 'unused', 'Edgar Rice Burroughs'),
      detectedTitle: undefined,
    }
    book.fullPath = `${book.folderPath}/03 - The Gods of Mars.m4b`
    const row = await matchOne(book, [
      result('B01BKS3DPE', 'The Gods of Mars', 'Edgar Rice Burroughs'),
    ])

    expect(row.selectedMatch?.asin).toBe('B01BKS3DPE')
  })

  // Guards for the fix: a close title is still matched automatically
  it('still matches a candidate whose title adds a series suffix to the detected one', async () => {
    const row = await matchOne(
      scannedBook('03 - The Gods of Mars', 'The Gods of Mars', 'Edgar Rice Burroughs'),
      [result('B01BKS3DPE', 'The Gods of Mars: Barsoom Series, Book 2', 'Edgar Rice Burroughs')],
    )

    expect(row.selectedMatch?.asin).toBe('B01BKS3DPE')
    expect(row.selected).toBe(true)
  })

  it('still matches the same title when the author is written differently', async () => {
    const row = await matchOne(scannedBook('Peter and Wendy', 'Peter and Wendy', 'J. M. Barrie'), [
      result('B084J9S79P', 'Peter and Wendy', 'J.M. Barrie'),
    ])

    expect(row.selectedMatch?.asin).toBe('B084J9S79P')
  })
})
