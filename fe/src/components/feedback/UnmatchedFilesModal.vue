<template>
  <Modal :visible="isOpen" size="xl" @close="close">
    <template #header>
      <ModalHeader
        title="Unmatched Files"
        :icon="PhFolderSimpleMagnifyingGlass"
        @close="close"
      />
    </template>

    <template #default>
      <ModalBody>
        <!-- Phase 1: Scanning -->
        <div v-if="phase === 'scanning'" class="scan-status">
          <PhSpinner class="ph-spin scan-spinner" />
          <p>Scanning <strong>{{ rootFolderName }}</strong> for audio files not in your library…</p>
        </div>

        <!-- Phase 1 error -->
        <div v-else-if="phase === 'error'" class="scan-status error">
          <PhWarning class="error-icon" />
          <p>{{ errorMessage }}</p>
        </div>

        <!-- Phase 2: Results -->
        <div v-else-if="phase === 'results'">
          <div v-if="items.length === 0" class="empty-state">
            <PhCheckCircle class="empty-icon" />
            <h4>All files are in your library</h4>
            <p>No unmatched audio files were found in <strong>{{ rootFolderName }}</strong>.</p>
          </div>

          <div v-else>
            <p class="results-summary">
              Found <strong>{{ items.length }}</strong> folder{{ items.length !== 1 ? 's' : '' }} with audio files not in your library.
            </p>

            <div class="results-table-wrapper">
              <table class="results-table">
                <thead>
                  <tr>
                    <th>Title</th>
                    <th>Author</th>
                    <th>Series</th>
                    <th>Year</th>
                    <th>Narrator</th>
                    <th>Files</th>
                    <th>Format</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="item in items" :key="item.fullPath" class="result-row">
                    <td class="cell-title" :title="item.description || undefined">
                      {{ item.title || item.relativePath }}
                    </td>
                    <td class="cell-author">{{ item.author || '—' }}</td>
                    <td class="cell-series">
                      <span v-if="item.series">{{ item.series }}<span v-if="item.seriesNumber" class="series-number"> #{{ item.seriesNumber }}</span></span>
                      <span v-else>—</span>
                    </td>
                    <td class="cell-year">{{ item.year || '—' }}</td>
                    <td class="cell-narrator">{{ item.narrator || '—' }}</td>
                    <td class="cell-files">{{ item.fileCount }}</td>
                    <td class="cell-format">{{ item.format }}</td>
                    <td class="cell-actions">
                      <button
                        class="btn btn-primary btn-sm"
                        @click="startAdd(item)"
                        title="Add to library"
                      >
                        Add
                      </button>
                      <button
                        class="icon-button btn-sm"
                        @click="ignore(item)"
                        title="Ignore"
                      >
                        <PhX />
                      </button>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </ModalBody>
    </template>

    <template #footer>
      <ModalFooter :showCancel="false">
        <template #right>
          <button class="btn" @click="close">Close</button>
        </template>
      </ModalFooter>
    </template>
  </Modal>

  <!-- AddLibraryModal for confirming + adding an audiobook -->
  <AddLibraryModal
    v-if="addingItem"
    :visible="true"
    :book="addingBook"
    @close="addingItem = null"
    @added="onAdded"
  />
</template>

<script setup lang="ts">
import { ref, watch, computed, onUnmounted } from 'vue'
import Modal from '@/components/feedback/Modal.vue'
import ModalHeader from '@/components/feedback/ModalHeader.vue'
import ModalBody from '@/components/feedback/ModalBody.vue'
import ModalFooter from '@/components/feedback/ModalFooter.vue'
import AddLibraryModal from '@/components/domain/audiobook/AddLibraryModal.vue'
import {
  PhFolderSimpleMagnifyingGlass,
  PhSpinner,
  PhWarning,
  PhCheckCircle,
  PhX,
} from '@phosphor-icons/vue'
import { apiService } from '@/services/api'
import { signalRService } from '@/services/signalr'
import { useToast } from '@/services/toastService'
import type { UnmatchedFileItem, AudibleBookMetadata, Audiobook, RootFolder } from '@/types'

interface Props {
  isOpen: boolean
  rootFolder: RootFolder | null
}

interface Emits {
  (e: 'close'): void
}

const props = defineProps<Props>()
const emit = defineEmits<Emits>()

const toast = useToast()

type Phase = 'scanning' | 'results' | 'error'

const phase = ref<Phase>('scanning')
const items = ref<UnmatchedFileItem[]>([])
const errorMessage = ref('')
const addingItem = ref<UnmatchedFileItem | null>(null)

const rootFolderName = computed(() => props.rootFolder?.name || props.rootFolder?.path || 'folder')

let jobId = ''
let offSignalR: (() => void) | null = null

watch(
  () => props.isOpen,
  async (open) => {
    if (!open) return
    if (!props.rootFolder?.id) return

    phase.value = 'scanning'
    items.value = []
    errorMessage.value = ''
    jobId = ''

    // Subscribe to SignalR before triggering the scan
    offSignalR = signalRService.onUnmatchedScanComplete(async (payload) => {
      if (payload.jobId !== jobId) return
      if (payload.error) {
        phase.value = 'error'
        errorMessage.value = payload.error
        return
      }
      try {
        const response = await apiService.getUnmatchedResults(payload.jobId)
        items.value = response.items
        phase.value = 'results'
      } catch (e) {
        phase.value = 'error'
        errorMessage.value = (e as Error)?.message || 'Failed to fetch results'
      }
    })

    try {
      const result = await apiService.scanUnmatchedFiles(props.rootFolder.id)
      jobId = result.jobId
    } catch (e) {
      phase.value = 'error'
      errorMessage.value = (e as Error)?.message || 'Failed to start scan'
      offSignalR?.()
      offSignalR = null
    }
  },
)

onUnmounted(() => {
  offSignalR?.()
})

function close() {
  offSignalR?.()
  offSignalR = null
  emit('close')
}

// Build a minimal AudibleBookMetadata from path-parsed data to pre-fill AddLibraryModal
const addingBook = computed<AudibleBookMetadata>(() => {
  const item = addingItem.value
  if (!item) return { title: '', asin: '', authors: [] }
  return {
    title: item.title || item.relativePath.split('/').pop() || item.relativePath,
    asin: item.asin || '',
    authors: item.author ? [item.author] : [],
    series: item.series,
    seriesNumber: item.seriesNumber,
    publishYear: item.year,
    narrators: item.narrator ? [item.narrator] : [],
    description: item.description,
  }
})

function startAdd(item: UnmatchedFileItem) {
  addingItem.value = item
}

async function onAdded(audiobook: Audiobook) {
  if (!addingItem.value) return
  const item = addingItem.value
  addingItem.value = null

  // Link the file(s) to the newly added audiobook via manual-import
  try {
    await apiService.startManualImport({
      path: item.bookFolder,
      mode: 'interactive',
      inputMode: 'move',
      items: [
        {
          fullPath: item.fullPath,
          matchedAudiobookId: audiobook.id,
        },
      ],
    })
    toast.success('Added', `${audiobook.title || item.title || 'Book'} added to library`)
  } catch {
    toast.success('Added', `${audiobook.title || item.title || 'Book'} added — link files manually if needed`)
  }

  // Remove from the results table
  items.value = items.value.filter((i) => i.fullPath !== item.fullPath)
}

function ignore(item: UnmatchedFileItem) {
  items.value = items.value.filter((i) => i.fullPath !== item.fullPath)
}
</script>

<style scoped>
.scan-status {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1rem;
  padding: 3rem;
  color: #adb5bd;
  text-align: center;
}

.scan-spinner {
  width: 40px;
  height: 40px;
  color: #4dabf7;
}

.scan-status.error {
  color: #f03e3e;
}

.error-icon {
  width: 40px;
  height: 40px;
}

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.75rem;
  padding: 3rem;
  color: #adb5bd;
  text-align: center;
}

.empty-icon {
  width: 40px;
  height: 40px;
  color: #51cf66;
}

.empty-state h4 {
  margin: 0;
  color: #fff;
}

.results-summary {
  margin: 0 0 1rem;
  color: #adb5bd;
  font-size: 0.95rem;
}

.results-table-wrapper {
  overflow-x: auto;
}

.results-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9rem;
}

.results-table th {
  text-align: left;
  padding: 0.5rem 0.75rem;
  color: #868e96;
  font-weight: 500;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  white-space: nowrap;
}

.results-table td {
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);
  vertical-align: middle;
}

.result-row:hover td {
  background: rgba(255, 255, 255, 0.03);
}

.cell-title {
  max-width: 220px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: #fff;
  cursor: default;
}

.cell-author,
.cell-series,
.cell-narrator {
  max-width: 140px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.series-number {
  color: #868e96;
}

.cell-files,
.cell-year,
.cell-format {
  white-space: nowrap;
  color: #adb5bd;
}

.cell-actions {
  display: flex;
  gap: 0.4rem;
  align-items: center;
  white-space: nowrap;
}

.btn-sm {
  padding: 0.25rem 0.6rem;
  font-size: 0.85rem;
}
</style>
