import { describe, expect, it } from 'vitest'
import { sortLibraryImportItems } from '@/utils/libraryImportTable'

describe('sortLibraryImportItems', () => {
  const items = [
    {
      id: '3',
      folderName: 'Gamma',
      fullPath: '/books/Gamma/book.m4b',
      format: 'M4B',
      fileCount: 1,
      selectedMatch: null,
    },
    {
      id: '1',
      folderName: 'Alpha',
      fullPath: '/books/Alpha/book-2.mp3',
      format: 'MP3',
      fileCount: 2,
      selectedMatch: {
        title: 'Zeta Match',
        authors: [{ name: 'Author Z' }],
      },
    },
    {
      id: '2',
      folderName: 'Beta',
      fullPath: '/books/Beta/book-1.m4b',
      format: 'M4B',
      fileCount: 1,
      selectedMatch: {
        title: 'Alpha Match',
        authors: [{ name: 'Author A' }],
      },
    },
  ]

  it('sorts by folder ascending', () => {
    const sorted = sortLibraryImportItems(items, 'folder', 'asc')
    expect(sorted.map((item) => item.id)).toEqual(['1', '2', '3'])
  })

  it('sorts by format descending and uses file count as a tiebreaker', () => {
    const sorted = sortLibraryImportItems(items, 'format', 'desc')
    expect(sorted.map((item) => item.id)).toEqual(['1', '2', '3'])
  })

  it('sorts unmatched items after matched items when sorting by match', () => {
    const sorted = sortLibraryImportItems(items, 'match', 'asc')
    expect(sorted.map((item) => item.id)).toEqual(['2', '1', '3'])
  })
})
