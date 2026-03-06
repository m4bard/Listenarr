<template>
  <div class="import-footer">
    <!-- Left: input mode + rate limit warning -->
    <div class="footer-left">
      <label class="footer-label">
        <select v-model="store.inputMode" class="mode-select">
          <option value="move">Move</option>
          <option value="hardlink/copy">Hardlink / Copy</option>
        </select>
        <span class="footer-to">to:</span>
        <select v-model="destinationFolderId" class="mode-select destination-select">
          <option v-for="f in props.folders" :key="f.id" :value="f.id">
            {{ f.path }}
          </option>
        </select>
      </label>

      <div v-if="store.metadataFetchCount > 100" class="rate-limit-warning">
        <PhWarning :size="14" />
        {{ store.metadataFetchCount }} API lookups — rate limit: 150/window
      </div>
    </div>

    <!-- Center: processing controls -->
    <div class="footer-center">
      <button
        v-if="store.hasUnprocessedItems && !store.isProcessing"
        class="btn btn-success"
        @click="store.startProcessing()"
      >
        <PhPlay :size="14" />
        Start Processing
      </button>

      <template v-if="store.isProcessing">
        <PhSpinner class="ph-spin" :size="14" />
        <span class="processing-label">
          Processing {{ store.processedCount }} / {{ store.itemList.length }}…
        </span>
        <button class="btn btn-warning btn-sm" @click="store.stopProcessing()">
          <PhStop :size="14" />
          Cancel
        </button>
      </template>
    </div>

    <!-- Right: import button -->
    <div class="footer-right">
      <span v-if="store.selectedCount > 0" class="selected-label">
        {{ store.selectedCount }} selected
      </span>
      <button
        class="btn btn-primary"
        :disabled="store.selectedCount === 0 || store.isProcessing"
        @click="handleImport"
      >
        <PhDownload :size="14" />
        Import {{ store.selectedCount > 0 ? store.selectedCount : '' }} Book{{ store.selectedCount !== 1 ? 's' : '' }}
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { PhWarning, PhPlay, PhStop, PhSpinner, PhDownload } from '@phosphor-icons/vue'
import { useLibraryImportStore } from '@/stores/libraryImport'
import { useToast } from '@/services/toastService'
import type { RootFolder } from '@/types'

const props = defineProps<{ folders: RootFolder[] }>()

const store = useLibraryImportStore()
const toast = useToast()

const destinationFolderId = ref<number | null>(props.folders[0]?.id ?? null)
const destinationPath = computed(
  () => props.folders.find((f) => f.id === destinationFolderId.value)?.path ?? '',
)

async function handleImport() {
  const { imported, errors } = await store.importSelected(destinationPath.value)

  if (imported > 0) {
    let msg = `${imported} book${imported !== 1 ? 's' : ''} imported`
    if (store.metadataFetchCount > 0) msg += ` · ${store.metadataFetchCount} metadata lookups`
    toast.success('Import complete', msg)
  }

  if (errors.length > 0) {
    toast.error('Import errors', `${errors.length} item${errors.length !== 1 ? 's' : ''} failed — check logs`)
  }
}
</script>

<style scoped>
.import-footer {
  position: sticky;
  bottom: 0;
  background: #1a1a1a;
  border-top: 1px solid #333;
  padding: 0.75rem 1.5rem;
  display: flex;
  align-items: center;
  gap: 1rem;
  z-index: 10;
}

.footer-left {
  display: flex;
  align-items: center;
  gap: 1rem;
  flex: 1;
}

.footer-label {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 0.82rem;
  color: #aaa;
  white-space: nowrap;
}

.footer-to {
  color: #888;
}

.destination-select {
  font-family: monospace;
  font-size: 0.78rem;
  max-width: 280px;
}

.mode-select {
  background: #2a2a2a;
  border: 1px solid #444;
  border-radius: 4px;
  color: #e0e0e0;
  font-size: 0.82rem;
  padding: 0.25rem 0.5rem;
  cursor: pointer;
}

.rate-limit-warning {
  display: flex;
  align-items: center;
  gap: 0.3rem;
  font-size: 0.78rem;
  color: #f59e0b;
  background: rgba(245, 158, 11, 0.1);
  border: 1px solid rgba(245, 158, 11, 0.3);
  border-radius: 4px;
  padding: 0.2rem 0.5rem;
}

.footer-center {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  color: #888;
  font-size: 0.82rem;
}

.processing-label {
  color: #aaa;
  white-space: nowrap;
}

.footer-right {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.selected-label {
  font-size: 0.82rem;
  color: #888;
  white-space: nowrap;
}

.btn {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  border: none;
  border-radius: 4px;
  cursor: pointer;
  font-size: 0.85rem;
  padding: 0.4rem 0.85rem;
  transition: background 0.15s, opacity 0.15s;
}

.btn:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.btn-primary {
  background: var(--brand-500, #6366f1);
  color: #fff;
}

.btn-primary:hover:not(:disabled) {
  background: var(--brand-600, #4f52c9);
}

.btn-success {
  background: #166534;
  color: #d1fae5;
  border: 1px solid #14532d;
}

.btn-success:hover {
  background: #15803d;
}

.btn-warning {
  background: #78350f;
  color: #fef3c7;
  border: 1px solid #92400e;
}

.btn-warning:hover {
  background: #92400e;
}

.btn-sm {
  padding: 0.25rem 0.6rem;
  font-size: 0.78rem;
}
</style>
