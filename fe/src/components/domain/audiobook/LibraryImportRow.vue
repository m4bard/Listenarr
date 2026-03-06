<template>
  <tr class="import-row" :class="{ selected: item.selected, 'no-match': item.hasSearched && !item.selectedMatch }">
    <!-- Checkbox -->
    <td class="cell-check">
      <input
        type="checkbox"
        :checked="item.selected"
        :disabled="!item.selectedMatch"
        @change="store.toggleSelect(item.id)"
      />
    </td>

    <!-- Folder path -->
    <td class="cell-path">
      <span class="folder-name" :title="item.relativePath">{{ item.folderName }}</span>
      <span class="folder-meta" v-if="item.detectedTitle || item.detectedAuthor">
        {{ [item.detectedTitle, item.detectedAuthor].filter(Boolean).join(' · ') }}
      </span>
    </td>

    <!-- Format / file count -->
    <td class="cell-format">
      <span class="format-badge">{{ item.format }}</span>
      <span class="file-count" v-if="item.fileCount > 1">{{ item.fileCount }} files</span>
    </td>

    <!-- Match cell -->
    <td class="cell-match">
      <div class="match-area">
        <!-- Searching spinner -->
        <div v-if="item.isSearching" class="match-status searching">
          <PhSpinner class="ph-spin" :size="14" />
          <span>Searching…</span>
        </div>

        <!-- Has a match -->
        <div v-else-if="item.selectedMatch" class="match-status matched">
          <PhCheckCircle :size="14" class="match-icon-ok" />
          <span class="match-title" :title="`ASIN: ${item.selectedMatch.asin}`">
            {{ item.selectedMatch.title }}
          </span>
          <span class="match-author" v-if="item.selectedMatch.authors?.length">
            {{ item.selectedMatch.authors[0]?.name }}
          </span>
          <button class="btn-clear-match" title="Clear match" @click="store.clearMatch(item.id)">×</button>
        </div>

        <!-- Searched, no match -->
        <div v-else-if="item.hasSearched" class="match-status no-match">
          <PhWarningCircle :size="14" class="match-icon-warn" />
          <span>No match found</span>
        </div>

        <!-- Not yet searched -->
        <div v-else class="match-status unsearched">
          <span>—</span>
        </div>

        <!-- Search toggle button -->
        <button
          class="btn-search-toggle"
          :class="{ active: showSearch }"
          title="Search for a match"
          @click="toggleSearch"
        >
          <PhMagnifyingGlass :size="14" />
        </button>
      </div>

      <!-- Inline search panel -->
      <div v-if="showSearch" class="search-panel">
        <div class="search-input-wrap">
          <input
            ref="searchInputEl"
            v-model="searchQuery"
            class="search-input"
            placeholder="Search by title or enter ASIN…"
            @input="onSearchInput"
            @keydown.escape="showSearch = false"
          />
          <PhSpinner v-if="isLocalSearching" class="ph-spin search-spinner" :size="14" />
        </div>

        <div v-if="searchResults.length > 0" class="search-results">
          <div
            v-for="result in searchResults"
            :key="result.asin ?? result.title"
            class="search-result-item"
            @click="applyMatch(result)"
          >
            <img v-if="result.imageUrl" :src="result.imageUrl" class="result-thumb" alt="" />
            <div class="result-info">
              <span class="result-title">{{ result.title }}</span>
              <span class="result-meta">
                {{ result.authors?.[0]?.name }}
                <span v-if="result.series"> · {{ result.series }}</span>
                <span v-if="result.asin" class="result-asin"> · {{ result.asin }}</span>
              </span>
            </div>
          </div>
        </div>

        <div v-else-if="hasSearched && searchResults.length === 0" class="search-no-results">
          No results for "{{ searchQuery }}"
        </div>
      </div>
    </td>
  </tr>
</template>

<script setup lang="ts">
import { ref, nextTick } from 'vue'
import { PhSpinner, PhCheckCircle, PhWarningCircle, PhMagnifyingGlass } from '@phosphor-icons/vue'
import { useLibraryImportStore } from '@/stores/libraryImport'
import type { LibraryImportItem } from '@/stores/libraryImport'
import type { SearchResult } from '@/types'

const props = defineProps<{ item: LibraryImportItem }>()

const store = useLibraryImportStore()

const showSearch = ref(false)
const searchQuery = ref(props.item.folderName)
const searchResults = ref<SearchResult[]>([])
const isLocalSearching = ref(false)
const hasSearched = ref(false)
const searchInputEl = ref<HTMLInputElement | null>(null)

let debounceTimer: ReturnType<typeof setTimeout> | null = null

async function toggleSearch() {
  showSearch.value = !showSearch.value
  if (showSearch.value) {
    await nextTick()
    searchInputEl.value?.focus()
    searchInputEl.value?.select()
  }
}

function onSearchInput() {
  if (debounceTimer) clearTimeout(debounceTimer)
  debounceTimer = setTimeout(() => runSearch(), 400)
}

async function runSearch() {
  const q = searchQuery.value.trim()
  if (!q) return
  isLocalSearching.value = true
  hasSearched.value = false
  try {
    const results = await store.searchItem(props.item.id, q)
    searchResults.value = results ?? []
    hasSearched.value = true
  } finally {
    isLocalSearching.value = false
  }
}

function applyMatch(result: SearchResult) {
  store.selectMatch(props.item.id, result)
  showSearch.value = false
  searchResults.value = []
}
</script>

<style scoped>
.import-row td {
  padding: 0.5rem 0.75rem;
  vertical-align: top;
  border-bottom: 1px solid #2a2a2a;
}

.import-row.selected td {
  background-color: rgba(var(--brand-500-rgb, 99, 102, 241), 0.06);
}

/* Checkbox */
.cell-check {
  width: 2.5rem;
  text-align: center;
}

.cell-check input[type='checkbox'] {
  width: 1rem;
  height: 1rem;
  cursor: pointer;
}

.cell-check input:disabled {
  opacity: 0.3;
  cursor: not-allowed;
}

/* Path */
.cell-path {
  max-width: 280px;
}

.folder-name {
  display: block;
  font-family: monospace;
  font-size: 0.85rem;
  color: #e0e0e0;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.folder-meta {
  display: block;
  font-size: 0.75rem;
  color: #888;
  margin-top: 0.1rem;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

/* Format */
.cell-format {
  width: 6rem;
  white-space: nowrap;
}

.format-badge {
  display: inline-block;
  font-size: 0.7rem;
  background: #333;
  color: #aaa;
  border-radius: 3px;
  padding: 0.1rem 0.4rem;
  text-transform: uppercase;
}

.file-count {
  display: block;
  font-size: 0.7rem;
  color: #888;
  margin-top: 0.15rem;
}

/* Match cell */
.cell-match {
  min-width: 280px;
}

.match-area {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: nowrap;
}

.match-status {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  font-size: 0.82rem;
  flex: 1;
  min-width: 0;
}

.match-status.searching {
  color: #888;
}

.match-status.matched {
  color: #e0e0e0;
}

.match-icon-ok {
  color: #4caf50;
  flex-shrink: 0;
}

.match-title {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 180px;
}

.match-author {
  font-size: 0.75rem;
  color: #888;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 100px;
}

.match-status.no-match {
  color: #f59e0b;
}

.match-icon-warn {
  color: #f59e0b;
  flex-shrink: 0;
}

.match-status.unsearched {
  color: #555;
}

.btn-clear-match {
  background: none;
  border: none;
  color: #888;
  cursor: pointer;
  font-size: 1rem;
  line-height: 1;
  padding: 0 0.2rem;
  flex-shrink: 0;
}

.btn-clear-match:hover {
  color: #ef4444;
}

.btn-search-toggle {
  background: none;
  border: 1px solid #444;
  border-radius: 4px;
  color: #888;
  cursor: pointer;
  padding: 0.2rem 0.4rem;
  flex-shrink: 0;
  display: flex;
  align-items: center;
}

.btn-search-toggle:hover,
.btn-search-toggle.active {
  border-color: var(--brand-500, #6366f1);
  color: var(--brand-500, #6366f1);
}

/* Inline search panel */
.search-panel {
  margin-top: 0.4rem;
  background: #1e1e1e;
  border: 1px solid #333;
  border-radius: 6px;
  overflow: hidden;
}

.search-input-wrap {
  display: flex;
  align-items: center;
  padding: 0.3rem 0.5rem;
  gap: 0.4rem;
  border-bottom: 1px solid #2a2a2a;
}

.search-input {
  flex: 1;
  background: transparent;
  border: none;
  outline: none;
  color: #e0e0e0;
  font-size: 0.82rem;
}

.search-spinner {
  color: #888;
  flex-shrink: 0;
}

.search-results {
  max-height: 180px;
  overflow-y: auto;
}

.search-result-item {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.4rem 0.6rem;
  cursor: pointer;
  transition: background 0.15s;
}

.search-result-item:hover {
  background: #2a2a2a;
}

.result-thumb {
  width: 32px;
  height: 32px;
  object-fit: cover;
  border-radius: 3px;
  flex-shrink: 0;
}

.result-info {
  min-width: 0;
  flex: 1;
}

.result-title {
  display: block;
  font-size: 0.82rem;
  color: #e0e0e0;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.result-meta {
  display: block;
  font-size: 0.72rem;
  color: #888;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.result-asin {
  font-family: monospace;
  font-size: 0.7rem;
}

.search-no-results {
  padding: 0.5rem 0.75rem;
  font-size: 0.8rem;
  color: #666;
}
</style>
