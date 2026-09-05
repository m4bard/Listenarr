<!--
  Listenarr - Audiobook Management System
  Copyright (C) 2024-2026 Listenarr Contributors

  This program is free software: you can redistribute it and/or modify
  it under the terms of the GNU Affero General Public License as published
  by the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  This program is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
  GNU Affero General Public License for more details.

  You should have received a copy of the GNU Affero General Public License
  along with this program. If not, see <https://www.gnu.org/licenses/>.
-->
<template>
  <div class="wanted-view">
    <div class="page-header">
      <h1>
        <PhHeart />
        Wanted
      </h1>
      <div class="wanted-tabs" role="tablist">
        <button
          class="wanted-tab"
          :class="{ active: wantedMode === 'missing' }"
          role="tab"
          :aria-selected="wantedMode === 'missing'"
          @click="wantedMode = 'missing'"
        >
          Missing
          <span class="wanted-tab-count">{{ wantedAudiobooks.length }}</span>
        </button>
        <button
          class="wanted-tab"
          :class="{ active: wantedMode === 'cutoff' }"
          role="tab"
          :aria-selected="wantedMode === 'cutoff'"
          @click="wantedMode = 'cutoff'"
        >
          Cutoff Unmet
          <span class="wanted-tab-count">{{ cutoffUnmetAudiobooks.length }}</span>
        </button>
      </div>
      <div class="wanted-actions">
        <div class="filter-input-wrapper">
          <PhMagnifyingGlass class="filter-icon" />
          <input
            v-model="filterText"
            type="text"
            class="filter-input"
            placeholder="Filter wanted..."
          />
          <button v-if="filterText" class="filter-clear" @click="filterText = ''">
            <PhX />
          </button>
        </div>
        <button
          v-if="selectedCount > 0"
          class="btn btn-secondary"
          @click="clearSelection"
          :disabled="searchSelectedRunning"
        >
          <PhX />
          Clear Selection
        </button>
        <button
          class="btn btn-primary"
          @click="searchSelected"
          :disabled="selectedCount === 0 || searchSelectedRunning"
        >
          <PhRobot />
          Search Selected ({{ selectedCount }})
        </button>
        <button
          class="btn btn-primary"
          @click="searchActiveBucket"
          :disabled="searchableInActiveBucket.length === 0"
        >
          <PhRobot />
          {{ wantedMode === 'cutoff' ? 'Search All (cutoff unmet)' : 'Search All (missing)' }}
        </button>
        <button class="btn btn-secondary" @click="openManualImport">
          <PhFolderPlus />
          Manual Import
        </button>
      </div>
    </div>

    <!-- Loading State -->
    <LoadingState v-if="loading" message="Loading wanted audiobooks..." />

    <!-- Wanted Table -->
    <div
      v-else-if="filteredWanted.length > 0"
      ref="scrollContainer"
      :class="['wanted-grid-container', { 'is-static': !useVirtualWantedList }]"
      @scroll="updateVisibleRange"
    >
      <div class="wanted-header">
        <div class="col-select">
          <div class="selection-checkbox">
            <input
              type="checkbox"
              aria-label="Select all wanted audiobooks"
              :checked="allWantedSelected"
              :disabled="selectableWanted.length === 0"
              @change="onSelectAllChange"
            />
          </div>
        </div>
        <div class="col-poster"></div>
        <div class="col-title">Title</div>
        <div class="col-author">Author</div>
        <div class="col-series">Series</div>
        <div class="col-quality">Quality</div>
        <div class="col-status">Status</div>
        <div class="col-actions"></div>
      </div>
      <div
        :class="['wanted-body-spacer', { 'is-static': !useVirtualWantedList }]"
        :style="useVirtualWantedList ? { height: `${totalHeight}px` } : undefined"
      >
        <div
          :class="['wanted-body', { 'is-static': !useVirtualWantedList }]"
          :style="useVirtualWantedList ? { transform: `translateY(${topPadding}px)` } : undefined"
        >
          <div
            v-for="item in visibleWanted"
            :key="item.id"
            class="wanted-row"
            :class="{ selected: isSelected(item.id) }"
          >
            <div class="col-select">
              <div class="selection-checkbox">
                <input
                  type="checkbox"
                  :aria-label="`Select ${safeText(item.title)}`"
                  :checked="isSelected(item.id)"
                  :disabled="hasActiveDownload(item) || searchSelectedRunning"
                  :title="
                    hasActiveDownload(item) ? 'Already downloading' : 'Select for Search Selected'
                  "
                  @change="onSelectionCheckboxChange(item, $event)"
                />
              </div>
            </div>
            <div class="col-poster">
              <img
                class="row-poster"
                :src="getProtectedImageSrc(item.imageUrl, getPlaceholderUrl())"
                :alt="item.title"
                loading="lazy"
                decoding="async"
                @error="handleImageError"
              />
            </div>
            <div class="col-title">
              <div class="title-cell">
                <span v-if="hasActiveDownload(item)" class="download-indicator" title="Downloading">
                  <PhDownloadSimple :size="14" weight="fill" />
                </span>
                <RouterLink :to="`/audiobooks/${item.id}`" class="title-link">{{
                  safeText(item.title)
                }}</RouterLink>
              </div>
            </div>
            <div class="col-author">
              <template v-if="item.authors?.length">
                <template v-for="(a, i) in item.authors" :key="a">
                  <RouterLink
                    :to="`/collection/author/${encodeURIComponent(a)}`"
                    class="author-link"
                    >{{ safeText(a) }}</RouterLink
                  ><span v-if="i < item.authors.length - 1">, </span>
                </template>
              </template>
              <span v-else class="author-text">-</span>
            </div>
            <div class="col-series">
              <span v-if="item.series" class="series-text">
                <RouterLink
                  :to="`/collection/series/${encodeURIComponent(item.series)}`"
                  class="series-link"
                  >{{ safeText(item.series) }}</RouterLink
                ><span v-if="item.seriesNumber"> #{{ item.seriesNumber }}</span>
              </span>
              <span v-else class="muted">-</span>
            </div>
            <div class="col-quality">
              <span class="quality-tag">
                {{ getQualityProfileForAudiobook(item)?.name ?? item.quality ?? 'Unknown' }}
              </span>
            </div>
            <div class="col-status">
              <span :class="['status-badge', getStatusClass(item)]">
                {{ getStatusText(item) }}
              </span>
              <div v-if="searchResults[item.id]" class="search-info">
                <PhSpinner v-if="searching[item.id]" class="ph-spin" :size="12" />
                {{ searchResults[item.id] }}
              </div>
            </div>
            <div class="col-actions">
              <div class="actions-cell">
                <button
                  class="btn-icon"
                  @click="searchAudiobook(item)"
                  :disabled="searching[item.id]"
                  title="Automatic Search"
                >
                  <PhRobot />
                </button>
                <button class="btn-icon" @click="openManualSearch(item)" title="Manual Search">
                  <PhMagnifyingGlass />
                </button>
                <button
                  class="btn-icon btn-danger-icon"
                  @click="markAsSkipped(item)"
                  :disabled="searching[item.id]"
                  title="Unmonitor Audiobook"
                >
                  <PhX />
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Empty State -->
    <EmptyState
      v-else
      :title="filterText ? 'No Matching Audiobooks' : 'No Wanted Audiobooks'"
      :message="
        filterText
          ? 'No wanted audiobooks match your filter.'
          : 'All your monitored audiobooks have files!'
      "
    >
      <template #icon>
        <PhCheckCircle :size="48" />
      </template>
    </EmptyState>

    <!-- Manual Search Modal -->
    <ManualSearchModal
      :is-open="showManualSearchModal"
      :audiobook="selectedAudiobook"
      @close="closeManualSearch"
      @downloaded="handleDownloaded"
    />

    <!-- Manual Import Modal -->
    <ManualImportModal
      :is-open="showManualImportModal"
      @close="closeManualImport"
      @imported="handleImported"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount, nextTick, watch } from 'vue'
import { useLibraryStore } from '@/stores/library'
import { useConfigurationStore } from '@/stores/configuration'
import { apiService } from '@/services/api'
import { errorTracking } from '@/services/errorTracking'
import { handleImageError } from '@/utils/imageFallback'
import ManualSearchModal from '@/components/domain/search/ManualSearchModal.vue'
import ManualImportModal from '@/components/feedback/ManualImportModal.vue'
import { EmptyState, LoadingState } from '@/components/base'
import type { Audiobook, SearchResult, Download } from '@/types'
import { safeText } from '@/utils/textUtils'
import {
  PhHeart,
  PhRobot,
  PhFolderPlus,
  PhSpinner,
  PhMagnifyingGlass,
  PhX,
  PhCheckCircle,
  PhDownloadSimple,
} from '@phosphor-icons/vue'
import { logger } from '@/utils/logger'
import { useDownloadsStore } from '@/stores/downloads'
import { useProtectedImages } from '@/composables/useProtectedImages'
import { useRowSelection } from '@/composables/useRowSelection'
import { getPlaceholderUrl } from '@/utils/placeholder'

const downloadsStore = useDownloadsStore()
const { getProtectedImageSrc } = useProtectedImages()
const libraryStore = useLibraryStore()
const configurationStore = useConfigurationStore()

// Filter
const filterText = ref('')
const wantedMode = ref<'missing' | 'cutoff'>('missing')

// Virtual scrolling setup
const scrollContainer = ref<HTMLElement | null>(null)
const ROW_HEIGHT = 48
const BUFFER_ROWS = 5
const MOBILE_WANTED_BREAKPOINT = 768

const visibleRange = ref({ start: 0, end: 30 })
const isMobileWantedLayout = ref(false)
const useVirtualWantedList = computed(() => !isMobileWantedLayout.value)

const updateWantedLayoutMode = () => {
  if (typeof window === 'undefined') return

  if (typeof window.matchMedia === 'function') {
    isMobileWantedLayout.value = window.matchMedia(
      `(max-width: ${MOBILE_WANTED_BREAKPOINT}px)`,
    ).matches
    return
  }

  isMobileWantedLayout.value = window.innerWidth <= MOBILE_WANTED_BREAKPOINT
}

const updateVisibleRange = () => {
  if (!useVirtualWantedList.value) {
    visibleRange.value = { start: 0, end: filteredWanted.value.length }
    return
  }

  if (!scrollContainer.value) return

  const scrollTop = scrollContainer.value.scrollTop
  const viewportHeight = scrollContainer.value.clientHeight

  const firstVisibleIndex = Math.floor(scrollTop / ROW_HEIGHT)
  const visibleItemCount = Math.ceil(viewportHeight / ROW_HEIGHT)

  const startIndex = Math.max(0, firstVisibleIndex - BUFFER_ROWS)
  const endIndex = Math.min(
    firstVisibleIndex + visibleItemCount + BUFFER_ROWS,
    filteredWanted?.value?.length || 0,
  )

  visibleRange.value = { start: startIndex, end: endIndex }
}

const getQualityProfileForAudiobook = (audiobook: Audiobook) => {
  if (!audiobook || !audiobook.qualityProfileId) {
    return null
  }
  const profile = configurationStore.qualityProfiles.find(
    (profile) => profile.id === audiobook.qualityProfileId,
  )
  return profile || null
}

const loading = computed(() => libraryStore.loading)
const searching = ref<Record<number, boolean>>({})
const searchResults = ref<Record<number, string>>({})
const showManualSearchModal = ref(false)
const selectedAudiobook = ref<Audiobook | null>(null)
const showManualImportModal = ref(false)
const searchSelectedRunning = ref(false)
// Set once when the view goes away, so a run in progress stops instead of
// continuing to grab against a component that is no longer mounted.
let searchSelectedAborted = false

const syncWantedLayout = async () => {
  await nextTick()
  updateVisibleRange()
}

const handleViewportResize = () => {
  updateWantedLayoutMode()
  void syncWantedLayout()
}

onMounted(async () => {
  updateWantedLayoutMode()
  if (typeof window !== 'undefined') {
    window.addEventListener('resize', handleViewportResize, { passive: true })
  }

  if (libraryStore.audiobooks.length === 0) {
    await libraryStore.fetchLibrary()
  }
  await configurationStore.loadQualityProfiles()

  await syncWantedLayout()
})

onBeforeUnmount(() => {
  searchSelectedAborted = true
  if (typeof window !== 'undefined') {
    window.removeEventListener('resize', handleViewportResize)
  }
})

// Filter audiobooks that are monitored and missing files
const wantedAudiobooks = computed(() => {
  return libraryStore.audiobooks.filter((audiobook) => {
    const serverWanted = (audiobook as unknown as Record<string, unknown>)['wanted']

    if (serverWanted === true) return true
    if (serverWanted === false) return false

    const hasFiles = Array.isArray(audiobook.files) ? audiobook.files.length > 0 : false
    const hasPrimaryFile = !!(audiobook.filePath && audiobook.filePath.toString().trim() !== '')

    return !!audiobook.monitored && !hasFiles && !hasPrimaryFile
  })
})

// Books below their profile cutoff. These can never appear in wantedAudiobooks: the server's
// `wanted` flag is false for anything that has a file, and a book below cutoff has one. The status
// this reads is already on the same payload the list is built from, so no extra request is needed.
const cutoffUnmetAudiobooks = computed(() => {
  return libraryStore.audiobooks.filter((audiobook) => {
    if (!audiobook.monitored) return false
    return audiobook.status === 'quality-mismatch'
  })
})

const activeWanted = computed(() =>
  wantedMode.value === 'cutoff' ? cutoffUnmetAudiobooks.value : wantedAudiobooks.value,
)

// Categorize wanted audiobooks by their current search state
const categorizedWanted = computed(() => {
  const all = wantedAudiobooks.value
  const notYetSearched = (a: Audiobook) => !searching.value[a.id] && !searchResults.value[a.id]

  return {
    all,
    missing: all.filter(notYetSearched),
    cutoffUnmet: cutoffUnmetAudiobooks.value.filter(notYetSearched),
  }
})

const searchableInActiveBucket = computed(() =>
  wantedMode.value === 'cutoff' ? categorizedWanted.value.cutoffUnmet : categorizedWanted.value.missing,
)

const filteredWanted = computed(() => {
  const items = activeWanted.value
  if (!filterText.value) return items

  const query = filterText.value.toLowerCase()
  return items.filter((item) => {
    const title = (item.title || '').toLowerCase()
    const authors = (item.authors || []).join(' ').toLowerCase()
    const series = (item.series || '').toLowerCase()
    return title.includes(query) || authors.includes(query) || series.includes(query)
  })
})

const visibleWanted = computed(() => {
  if (!useVirtualWantedList.value) {
    return filteredWanted.value
  }

  return filteredWanted.value.slice(visibleRange.value.start, visibleRange.value.end)
})

const totalHeight = computed(() => {
  if (!useVirtualWantedList.value) return 0
  return filteredWanted.value.length * ROW_HEIGHT
})

const topPadding = computed(() => {
  if (!useVirtualWantedList.value) return 0
  return visibleRange.value.start * ROW_HEIGHT
})

watch(
  filteredWanted,
  () => {
    void syncWantedLayout()
  },
  { flush: 'post' },
)

// Map audiobook IDs to active downloads
const activeDownloadsByAudiobook = computed(() => {
  const map = new Map<number, Download>()
  const terminalStates = ['Completed', 'Failed', 'Ready', 'Moved', 'ImportBlocked']

  downloadsStore.downloads.forEach((download) => {
    if (download.audiobookId && !terminalStates.includes(download.status)) {
      map.set(download.audiobookId, download)
    }
  })
  return map
})

function hasActiveDownload(item: Audiobook): boolean {
  return activeDownloadsByAudiobook.value.has(item.id)
}

function getActiveDownload(item: Audiobook): Download | undefined {
  return activeDownloadsByAudiobook.value.get(item.id)
}

// Rows the user may tick. A book already downloading is excluded: searching it
// again would send a second grab for a release that is already in flight, which
// is the thing a per-row Search is careful not to do. Because this is derived
// rather than pruned on an event, a book that starts downloading while ticked
// drops out of the count on its own.
const selectableWanted = computed(() =>
  filteredWanted.value.filter((item) => !hasActiveDownload(item)),
)

const selectableWantedIds = computed(() => selectableWanted.value.map((item) => item.id))

const {
  selectedIds: selectedWantedIds,
  selectedCount,
  isSelected,
  toggleSelection,
  selectAll,
  clearSelection,
  allSelected: allWantedSelected,
} = useRowSelection<number>(() => selectableWantedIds.value)

function onSelectionCheckboxChange(item: Audiobook, event: Event) {
  event.stopPropagation()
  toggleSelection(item.id)
}

function onSelectAllChange(event: Event) {
  event.stopPropagation()
  if (allWantedSelected.value) {
    clearSelection()
  } else {
    selectAll()
  }
}

function getStatusClass(item: Audiobook): string {
  if (hasActiveDownload(item)) {
    return 'downloading'
  }
  if (searching.value[item.id]) {
    return 'searching'
  }
  if (searchResults.value[item.id] && searchResults.value[item.id] !== 'Searching...') {
    return 'failed'
  }
  return 'missing'
}

function getStatusText(item: Audiobook): string {
  const download = getActiveDownload(item)
  if (download) {
    if (download.status === 'Downloading') {
      return `Downloading (${download.progress.toFixed(0)}%)`
    }
    return download.status
  }
  if (searching.value[item.id]) {
    return 'Searching'
  }
  if (searchResults.value[item.id] && searchResults.value[item.id] !== 'Searching...') {
    return 'Failed'
  }
  return 'Missing'
}

const searchActiveBucket = async () => {
  logger.debug(`Automatic search for all ${wantedMode.value} audiobooks`)

  for (const audiobook of searchableInActiveBucket.value) {
    await searchAudiobook(audiobook)
    await new Promise((resolve) => setTimeout(resolve, 1000))
  }
}

const searchSelected = async () => {
  // Read the ids once. The loop awaits, and the selection is derived from live
  // data, so re-reading it each pass would let the set change mid-run.
  const ids = [...selectedWantedIds.value]
  if (ids.length === 0) return

  const byId = new Map(wantedAudiobooks.value.map((item) => [item.id, item]))

  logger.debug('Automatic search for selected audiobooks:', ids.length)
  searchSelectedRunning.value = true
  searchSelectedAborted = false

  try {
    for (const id of ids) {
      if (searchSelectedAborted) return

      const audiobook = byId.get(id)
      if (!audiobook) continue

      // Re-check rather than trusting the snapshot: an earlier pass in this same
      // run, or a push from the downloads hub, may have started a download for
      // this book since the ids were read.
      if (hasActiveDownload(audiobook)) continue

      await searchAudiobook(audiobook)
      await new Promise((resolve) => setTimeout(resolve, 1000))
    }

    if (!searchSelectedAborted) clearSelection()
  } finally {
    searchSelectedRunning.value = false
  }
}

function openManualSearch(item: Audiobook) {
  selectedAudiobook.value = item
  showManualSearchModal.value = true
}

function openManualImport() {
  showManualImportModal.value = true
}

function closeManualImport() {
  showManualImportModal.value = false
}

async function handleImported(result: { imported: number }) {
  logger.debug('Manual import completed, imported:', result.imported)
  await libraryStore.fetchLibrary()
  closeManualImport()
}

function closeManualSearch() {
  showManualSearchModal.value = false
  selectedAudiobook.value = null
}

function handleDownloaded(result: SearchResult) {
  logger.debug('Downloaded:', result)
  setTimeout(async () => {
    try {
      await downloadsStore.loadDownloads()
    } catch (e) {
      logger.warn('Failed to refresh downloads after manual download:', e)
    }
    await libraryStore.fetchLibrary()
    closeManualSearch()
  }, 2000)
}

const searchAudiobook = async (item: Audiobook) => {
  logger.debug('Searching audiobook:', item.title)

  searching.value[item.id] = true
  searchResults.value[item.id] = 'Searching...'

  try {
    const result = await apiService.searchAndDownload(item.id)

    if (result.success) {
      searchResults.value[item.id] = `Found on ${result.indexerUsed}, downloading...`

      setTimeout(async () => {
        try {
          await downloadsStore.loadDownloads()
        } catch (e) {
          logger.warn('Failed to refresh downloads after search:', e)
        }
        await libraryStore.fetchLibrary()
        delete searching.value[item.id]
        delete searchResults.value[item.id]
      }, 2000)
    } else {
      searchResults.value[item.id] = result.message || 'No matches found'
      setTimeout(() => {
        delete searching.value[item.id]
        delete searchResults.value[item.id]
      }, 5000)
    }
  } catch (err) {
    errorTracking.captureException(err as Error, {
      component: 'WantedView',
      operation: 'searchWanted',
      metadata: { itemId: item.id },
    })
    searchResults.value[item.id] = 'Search failed'
    setTimeout(() => {
      delete searching.value[item.id]
      delete searchResults.value[item.id]
    }, 5000)
  }
}

const markAsSkipped = async (item: Audiobook) => {
  logger.debug('Mark as skipped:', item.title)

  try {
    await apiService.updateAudiobook(item.id, { monitored: false })
    await libraryStore.fetchLibrary()
  } catch (err) {
    logger.error('Failed to unmonitor audiobook:', err)
  }
}
</script>

<style scoped>
.wanted-view {
  padding: 1em;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 2rem;
}

.page-header h1 {
  margin: 0;
  color: white;
  font-size: 2rem;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-weight: 500;
}

.page-header h1 svg {
  color: #fa5252;
  width: 32px;
  height: 32px;
}

.wanted-tabs {
  display: flex;
  gap: 4px;
  margin-right: auto;
}

.wanted-tab {
  background: transparent;
  border: 1px solid transparent;
  border-radius: 6px;
  color: #aaa;
  cursor: pointer;
  font-size: 14px;
  padding: 6px 12px;
}

.wanted-tab.active {
  background-color: rgba(var(--brand-rgb), 0.12);
  border-color: rgba(var(--brand-rgb), 0.35);
  color: var(--brand-500);
}

.wanted-tab-count {
  color: inherit;
  margin-left: 6px;
  opacity: 0.75;
}

.wanted-actions {
  display: flex;
  gap: 0.75rem;
  align-items: center;
}

/* Filter input */
.filter-input-wrapper {
  position: relative;
  display: flex;
  align-items: center;
}

.filter-icon {
  position: absolute;
  left: 0.75rem;
  color: #868e96;
  width: 16px;
  height: 16px;
  pointer-events: none;
}

.filter-input {
  background: #2a2a2a;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 6px;
  color: #fff;
  padding: 0.5rem 2rem 0.5rem 2.25rem;
  font-size: 0.875rem;
  width: 220px;
  transition:
    border-color 0.2s,
    box-shadow 0.2s;
}

.filter-input::placeholder {
  color: #868e96;
}

.filter-input:focus {
  outline: none;
  border-color: #4dabf7;
  box-shadow: 0 0 0 2px rgba(77, 171, 247, 0.15);
}

.filter-clear {
  position: absolute;
  right: 0.5rem;
  background: none;
  border: none;
  color: #868e96;
  cursor: pointer;
  padding: 0.25rem;
  border-radius: 4px;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: color 0.2s;
}

.filter-clear:hover {
  color: white;
}

.filter-clear svg {
  width: 14px;
  height: 14px;
}

/* Grid container with virtual scrolling */
.wanted-grid-container {
  height: calc(100vh - 220px);
  overflow-y: auto;
  position: relative;
  border: 1px solid rgba(255, 255, 255, 0.06);
  border-radius: 6px;
  background: #1e1e1e;
}

.wanted-grid-container.is-static {
  height: auto;
  overflow-y: visible;
}

/* Desktop grid columns shared by header and rows */
.wanted-header,
.wanted-row {
  display: grid;
  grid-template-columns:
    40px 48px minmax(0, 28fr) minmax(0, 20fr) minmax(0, 18fr) minmax(0, 10fr)
    minmax(0, 12fr) minmax(0, 12fr);
  align-items: center;
}

/* Selection cell, shared by the header and the rows */
.col-select {
  justify-content: center;
}

.selection-checkbox {
  display: flex;
  align-items: center;
  justify-content: center;
}

.selection-checkbox input[type='checkbox'] {
  width: 16px;
  height: 16px;
  cursor: pointer;
  accent-color: #fa5252;
}

.selection-checkbox input[type='checkbox']:disabled {
  cursor: not-allowed;
  opacity: 0.35;
}

.wanted-row.selected {
  background-color: rgba(250, 82, 82, 0.08);
}

.wanted-header {
  position: sticky;
  top: 0;
  z-index: 2;
  background: #252525;
  padding: 0.65rem 0;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
}

.wanted-header > .col-select {
  padding: 0;
}

.wanted-header > div {
  padding: 0 0.75rem;
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: #868e96;
  white-space: nowrap;
}

.wanted-body-spacer {
  position: relative;
  width: 100%;
}

.wanted-body-spacer.is-static {
  position: static;
}

.wanted-body {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
}

.wanted-body.is-static {
  position: static;
}

/* Grid rows */
.wanted-row {
  transition: background-color 0.15s;
}

.wanted-row:hover {
  background-color: rgba(255, 255, 255, 0.03);
}

.wanted-row > div {
  padding: 0.4rem 0.75rem;
  font-size: 0.875rem;
  color: #adb5bd;
  border-bottom: 1px solid rgba(255, 255, 255, 0.04);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  display: flex;
  align-items: center;
  align-self: stretch;
}

/* Poster cell */
.row-poster {
  width: 32px;
  height: 32px;
  object-fit: cover;
  border-radius: 4px;
  display: block;
}

/* Title cell */
.title-cell {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  min-width: 0;
}

.title-text {
  color: white;
  font-weight: 500;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.title-link {
  color: white;
  font-weight: 500;
  text-decoration: none;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.title-link:hover {
  color: #4dabf7;
}

.download-indicator {
  color: #51cf66;
  display: inline-flex;
  align-items: center;
  flex-shrink: 0;
  animation: bounce 2s ease-in-out infinite;
}

@keyframes bounce {
  0%,
  100% {
    transform: translateY(0);
  }
  50% {
    transform: translateY(-2px);
  }
}

/* Author cell */
.author-text {
  color: #4dabf7;
  font-size: 0.8rem;
}

.author-link {
  color: #4dabf7;
  font-size: 0.8rem;
  text-decoration: none;
}

.author-link:hover {
  text-decoration: underline;
}

/* Series cell */
.series-text {
  color: #868e96;
  font-size: 0.8rem;
}

.series-link {
  color: #868e96;
  text-decoration: none;
}

.series-link:hover {
  color: #adb5bd;
  text-decoration: underline;
}

.muted {
  color: #495057;
  font-size: 0.8rem;
}

/* Quality tag */
.quality-tag {
  font-size: 0.7rem;
  padding: 0.15rem 0.45rem;
  border-radius: 4px;
  background: rgba(255, 212, 59, 0.12);
  color: #ffd43b;
  font-weight: 500;
}

/* Status badges */
.status-badge {
  padding: 0.2rem 0.5rem;
  border-radius: 4px;
  font-size: 0.7rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.3px;
  display: inline-block;
}

.status-badge.missing {
  background-color: rgba(250, 82, 82, 0.15);
  color: #fa5252;
}

.status-badge.searching {
  background-color: rgba(77, 171, 247, 0.15);
  color: #4dabf7;
}

.status-badge.downloading {
  background-color: rgba(81, 207, 102, 0.15);
  color: #51cf66;
  animation: pulse 2s ease-in-out infinite;
}

@keyframes pulse {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.6;
  }
}

.status-badge.failed {
  background-color: rgba(134, 142, 150, 0.15);
  color: #868e96;
}

.search-info {
  font-size: 0.7rem;
  color: #4dabf7;
  display: flex;
  align-items: center;
  gap: 0.3rem;
  margin-top: 0.15rem;
}

.ph-spin {
  animation: spin 1s linear infinite;
}

@keyframes spin {
  from {
    transform: rotate(0deg);
  }
  to {
    transform: rotate(360deg);
  }
}

/* Actions cell */
.actions-cell {
  display: flex;
  gap: 0.25rem;
}

.btn-icon {
  background: none;
  border: 1px solid rgba(255, 255, 255, 0.08);
  color: #adb5bd;
  cursor: pointer;
  padding: 0.3rem;
  border-radius: 4px;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.15s;
}

.btn-icon:hover {
  background-color: rgba(255, 255, 255, 0.08);
  color: white;
}

.btn-icon:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.btn-icon svg {
  width: 16px;
  height: 16px;
}

.btn-danger-icon {
  color: #868e96;
}

.btn-danger-icon:hover {
  background-color: rgba(250, 82, 82, 0.15);
  color: #fa5252;
  border-color: rgba(250, 82, 82, 0.3);
}

@media (max-width: 768px) {
  .page-header {
    flex-direction: column;
    align-items: flex-start;
    gap: 0.75rem;
    margin-bottom: 1rem;
  }

  .wanted-actions {
    flex-direction: column;
    width: 100%;
    gap: 0.5rem;
  }

  .wanted-actions .btn {
    width: 100%;
    justify-content: center;
  }

  .filter-input-wrapper {
    width: 100%;
  }

  .filter-input {
    width: 100%;
  }

  .wanted-grid-container {
    height: auto;
    overflow-y: visible;
    border: none;
    background: transparent;
  }

  .wanted-header {
    display: none;
  }

  .wanted-body-spacer {
    height: auto !important;
    position: static !important;
  }

  .wanted-body {
    position: static !important;
    transform: none !important;
  }

  /* Each row becomes a card */
  .wanted-row {
    grid-template-columns: auto 40px 1fr auto;
    grid-template-rows: auto auto;
    gap: 0.2rem 0.6rem;
    padding: 0.75rem;
    margin-bottom: 0.5rem;
    background: #2a2a2a;
    border-radius: 6px;
    border: 1px solid rgba(255, 255, 255, 0.06);
  }

  .wanted-row:hover {
    background-color: #2f2f2f;
  }

  .wanted-row > div {
    padding: 0;
    border: none;
    overflow: visible;
    white-space: normal;
  }

  /* Hide series and quality on mobile */
  .wanted-row .col-series,
  .wanted-row .col-quality {
    display: none;
  }

  /* Row 1: Select (spans 2 rows) | Poster (spans 2 rows) | Title | Status */
  .wanted-row .col-select {
    grid-column: 1;
    grid-row: 1 / 3;
    align-self: center;
  }

  .wanted-row .col-poster {
    grid-column: 2;
    grid-row: 1 / 3;
    align-self: center;
  }

  .wanted-row .col-title {
    grid-column: 3;
    grid-row: 1;
    min-width: 0;
  }

  .wanted-row .col-status {
    grid-column: 4;
    grid-row: 1;
    white-space: nowrap;
  }

  /* Row 2: Author | Actions */
  .wanted-row .col-author {
    grid-column: 3;
    grid-row: 2;
    min-width: 0;
  }

  .wanted-row .col-author .author-text {
    display: block;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    font-size: 0.75rem;
  }

  .wanted-row .col-actions {
    grid-column: 4;
    grid-row: 2;
    display: flex;
    justify-content: flex-end;
    overflow: visible;
  }

  .row-poster {
    width: 36px;
    height: 36px;
  }

  .actions-cell {
    gap: 0.15rem;
  }

  .btn-icon {
    padding: 0.35rem;
    min-width: 32px;
    min-height: 32px;
  }

  .btn-icon svg {
    width: 16px;
    height: 16px;
  }
}
</style>
