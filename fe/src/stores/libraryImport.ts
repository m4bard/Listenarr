import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { apiService } from '@/services/api'
import { signalRService } from '@/services/signalr'
import { logger } from '@/utils/logger'
import type { SearchResult, AudibleBookMetadata, UnmatchedFileItem } from '@/types'

export interface LibraryImportItem {
  id: string            // = fullPath (unique key)
  fullPath: string      // path to audio file
  folderPath: string    // bookFolder (parent directory)
  relativePath: string  // relative to root folder
  folderName: string    // search term: last non-empty path segment
  // Detected from file scan
  detectedTitle?: string
  detectedAuthor?: string
  detectedAsin?: string
  detectedSeries?: string
  format: string
  fileCount: number
  // Match state
  selectedMatch: SearchResult | null
  hasSearched: boolean  // true once auto-search was attempted
  isSearching: boolean  // currently in-flight
  // Selection
  selected: boolean
}

function extractFolderName(relativePath: string): string {
  const parts = relativePath.replace(/\\/g, '/').split('/').filter(Boolean)
  // Prefer the last meaningful segment (author/title structure)
  return parts[parts.length - 1] ?? relativePath
}

function unmatchedToImportItem(item: UnmatchedFileItem): LibraryImportItem {
  return {
    id: item.fullPath,
    fullPath: item.fullPath,
    folderPath: item.bookFolder,
    relativePath: item.relativePath,
    folderName: extractFolderName(item.relativePath),
    detectedTitle: item.title,
    detectedAuthor: item.author,
    detectedAsin: item.asin,
    detectedSeries: item.series,
    format: item.format,
    fileCount: item.fileCount,
    selectedMatch: null,
    hasSearched: false,
    isSearching: false,
    selected: false,
  }
}

function matchToMetadata(result: SearchResult): AudibleBookMetadata {
  const authors: string[] =
    result.authors && result.authors.length > 0
      ? result.authors.map((a) => a.name ?? '').filter(Boolean)
      : []

  return {
    title: result.title ?? '',
    asin: result.asin ?? '',
    authors,
    subtitle: result.subtitle,
    series: result.series,
    seriesNumber: result.seriesNumber,
    seriesAsin: result.seriesAsin,
    description: result.description,
    publisher: result.publisher,
    language: result.language,
    runtime: result.runtime ?? (result.lengthMinutes ? result.lengthMinutes * 60 : undefined),
    imageUrl: result.imageUrl,
    genres: result.genres,
    narrators: result.narrators?.map((n) => n.name ?? '').filter(Boolean),
    publishYear: result.releaseDate?.substring(0, 4) ?? result.publishDate?.substring(0, 4),
    metadataSource: result.metadataSource,
  }
}

export const useLibraryImportStore = defineStore('libraryImport', () => {
  const items = ref<Record<string, LibraryImportItem>>({})
  const lookupQueue = ref<string[]>([])
  const isProcessing = ref(false)
  const rootFolderId = ref<number | null>(null)
  const scanStatus = ref<'idle' | 'scanning' | 'done' | 'error'>('idle')
  const scanError = ref<string | null>(null)
  const lastScannedAt = ref<string | null>(null)
  const inputMode = ref<'move' | 'hardlink/copy'>('move')
  const metadataFetchCount = ref(0)
  const importErrors = ref<string[]>([])

  // ─── Computed ────────────────────────────────────────────────────────────────

  const itemList = computed(() => Object.values(items.value))
  const selectedCount = computed(() => itemList.value.filter((i) => i.selected).length)
  const hasUnprocessedItems = computed(() => itemList.value.some((i) => !i.hasSearched))
  const processedCount = computed(() => itemList.value.filter((i) => i.hasSearched).length)
  const matchedCount = computed(() => itemList.value.filter((i) => i.selectedMatch).length)

  // ─── Scan ─────────────────────────────────────────────────────────────────

  async function initFromRootFolder(id: number) {
    rootFolderId.value = id
    scanStatus.value = 'idle'
    try {
      const saved = await apiService.getSavedUnmatchedFiles(id)
      if (saved.lastScannedAt) lastScannedAt.value = saved.lastScannedAt
      const newItems: Record<string, LibraryImportItem> = {}
      for (const item of saved.items) {
        const existing = items.value[item.fullPath]
        // Preserve match/search state if item already exists
        newItems[item.fullPath] = existing
          ? { ...unmatchedToImportItem(item), selectedMatch: existing.selectedMatch, hasSearched: existing.hasSearched, isSearching: false, selected: existing.selected }
          : unmatchedToImportItem(item)
      }
      items.value = newItems
      if (saved.items.length > 0) scanStatus.value = 'done'
    } catch (e) {
      logger.debug('[libraryImport] Failed to load saved results:', e)
    }
  }

  async function triggerScan(id: number) {
    rootFolderId.value = id
    scanStatus.value = 'scanning'
    scanError.value = null
    items.value = {}

    let jobId = ''
    let offSignalR: (() => void) | null = null

    offSignalR = signalRService.onUnmatchedScanComplete(async (payload) => {
      if (payload.jobId !== jobId) return
      offSignalR?.()
      if (payload.error) {
        scanStatus.value = 'error'
        scanError.value = payload.error
        return
      }
      try {
        const response = await apiService.getUnmatchedResults(payload.jobId)
        _populateFromItems(response.items)
        lastScannedAt.value = new Date().toISOString()
        scanStatus.value = 'done'
      } catch (e) {
        scanStatus.value = 'error'
        scanError.value = (e as Error)?.message ?? 'Failed to fetch results'
      }
    })

    try {
      const result = await apiService.scanUnmatchedFiles(id)
      jobId = result.jobId
      // Poll once immediately — handles fast scans before SignalR fires
      const check = await apiService.getUnmatchedResults(jobId)
      if (check.status === 'Completed') {
        offSignalR?.()
        _populateFromItems(check.items)
        lastScannedAt.value = new Date().toISOString()
        scanStatus.value = 'done'
      } else if (check.status === 'Failed') {
        offSignalR?.()
        scanStatus.value = 'error'
        scanError.value = check.error ?? 'Scan failed'
      }
    } catch (e) {
      offSignalR?.()
      scanStatus.value = 'error'
      scanError.value = (e as Error)?.message ?? 'Failed to start scan'
    }
  }

  function _populateFromItems(scanItems: UnmatchedFileItem[]) {
    const newItems: Record<string, LibraryImportItem> = {}
    for (const item of scanItems) {
      newItems[item.fullPath] = unmatchedToImportItem(item)
    }
    items.value = newItems
  }

  // ─── Queue Processing ─────────────────────────────────────────────────────

  function startProcessing() {
    const unsearched = itemList.value.filter((i) => !i.hasSearched).map((i) => i.id)
    if (unsearched.length === 0) return
    lookupQueue.value = unsearched
    isProcessing.value = true
    metadataFetchCount.value = 0
    processNext()
  }

  function stopProcessing() {
    lookupQueue.value = []
    isProcessing.value = false
    // Clear any in-flight isSearching flags
    for (const id of Object.keys(items.value)) {
      const entry = items.value[id]
      if (entry?.isSearching) {
        items.value = { ...items.value, [id]: { ...entry, isSearching: false } }
      }
    }
  }

  async function processNext() {
    if (!isProcessing.value || lookupQueue.value.length === 0) {
      isProcessing.value = false
      return
    }

    const id: string = lookupQueue.value[0]!
    const item = items.value[id]

    if (!item) {
      lookupQueue.value = lookupQueue.value.slice(1)
      await processNext()
      return
    }

    items.value = { ...items.value, [id]: { ...item, isSearching: true } }

    try {
      const searchParams = item.detectedAsin
        ? { asin: item.detectedAsin, cap: 5 }
        : { title: item.detectedTitle ?? item.folderName, cap: 5 }
      const results = await apiService.advancedSearch(searchParams)
      metadataFetchCount.value++
      const first = results[0] ?? null
      const current = items.value[id]!
      items.value = {
        ...items.value,
        [id]: { ...current, isSearching: false, hasSearched: true, selectedMatch: first, selected: first !== null },
      }
    } catch {
      const current = items.value[id]
      if (current) items.value = { ...items.value, [id]: { ...current, isSearching: false, hasSearched: true } }
    }

    lookupQueue.value = lookupQueue.value.slice(1)
    await processNext()
  }

  // ─── Per-row manual search ────────────────────────────────────────────────

  async function searchItem(id: string, query: string) {
    const item = items.value[id]
    if (!item) return

    items.value[id] = { ...item, isSearching: true }
    try {
      const results = await apiService.advancedSearch({ title: query, cap: 5 })
      items.value[id] = { ...items.value[id], isSearching: false, hasSearched: true }
      return results
    } catch {
      items.value[id] = { ...items.value[id], isSearching: false }
      return []
    }
  }

  // ─── Match management ─────────────────────────────────────────────────────

  function selectMatch(id: string, match: SearchResult) {
    const item = items.value[id]
    if (!item) return
    items.value[id] = { ...item, selectedMatch: match, hasSearched: true, selected: true }
  }

  function clearMatch(id: string) {
    const item = items.value[id]
    if (!item) return
    items.value[id] = { ...item, selectedMatch: null, selected: false }
  }

  // ─── Selection ────────────────────────────────────────────────────────────

  function toggleSelect(id: string) {
    const item = items.value[id]
    if (!item) return
    items.value[id] = { ...item, selected: !item.selected }
  }

  function toggleSelectAll() {
    const allSelected = itemList.value.filter((i) => i.selectedMatch).every((i) => i.selected)
    for (const item of itemList.value) {
      if (item.selectedMatch) {
        items.value[item.id] = { ...item, selected: !allSelected }
      }
    }
  }

  // ─── Import ───────────────────────────────────────────────────────────────

  async function importSelected(rootFolderPath: string): Promise<{ imported: number; errors: string[] }> {
    const toImport = itemList.value.filter((i) => i.selected && i.selectedMatch)
    importErrors.value = []
    let imported = 0

    for (const item of toImport) {
      const match = item.selectedMatch!
      try {
        const { audiobook } = await apiService.addToLibrary(matchToMetadata(match), {
          destinationPath: rootFolderPath,
          searchResult: match,
        })
        await apiService.startManualImport({
          path: item.folderPath,
          mode: 'interactive',
          inputMode: inputMode.value,
          items: [{ fullPath: item.fullPath, matchedAudiobookId: audiobook.id }],
        })
        // Remove imported item from store
        const updated = { ...items.value }
        delete updated[item.id]
        items.value = updated
        imported++
      } catch (e) {
        const msg = `${item.folderName}: ${(e as Error)?.message ?? 'Import failed'}`
        importErrors.value.push(msg)
        logger.debug('[libraryImport] Import error:', msg)
      }
    }

    return { imported, errors: importErrors.value }
  }

  return {
    // State
    items,
    lookupQueue,
    isProcessing,
    rootFolderId,
    scanStatus,
    scanError,
    lastScannedAt,
    inputMode,
    metadataFetchCount,
    importErrors,
    // Computed
    itemList,
    selectedCount,
    hasUnprocessedItems,
    processedCount,
    matchedCount,
    // Actions
    initFromRootFolder,
    triggerScan,
    startProcessing,
    stopProcessing,
    processNext,
    searchItem,
    selectMatch,
    clearMatch,
    toggleSelect,
    toggleSelectAll,
    importSelected,
  }
})
