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
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { SearchResult } from '@/types'

const startManualImport = vi.fn()
const addToLibrary = vi.fn()
const removeFromLibrary = vi.fn()
const updateAudiobook = vi.fn()
const getAudiobook = vi.fn()

vi.mock('@/services/api', () => ({
  apiService: {
    addToLibrary,
    removeFromLibrary,
    updateAudiobook,
    getAudiobook,
    startManualImport,
    getAudibleMetadata: vi.fn(),
    getApplicationSettings: vi.fn().mockResolvedValue({}),
    getSavedUnmatchedFiles: vi.fn().mockResolvedValue({ items: [], lastScannedAt: null }),
  },
}))

vi.mock('@/services/signalr', () => ({
  signalRService: { onUnmatchedScanComplete: vi.fn(() => () => {}) },
}))

vi.mock('@/utils/logger', () => ({ logger: { debug: vi.fn() } }))

const blockedBody = JSON.stringify({
  code: 'destination_path_blocked',
  message: 'Destination is already assigned to another audiobook in the library.',
  resolvedDestination: '/audiobooks/Shared',
})

function apiError(status: number, body: string): Error & { status: number; body: string } {
  return Object.assign(new Error(`API error: ${status} ${body}`), { status, body })
}

async function storeWithOneBook(action: 'none' | 'move' | 'copy') {
  const { useLibraryImportStore } = await import('@/stores/libraryImport')
  const store = useLibraryImportStore()
  store.items = {
    '/incoming/Book/Book.m4b': {
      id: '/incoming/Book/Book.m4b',
      fullPath: '/incoming/Book/Book.m4b',
      sourceFiles: ['/incoming/Book/Book.m4b'],
      folderPath: '/incoming/Book',
      relativePath: 'Book',
      folderName: 'Book',
      format: 'M4B',
      fileCount: 1,
      selectedMatch: { title: 'Book', authors: [{ name: 'Author' }] } as unknown as SearchResult,
      hasSearched: true,
      isSearching: false,
      selected: true,
    },
  }
  store.action = action
  return store
}

describe('library import rollback and destination messages', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
    addToLibrary.mockResolvedValue({ audiobook: { id: 42 } })
    removeFromLibrary.mockResolvedValue({ message: 'ok', id: 42 })
    updateAudiobook.mockResolvedValue({})
    getAudiobook.mockResolvedValue({ id: 42, files: [] })
  })

  it('removes the entry it just created when no file could be imported', async () => {
    const store = await storeWithOneBook('move')
    startManualImport.mockResolvedValueOnce({
      importedCount: 0,
      totalCount: 1,
      results: [{ success: false, error: 'Source file not found' }],
    })

    const result = await store.importSelected('/library')

    expect(removeFromLibrary).toHaveBeenCalledWith(42, { deleteFiles: false, deleteFolder: false })
    expect(result.imported).toBe(0)
    expect(result.errors).toEqual(['Book: Source file not found'])
  })

  it('never removes an entry that already existed', async () => {
    const store = await storeWithOneBook('none')
    addToLibrary.mockRejectedValueOnce({ status: 409, body: { audiobook: { id: 77 } } })
    startManualImport.mockResolvedValueOnce({
      importedCount: 0,
      totalCount: 1,
      results: [{ success: false, error: 'Source file not found' }],
    })

    const result = await store.importSelected('')

    expect(removeFromLibrary).not.toHaveBeenCalled()
    expect(result.errors).toHaveLength(1)
  })

  it('keeps the entry when only part of the files were imported', async () => {
    const store = await storeWithOneBook('move')
    startManualImport.mockResolvedValueOnce({
      importedCount: 1,
      totalCount: 2,
      results: [{ success: true }, { success: false, error: 'Disk full' }],
    })

    await store.importSelected('/library')

    expect(removeFromLibrary).not.toHaveBeenCalled()
  })

  it('keeps the entry when the import succeeds', async () => {
    const store = await storeWithOneBook('move')
    startManualImport.mockResolvedValueOnce({
      importedCount: 1,
      totalCount: 1,
      results: [{ success: true }],
    })

    const result = await store.importSelected('/library')

    expect(removeFromLibrary).not.toHaveBeenCalled()
    expect(result.imported).toBe(1)
  })

  it('reports the import error and the leftover entry when the rollback itself fails', async () => {
    const store = await storeWithOneBook('move')
    startManualImport.mockResolvedValueOnce({
      importedCount: 0,
      totalCount: 1,
      results: [{ success: false, error: 'Source file not found' }],
    })
    removeFromLibrary.mockRejectedValueOnce(new Error('boom'))

    const result = await store.importSelected('/library')

    expect(result.errors).toEqual([
      'Book: Source file not found (the new library entry could not be removed)',
    ])
  })

  it('explains that each book needs its own folder when leaving files in place', async () => {
    const store = await storeWithOneBook('none')
    addToLibrary.mockRejectedValueOnce(apiError(400, blockedBody))

    const result = await store.importSelected('')

    expect(startManualImport).not.toHaveBeenCalled()
    expect(result.errors).toEqual([
      'Book: This folder already belongs to another book in the library. Leaving files in place needs one book per folder, so give each book its own folder.',
    ])
  })

  it('shows the API message instead of raw JSON for other blocked destinations', async () => {
    const store = await storeWithOneBook('none')
    addToLibrary.mockRejectedValueOnce(
      apiError(
        400,
        JSON.stringify({
          code: 'destination_path_blocked',
          message: 'A move job is still running for this folder.',
        }),
      ),
    )

    const result = await store.importSelected('')

    expect(result.errors).toEqual(['Book: A move job is still running for this folder.'])
  })

  it.each([400, 404])(
    'removes the new entry when the import request is rejected with %i',
    async (status) => {
      const store = await storeWithOneBook('move')
      startManualImport.mockRejectedValueOnce(apiError(status, '{}'))

      const result = await store.importSelected('/library')

      expect(getAudiobook).not.toHaveBeenCalled()
      expect(removeFromLibrary).toHaveBeenCalledWith(42, {
        deleteFiles: false,
        deleteFolder: false,
      })
      expect(result.errors).toHaveLength(1)
    },
  )

  it('removes the new entry after a 409 when it has no files', async () => {
    const store = await storeWithOneBook('move')
    startManualImport.mockRejectedValueOnce(apiError(409, '{}'))

    const result = await store.importSelected('/library')

    expect(getAudiobook).toHaveBeenCalledWith(42)
    expect(removeFromLibrary).toHaveBeenCalledWith(42, { deleteFiles: false, deleteFolder: false })
    expect(result.errors).toHaveLength(1)
  })

  it('keeps the new entry after a 409 when files were already imported into it', async () => {
    const store = await storeWithOneBook('move')
    startManualImport.mockRejectedValueOnce(apiError(409, '{}'))
    getAudiobook.mockResolvedValueOnce({
      id: 42,
      files: [{ id: 1, path: '/library/Book/Book.m4b' }],
    })

    const result = await store.importSelected('/library')

    expect(removeFromLibrary).not.toHaveBeenCalled()
    expect(result.errors).toHaveLength(1)
  })

  it('keeps the new entry after a 409 when its files cannot be determined', async () => {
    const store = await storeWithOneBook('move')
    startManualImport.mockRejectedValueOnce(apiError(409, '{}'))
    getAudiobook.mockResolvedValueOnce({ id: 42 })

    await store.importSelected('/library')

    expect(removeFromLibrary).not.toHaveBeenCalled()
  })

  it('keeps the new entry after a 409 when looking the entry up fails', async () => {
    const store = await storeWithOneBook('move')
    startManualImport.mockRejectedValueOnce(apiError(409, '{}'))
    getAudiobook.mockRejectedValueOnce(new Error('Failed to fetch'))

    const result = await store.importSelected('/library')

    expect(removeFromLibrary).not.toHaveBeenCalled()
    expect(result.errors).toHaveLength(1)
  })

  it('keeps the new entry when the import request fails with a server error', async () => {
    const store = await storeWithOneBook('move')
    startManualImport.mockRejectedValueOnce(apiError(500, '{}'))

    const result = await store.importSelected('/library')

    expect(removeFromLibrary).not.toHaveBeenCalled()
    expect(result.errors).toHaveLength(1)
  })

  it('keeps the new entry when the import request fails without a response', async () => {
    const store = await storeWithOneBook('move')
    startManualImport.mockRejectedValueOnce(new Error('Failed to fetch'))

    await store.importSelected('/library')

    expect(removeFromLibrary).not.toHaveBeenCalled()
  })

  it('never removes an existing entry when its import request is rejected', async () => {
    const store = await storeWithOneBook('none')
    addToLibrary.mockRejectedValueOnce({ status: 409, body: { audiobook: { id: 77 } } })
    startManualImport.mockRejectedValueOnce(apiError(404, '{}'))

    await store.importSelected('')

    expect(removeFromLibrary).not.toHaveBeenCalled()
  })

  it('shows the API message unchanged when moving, where the folder hint does not apply', async () => {
    const store = await storeWithOneBook('move')
    addToLibrary.mockRejectedValueOnce(apiError(400, blockedBody))

    const result = await store.importSelected('/library')

    expect(result.errors).toEqual([
      'Book: Destination is already assigned to another audiobook in the library.',
    ])
  })
})
