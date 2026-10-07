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
  <div class="blocklist-page">
    <div class="blocklist-header">
      <h2>Blocklist</h2>
      <button @click="refreshBlocklist" :disabled="loading" class="refresh-button btn">
        {{ loading ? 'Refreshing...' : 'Refresh' }}
      </button>
    </div>

    <div class="blocklist-content">
      <EmptyState
        v-if="!loading && blockedReleases.length === 0"
        title="No Blocked Releases"
        message="A release lands here when a grabbed download fails for a reason that isn't the release's own fault. Nothing is currently blocked, so every release a search finds is eligible to be grabbed."
      >
        <template #icon>
          <PhProhibit :size="48" />
        </template>
      </EmptyState>

      <div v-else class="blocklist-list">
        <div v-for="entry in blockedReleases" :key="entry.id" class="blocklist-card">
          <div class="blocklist-info">
            <h3>
              <RouterLink :to="`/audiobooks/${entry.audiobookId}`" class="title-link">
                {{ getDisplayTitle(entry) }}
              </RouterLink>
            </h3>
            <p class="blocklist-release-title">{{ entry.title }}</p>

            <div class="blocklist-meta">
              <span v-if="entry.size != null" class="blocklist-size">{{
                formatFileSize(entry.size)
              }}</span>
              <span class="blocklist-date">{{ formatDate(entry.blockedAt) }}</span>
              <span v-if="entry.reason" class="blocklist-reason">{{ entry.reason }}</span>
            </div>
          </div>

          <div class="blocklist-actions">
            <button
              @click="removeEntry(entry)"
              :disabled="removingId === entry.id"
              class="action-button remove btn"
            >
              {{ removingId === entry.id ? 'Removing...' : 'Remove' }}
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { RouterLink } from 'vue-router'
import { useLibraryStore } from '@/stores/library'
import { apiService } from '@/services/api'
import { useToast } from '@/services/toastService'
import { errorTracking } from '@/services/errorTracking'
import { showConfirm } from '@/composables/useConfirm'
import { PhProhibit } from '@phosphor-icons/vue'
import { EmptyState } from '@/components/base'
import type { BlockedRelease } from '@/types'

const libraryStore = useLibraryStore()
const toast = useToast()

const blockedReleases = ref<BlockedRelease[]>([])
const loading = ref(false)
const removingId = ref<number | null>(null)

// Resolve a book title the same way ActivityView does: BlockedRelease only carries an
// AudiobookId (there is no navigation property on the backend side, see
// AudiobookRepository.Blocklist.cs), so the title is looked up from the library already
// loaded into memory rather than carried on the entry itself.
const audiobookTitleMap = computed(() => {
  const map = new Map<number, string>()
  for (const ab of libraryStore.audiobooks) {
    if (ab.id && ab.title) map.set(ab.id, ab.title)
  }
  return map
})

const getDisplayTitle = (entry: BlockedRelease): string => {
  return audiobookTitleMap.value.get(entry.audiobookId) ?? `Audiobook #${entry.audiobookId}`
}

const refreshBlocklist = async () => {
  loading.value = true
  try {
    blockedReleases.value = await apiService.getBlocklist()
  } catch (error) {
    errorTracking.captureException(error as Error, {
      component: 'BlocklistView',
      operation: 'refreshBlocklist',
    })
    toast.error('Error', 'Failed to load the blocklist')
  } finally {
    loading.value = false
  }
}

const removeEntry = async (entry: BlockedRelease) => {
  const ok = await showConfirm(
    `Remove "${entry.title}" from the blocklist? Listenarr will be able to grab this release again.`,
    'Confirm Removal',
    { danger: true, confirmText: 'Remove', cancelText: 'Cancel' },
  )
  if (!ok) return

  removingId.value = entry.id
  try {
    await apiService.deleteBlocklistEntry(entry.id)
    blockedReleases.value = blockedReleases.value.filter((e) => e.id !== entry.id)
    toast.success('Success', 'Removed from the blocklist')
  } catch (error) {
    errorTracking.captureException(error as Error, {
      component: 'BlocklistView',
      operation: 'removeEntry',
      metadata: { blocklistEntryId: entry.id },
    })
    toast.error('Error', 'Failed to remove the blocklist entry')
  } finally {
    removingId.value = null
  }
}

const formatFileSize = (bytes: number): string => {
  const sizes = ['Bytes', 'KB', 'MB', 'GB']
  if (bytes === 0) return '0 Bytes'
  const i = Math.floor(Math.log(bytes) / Math.log(1024))
  return Math.round((bytes / Math.pow(1024, i)) * 100) / 100 + ' ' + sizes[i]
}

const formatDate = (dateString: string): string => {
  return new Date(dateString).toLocaleDateString() + ' ' + new Date(dateString).toLocaleTimeString()
}

onMounted(async () => {
  if (libraryStore.audiobooks.length === 0) {
    void libraryStore.fetchLibrary()
  }
  await refreshBlocklist()
})
</script>

<style scoped>
.blocklist-page {
  max-width: 1200px;
  margin: 0 auto;
}

.blocklist-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 2rem;
}

.blocklist-header h2 {
  margin: 0;
  color: #2c3e50;
}

.refresh-button {
  padding: 0.5rem 1rem;
  background-color: #3498db;
  color: white;
  border: none;
  border-radius: 6px;
  cursor: pointer;
  transition: background-color 0.2s;
}

.refresh-button:hover:not(:disabled) {
  background-color: #2980b9;
}

.refresh-button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.blocklist-content {
  min-height: 400px;
}

.blocklist-list {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.blocklist-card {
  background: white;
  border-radius: 6px;
  padding: 1.5rem;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
  display: grid;
  grid-template-columns: 1fr auto;
  gap: 2rem;
  align-items: start;
}

.blocklist-info h3 {
  margin: 0 0 0.5rem 0;
}

.title-link {
  color: #2c3e50;
  text-decoration: none;
}

.title-link:hover {
  text-decoration: underline;
}

.blocklist-release-title {
  margin: 0 0 1rem 0;
  color: #777;
  font-style: italic;
}

.blocklist-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 1rem;
  font-size: 0.9rem;
  color: #666;
}

.blocklist-meta span {
  background-color: #f8f9fa;
  padding: 0.25rem 0.5rem;
  border-radius: 6px;
}

.blocklist-actions {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.action-button {
  padding: 0.5rem 1rem;
  border: none;
  border-radius: 6px;
  cursor: pointer;
  font-size: 0.9rem;
  transition: background-color 0.2s;
}

.action-button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.action-button.remove {
  background-color: #e74c3c;
  color: white;
}

.action-button.remove:hover:not(:disabled) {
  background-color: #c0392b;
}

@media (max-width: 768px) {
  .blocklist-card {
    grid-template-columns: 1fr;
    gap: 1rem;
  }

  .blocklist-actions {
    flex-direction: row;
  }
}
</style>
