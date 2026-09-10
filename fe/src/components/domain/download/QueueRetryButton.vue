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
  <button
    v-if="visible"
    class="queue-retry"
    data-test="queue-retry"
    :disabled="running"
    title="Retry the blocked import"
    @click="retry"
  >
    <PhArrowClockwise />
    <span class="queue-retry-label">Retry</span>
  </button>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { PhArrowClockwise } from '@phosphor-icons/vue'
import { apiService } from '@/services/api'
import { errorTracking } from '@/services/errorTracking'
import { useToast } from '@/services/toastService'
import { isImportBlocked } from './queueStatus'

const props = defineProps<{ downloadId: string; status: string }>()
const emit = defineEmits<{ (e: 'retried', id: string): void }>()

const toast = useToast()
const running = ref(false)

// Only ImportBlocked records can be retried; the endpoint answers 400 for the rest.
const visible = computed(() => isImportBlocked(props.status))

const retry = async () => {
  if (running.value) return
  running.value = true
  try {
    await apiService.retryBlockedImport(props.downloadId)
    toast.success('Retrying', 'The import was queued for another attempt')
    emit('retried', props.downloadId)
  } catch (error) {
    errorTracking.captureException(error as Error, {
      component: 'QueueRetryButton',
      operation: 'retryBlockedImport',
      metadata: { downloadId: props.downloadId },
    })
    toast.error('Error', 'Failed to queue the import retry')
  } finally {
    running.value = false
  }
}
</script>

<style scoped>
.queue-retry {
  display: inline-flex;
  align-items: center;
  gap: 0.3rem;
  cursor: pointer;
}

.queue-retry:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>
