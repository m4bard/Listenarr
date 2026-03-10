<template>
  <div class="library-import-view">
    <!-- Header -->
    <div class="page-header">
      <h1>Library Import</h1>
      <p class="page-subtitle">
        Scan a root folder for audio files not in your library, match them to audiobooks, and import them.
      </p>
    </div>

    <!-- Folder selector + scan controls -->
    <div class="scan-controls">
      <div class="folder-select-wrap">
        <label class="control-label">Root folder</label>
        <select v-model="selectedFolderId" class="folder-select" @change="onFolderChange">
          <option v-for="f in rootFoldersStore.folders" :key="f.id" :value="f.id">
            {{ f.name || f.path }}
          </option>
        </select>
      </div>

      <button
        class="btn btn-primary btn-sm"
        :disabled="!selectedFolderId || store.scanStatus === 'scanning'"
        @click="startScan"
      >
        <PhSpinner v-if="store.scanStatus === 'scanning'" class="ph-spin" :size="15" />
        <PhMagnifyingGlass v-else :size="15" />
        {{ store.scanStatus === 'scanning' ? 'Scanning…' : 'Scan' }}
      </button>

      <span v-if="store.lastScannedAt" class="scan-meta">
        Last scanned {{ timeAgo(store.lastScannedAt) }}
        <span v-if="store.itemList.length > 0"> · {{ store.itemList.length }} unmatched</span>
      </span>

      <span v-if="store.scanStatus === 'error'" class="scan-error">
        <PhWarning :size="14" /> {{ store.scanError ?? 'Scan failed' }}
      </span>
    </div>

    <!-- Empty / scanning states -->
    <div v-if="store.scanStatus === 'scanning'" class="state-panel">
      <PhSpinner class="ph-spin state-icon" />
      <p>Scanning for unmatched audio files…</p>
    </div>

    <div v-else-if="store.scanStatus === 'idle' && store.itemList.length === 0" class="state-panel">
      <PhMagnifyingGlass class="state-icon" />
      <h3>No scan results yet</h3>
      <p>Select a root folder and click <strong>Scan</strong> to find unmatched audio files.</p>
    </div>

    <div v-else-if="store.scanStatus === 'done' && store.itemList.length === 0" class="state-panel">
      <PhCheckCircle class="state-icon ok" />
      <h3>All files are in your library</h3>
      <p>No unmatched audio files were found.</p>
    </div>

    <!-- Results table -->
    <div v-else-if="store.itemList.length > 0" class="table-wrap">
      <table class="import-table">
        <thead>
          <tr>
            <th class="col-check">
              <input
                type="checkbox"
                :checked="allMatchedSelected"
                :indeterminate="someSelected && !allMatchedSelected"
                @change="store.toggleSelectAll()"
                title="Select all matched"
              />
            </th>
            <th>Folder</th>
            <th class="col-format">Format</th>
            <th class="col-match">Match</th>
          </tr>
        </thead>
        <tbody>
          <LibraryImportRow
            v-for="item in store.itemList"
            :key="item.id"
            :item="item"
          />
        </tbody>
      </table>
    </div>

    <!-- Footer (always visible when folders exist) -->
    <LibraryImportFooter
      v-if="rootFoldersStore.folders.length > 0"
      :folders="rootFoldersStore.folders"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { PhSpinner, PhMagnifyingGlass, PhWarning, PhCheckCircle } from '@phosphor-icons/vue'
import { useLibraryImportStore } from '@/stores/libraryImport'
import { useRootFoldersStore } from '@/stores/rootFolders'
import { useConfigurationStore } from '@/stores/configuration'
import LibraryImportRow from '@/components/domain/audiobook/LibraryImportRow.vue'
import LibraryImportFooter from '@/components/domain/audiobook/LibraryImportFooter.vue'

const store = useLibraryImportStore()
const rootFoldersStore = useRootFoldersStore()
const configStore = useConfigurationStore()

const selectedFolderId = ref<number | null>(null)

const selectedFolder = computed(() =>
  rootFoldersStore.folders.find((f) => f.id === selectedFolderId.value) ?? null
)

const allMatchedSelected = computed(() => {
  const matched = store.itemList.filter((i) => i.selectedMatch)
  return matched.length > 0 && matched.every((i) => i.selected)
})

const someSelected = computed(() => store.selectedCount > 0)

onMounted(async () => {
  // Ensure stores are loaded
  if (rootFoldersStore.folders.length === 0) await rootFoldersStore.load()
  await configStore.loadApplicationSettings()

  // Default to the default root folder or first
  const defaultFolder = rootFoldersStore.defaultFolder ?? rootFoldersStore.folders[0] ?? null
  if (defaultFolder) {
    selectedFolderId.value = defaultFolder.id
    await store.initFromRootFolder(defaultFolder.id)
  }

  // Sync inputMode from application settings
  const action = configStore.applicationSettings?.completedFileAction
  if (action === 'Hardlink/Copy') store.inputMode = 'hardlink/copy'
  else store.inputMode = 'move'
})

async function onFolderChange() {
  if (!selectedFolderId.value) return
  store.stopProcessing()
  await store.initFromRootFolder(selectedFolderId.value)
}

async function startScan() {
  if (!selectedFolderId.value) return
  await store.triggerScan(selectedFolderId.value)
}

function timeAgo(isoString: string): string {
  const diff = Date.now() - new Date(isoString).getTime()
  const minutes = Math.floor(diff / 60000)
  if (minutes < 1) return 'just now'
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  return `${Math.floor(hours / 24)}d ago`
}
</script>

<style scoped>
.library-import-view {
  padding: 1.5rem;
  padding-bottom: 5rem; /* space for sticky footer */
  max-width: 1200px;
}

.page-header {
  margin-bottom: 1.5rem;
}

.page-header h1 {
  margin: 0 0 0.25rem;
  color: white;
  font-size: 1.75rem;
}

.page-subtitle {
  color: #888;
  font-size: 0.875rem;
  margin: 0;
}

/* Controls */
.scan-controls {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 1.25rem;
  flex-wrap: wrap;
}

.control-label {
  font-size: 0.8rem;
  color: #888;
  margin-right: 0.25rem;
}

.folder-select-wrap {
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.folder-select {
  background: #2a2a2a;
  border: 1px solid #444;
  border-radius: 4px;
  color: #e0e0e0;
  font-size: 0.85rem;
  padding: 0.4rem 0.6rem;
  height: var(--control-height, 40px);
  box-sizing: border-box;
  cursor: pointer;
  min-width: 180px;
}

.scan-meta {
  font-size: 0.8rem;
  color: #666;
}

.scan-error {
  display: flex;
  align-items: center;
  gap: 0.3rem;
  font-size: 0.8rem;
  color: #ef4444;
}

/* State panels */
.state-panel {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 4rem 2rem;
  text-align: center;
  color: #888;
}

.state-icon {
  font-size: 3rem;
  width: 3rem;
  height: 3rem;
  color: #555;
  margin-bottom: 1rem;
}

.state-icon.ok {
  color: #22c55e;
}

.state-panel h3 {
  color: #ccc;
  margin: 0 0 0.5rem;
}

.state-panel p {
  margin: 0;
  font-size: 0.875rem;
}

/* Table */
.table-wrap {
  overflow-x: auto;
  border: 1px solid #2a2a2a;
  border-radius: 6px;
}

.import-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.875rem;
}

.import-table thead tr {
  background: #1e1e1e;
}

.import-table th {
  padding: 0.6rem 0.75rem;
  text-align: left;
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: #888;
  border-bottom: 1px solid #2a2a2a;
  white-space: nowrap;
}

.col-check {
  width: 2.5rem;
  text-align: center;
}

.col-format {
  width: 7rem;
}

.col-match {
  min-width: 280px;
}

.import-table th input[type='checkbox'] {
  width: 1rem;
  height: 1rem;
  cursor: pointer;
}

@media (max-width: 640px) {
  .col-format {
    display: none;
  }
}
</style>
