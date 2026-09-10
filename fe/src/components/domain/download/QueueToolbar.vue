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
  <div class="queue-toolbar">
    <div class="toolbar-left">
      <span v-if="count > 0" class="selected-count" data-test="queue-selected-count">
        {{ count }} selected
      </span>
      <button
        v-if="count > 0"
        class="toolbar-btn"
        data-test="queue-clear-selection"
        @click="emit('clear-selection')"
      >
        Clear Selection
      </button>
      <button
        v-if="count > 0"
        class="toolbar-btn"
        data-test="queue-retry-selected"
        :disabled="!canRetry || busy"
        :title="retryTitle"
        @click="emit('retry-selected')"
      >
        Retry Import
      </button>
      <button
        v-if="count > 0"
        class="toolbar-btn delete-btn"
        data-test="queue-remove-selected"
        :disabled="busy"
        @click="ask('remove')"
      >
        Remove Selected ({{ count }})
      </button>
    </div>
    <div class="toolbar-right">
      <button
        class="toolbar-btn"
        data-test="queue-clear-completed"
        :disabled="busy"
        @click="ask('completed')"
      >
        Clear Completed
      </button>
      <button
        class="toolbar-btn"
        data-test="queue-clear-failed"
        :disabled="busy"
        @click="ask('failed')"
      >
        Clear Failed
      </button>
    </div>

    <ConfirmModal
      :visible="pending !== null"
      :title="confirmTitle"
      :message="confirmMessage"
      :confirmLabel="confirmLabel"
      :confirming="busy"
      @confirm="onConfirm"
      @cancel="onCancel"
    />
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { ConfirmModal } from '@/components/feedback'
import { isImportBlocked } from './queueStatus'

export interface ToolbarRow {
  id: string
  status: string
}

type Pending = 'remove' | 'completed' | 'failed' | null

const props = withDefaults(defineProps<{ selected?: ToolbarRow[]; busy?: boolean }>(), {
  selected: () => [],
  busy: false,
})

const emit = defineEmits<{
  (e: 'clear-selection'): void
  (e: 'retry-selected'): void
  (e: 'remove-selected'): void
  (e: 'clear-completed'): void
  (e: 'clear-failed'): void
}>()

const pending = ref<Pending>(null)

const count = computed(() => props.selected.length)

// The endpoint answers 400 for anything that is not ImportBlocked, so a mixed selection cannot
// be retried as a whole.
const canRetry = computed(
  () => count.value > 0 && props.selected.every((row) => isImportBlocked(row.status)),
)

const retryTitle = computed(() =>
  canRetry.value
    ? 'Ask Listenarr to import these downloads again'
    : 'Retry only applies to downloads whose import was blocked',
)

const confirmTitle = computed(() => {
  if (pending.value === 'remove') return 'Remove selected downloads'
  if (pending.value === 'completed') return 'Clear completed downloads'
  return 'Clear failed downloads'
})

const confirmMessage = computed(() => {
  if (pending.value === 'remove') {
    const noun = count.value === 1 ? 'download' : 'downloads'
    return `Remove ${count.value} selected ${noun}? Anything the download client still holds is removed there too.`
  }
  if (pending.value === 'completed') {
    return 'This deletes every completed download record, not only the ones selected here. Files that were already imported are left alone.'
  }
  return 'This deletes every failed download record, not only the ones selected here. It also deletes downloads that are import blocked, including ones Retry could still fix.'
})

const confirmLabel = computed(() => (pending.value === 'remove' ? 'Remove' : 'Clear'))

const ask = (what: Exclude<Pending, null>) => {
  pending.value = what
}

const onCancel = () => {
  pending.value = null
}

const onConfirm = () => {
  const what = pending.value
  pending.value = null
  if (what === 'remove') emit('remove-selected')
  else if (what === 'completed') emit('clear-completed')
  else if (what === 'failed') emit('clear-failed')
}
</script>

<style scoped>
.queue-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  flex-wrap: wrap;
  margin-bottom: 0.75rem;
}

.toolbar-left,
.toolbar-right {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.selected-count {
  font-size: 0.8rem;
  color: #868e96;
}

.toolbar-btn {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  padding: 0.35rem 0.7rem;
  font-size: 0.8rem;
  color: #adb5bd;
  background: rgba(255, 255, 255, 0.04);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 4px;
  cursor: pointer;
}

.toolbar-btn:hover:not(:disabled) {
  background: rgba(255, 255, 255, 0.08);
}

.toolbar-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.toolbar-btn.delete-btn {
  color: #ff8787;
}
</style>
