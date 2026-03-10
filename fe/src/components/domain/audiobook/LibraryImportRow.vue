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
          <span
            v-if="item.selectedMatch.authors?.length"
            class="match-author"
            :class="{ 'author-mismatch': isAuthorMismatch(item) }"
            :title="isAuthorMismatch(item) ? `Detected: ${item.detectedAuthor}` : undefined"
          >
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

        <!-- Search modal trigger -->
        <button
          class="btn-search-toggle"
          title="Search for a match"
          @click="showSearchModal = true"
        >
          <PhMagnifyingGlass :size="14" />
        </button>
      </div>
    </td>
  </tr>

  <!-- Search modal (teleported to body by Modal component) -->
  <LibraryImportSearchModal
    v-if="showSearchModal"
    :item="item"
    @close="showSearchModal = false"
    @select="applyMatch"
  />
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { PhSpinner, PhCheckCircle, PhWarningCircle, PhMagnifyingGlass } from '@phosphor-icons/vue'
import { useLibraryImportStore } from '@/stores/libraryImport'
import type { LibraryImportItem } from '@/stores/libraryImport'
import type { SearchResult } from '@/types'
import LibraryImportSearchModal from './LibraryImportSearchModal.vue'

const props = defineProps<{ item: LibraryImportItem }>()

const store = useLibraryImportStore()
const showSearchModal = ref(false)

function isAuthorMismatch(item: LibraryImportItem): boolean {
  if (!item.detectedAuthor || !item.selectedMatch?.authors?.length) return false
  const detected = item.detectedAuthor.toLowerCase()
  const matched = (item.selectedMatch.authors[0]?.name ?? '').toLowerCase()
  return !!matched && !matched.includes(detected) && !detected.includes(matched)
}

function applyMatch(result: SearchResult) {
  store.selectMatch(props.item.id, result)
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

.match-author.author-mismatch {
  color: #f59e0b;
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

.btn-search-toggle:hover {
  border-color: var(--brand-500, #6366f1);
  color: var(--brand-500, #6366f1);
}

/* Mobile: hide format column */
@media (max-width: 640px) {
  .cell-format {
    display: none;
  }
}
</style>
