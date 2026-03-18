import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

const startManualImport = vi.fn()
const addToLibrary = vi.fn()
const advancedSearch = vi.fn()
const scanUnmatchedFiles = vi.fn()
const getUnmatchedResults = vi.fn()
const getSavedUnmatchedFiles = vi.fn()
let unmatchedScanHandler: ((payload: { jobId: string, error?: string }) => void | Promise<void>) | null = null

vi.mock('@/services/api', () => ({
  apiService: {
    addToLibrary,
    startManualImport,
    advancedSearch,
    getAudibleMetadata: vi.fn(),
    scanUnmatchedFiles,
    getUnmatchedResults,
    getSavedUnmatchedFiles,
  },
}))

vi.mock('@/services/signalr', () => ({
  signalRService: {
    onUnmatchedScanComplete: vi.fn((handler) => {
      unmatchedScanHandler = handler
      return () => {
        unmatchedScanHandler = null
      }
    }),
  },
}))

vi.mock('@/utils/logger', () => ({
  logger: {
    debug: vi.fn(),
  },
}))

describe('library import store', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    unmatchedScanHandler = null
    setActivePinia(createPinia())
    addToLibrary.mockResolvedValue({ audiobook: { id: 42 } })
    startManualImport.mockResolvedValue({ importedCount: 3, totalCount: 3, results: [] })
    advancedSearch.mockResolvedValue([])
    getSavedUnmatchedFiles.mockResolvedValue({ items: [], lastScannedAt: null })
  })

  it('submits every grouped source file during import', async () => {
    const { useLibraryImportStore } = await import('@/stores/libraryImport')
    const store = useLibraryImportStore()

    store.items = {
      'C:\\incoming\\Part 1.mp3': {
        id: 'C:\\incoming\\Part 1.mp3',
        fullPath: 'C:\\incoming\\Part 1.mp3',
        sourceFiles: [
          'C:\\incoming\\Part 1.mp3',
          'C:\\incoming\\Part 2.mp3',
          'C:\\incoming\\Part 10.mp3',
        ],
        folderPath: 'C:\\incoming',
        relativePath: 'Ordered Book',
        folderName: 'Ordered Book',
        format: 'MP3',
        fileCount: 3,
        selectedMatch: {
          title: 'Ordered Book',
          authors: [],
        } as any,
        hasSearched: true,
        isSearching: false,
        selected: true,
      },
    } as any

    await store.importSelected('D:\\library')

    expect(addToLibrary).toHaveBeenCalledTimes(1)
    expect(startManualImport).toHaveBeenCalledTimes(1)
    expect(startManualImport).toHaveBeenCalledWith({
      path: 'C:\\incoming',
      mode: 'interactive',
      inputMode: 'move',
      includeCompanionFiles: true,
      cleanupEmptySourceFolders: true,
      items: [
        { fullPath: 'C:\\incoming\\Part 1.mp3', matchedAudiobookId: 42 },
        { fullPath: 'C:\\incoming\\Part 2.mp3', matchedAudiobookId: 42 },
        { fullPath: 'C:\\incoming\\Part 10.mp3', matchedAudiobookId: 42 },
      ],
    })
  })

  it('ignores foreign scan completions until its own job id is assigned', async () => {
    const { useLibraryImportStore } = await import('@/stores/libraryImport')
    const store = useLibraryImportStore()

    scanUnmatchedFiles.mockImplementation(async () => {
      await unmatchedScanHandler?.({ jobId: 'foreign-job' })
      return { jobId: 'own-job' }
    })
    getUnmatchedResults.mockImplementation(async (jobId: string) => {
      expect(jobId).toBe('own-job')
      return {
        status: 'Completed',
        error: null,
        items: [
          {
            fullPath: 'C:\\incoming\\Book A.mp3',
            sourceFiles: ['C:\\incoming\\Book A.mp3'],
            bookFolder: 'C:\\incoming',
            relativePath: 'Book A',
            title: 'Book A',
            author: 'Author A',
            series: null,
            asin: null,
            format: 'MP3',
            fileCount: 1,
          },
        ],
      }
    })

    await store.triggerScan(7)

    expect(getUnmatchedResults).not.toHaveBeenCalledWith('foreign-job')
    expect(getUnmatchedResults).toHaveBeenCalledWith('own-job')
    expect(Object.keys(store.items)).toEqual(['C:\\incoming\\Book A.mp3'])
    expect(store.scanStatus).toBe('done')
  })

  it('prefers detected title and author for automatic matching before folder fallback', async () => {
    const { useLibraryImportStore } = await import('@/stores/libraryImport')
    const store = useLibraryImportStore()

    advancedSearch.mockResolvedValue([
      {
        title: 'Jack of Shadows',
        authors: [{ name: 'Roger Zelazny' }],
      },
    ])

    store.items = {
      'C:\\incoming\\Chapter 01.mp3': {
        id: 'C:\\incoming\\Chapter 01.mp3',
        fullPath: 'C:\\incoming\\Chapter 01.mp3',
        sourceFiles: ['C:\\incoming\\Chapter 01.mp3'],
        folderPath: 'C:\\incoming',
        relativePath: 'test-import',
        folderName: 'test-import',
        detectedTitle: 'Jack of Shadows',
        detectedAuthor: 'Roger Zelazny',
        format: 'MP3',
        fileCount: 1,
        selectedMatch: null,
        hasSearched: false,
        isSearching: false,
        selected: false,
      },
    } as any

    store.startProcessing()
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(advancedSearch).toHaveBeenCalledWith({
      title: 'Jack of Shadows',
      author: 'Roger Zelazny',
      cap: 5,
    })
    expect(store.items['C:\\incoming\\Chapter 01.mp3']?.selectedMatch?.title).toBe('Jack of Shadows')
  })
})
