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
<script setup lang="ts">
/**
 * The count on the sidebar's System entry: how many indexer health checks fired, so an indexer
 * outage is visible from every page rather than only on the Indexers settings page.
 *
 * Refreshed when an indexer enters or leaves failure backoff (the backend broadcasts
 * IndexersUpdated on every transition) and when the realtime connection comes back. Red when no
 * enabled indexer is left to ask, amber otherwise.
 */
import { onUnmounted, ref, watch } from 'vue'
import { Pill } from '@/components/base'
import { apiService } from '@/services/api'
import { signalRService } from '@/services/signalr'
import { logger } from '@/utils/logger'
import type { ServiceHealth } from '@/types'

const props = defineProps<{
  /** False until the user is signed in, so the badge never asks a protected endpoint first. */
  enabled: boolean
}>()

const issueCount = ref(0)
const variant = ref<'warning' | 'error'>('warning')

let unsubscribers: Array<() => void> = []
let inFlight = false
let pending = false

const apply = (health: ServiceHealth) => {
  // A backend that predates indexer health has no indexers section: nothing to count.
  const indexers = health.indexers
  issueCount.value = indexers?.checks.length ?? 0
  variant.value = indexers?.status === 'error' ? 'error' : 'warning'
}

// Several indexers can change state in the same search, each with its own broadcast. Coalesce them
// into one request in flight plus at most one more after it.
const refresh = async () => {
  if (inFlight) {
    pending = true
    return
  }
  inFlight = true
  try {
    do {
      pending = false
      try {
        apply(await apiService.getServiceHealth())
      } catch (err) {
        // A failed read leaves the last known count; the next broadcast tries again.
        logger.debug('System health badge refresh failed', err)
      }
    } while (pending)
  } finally {
    inFlight = false
  }
}

const start = () => {
  if (unsubscribers.length > 0) return
  try {
    unsubscribers = [
      signalRService.onIndexersUpdated(() => void refresh()),
      signalRService.onConnected(() => void refresh()),
    ]
  } catch (err) {
    logger.debug('System health badge could not subscribe to realtime updates', err)
  }
  void refresh()
}

const stop = () => {
  unsubscribers.forEach((unsubscribe) => unsubscribe())
  unsubscribers = []
}

watch(
  () => props.enabled,
  (enabled) => (enabled ? start() : stop()),
  { immediate: true },
)

onUnmounted(stop)
</script>

<template>
  <Pill v-if="issueCount > 0" :variant="variant" data-testid="system-health-badge">{{
    issueCount
  }}</Pill>
</template>
