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
  <div class="backups-panel">
    <div class="panel-header">
      <h3><PhArchive /> Backups</h3>
      <button
        class="btn btn-primary"
        type="button"
        :disabled="creating"
        @click="createNow"
        data-testid="create-backup"
      >
        <component :is="creating ? PhSpinner : PhArchive" :class="{ spinning: creating }" />
        {{ creating ? 'Backing up...' : 'Back Up Now' }}
      </button>
    </div>

    <p class="panel-intro">
      One is taken automatically before a new release changes the database schema. Archives hold the
      database and config.json and stay in the config directory, where your volume mount already
      reaches them.
    </p>

    <p v-if="error" class="panel-error" role="alert">{{ error }}</p>

    <p v-if="loading" class="panel-empty">Loading backups...</p>
    <p v-else-if="backups.length === 0" class="panel-empty">
      No backups yet. Take one now, or wait for the next release to take one for you.
    </p>

    <ul v-else class="backup-list">
      <li v-for="backup in backups" :key="backup.name" class="backup-entry">
        <span class="backup-name">{{ backup.name }}</span>
        <span class="backup-trigger" :data-trigger="backup.trigger">
          {{ backup.trigger === 'Migration' ? 'Automatic' : 'Manual' }}
        </span>
        <span class="backup-size">{{ formatSize(backup.sizeBytes) }}</span>
        <span class="backup-age">{{ formatAge(backup.createdAtUtc) }}</span>
      </li>
    </ul>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { PhArchive, PhSpinner } from '@phosphor-icons/vue'
import type { BackupArchive } from '@/types'
import { createBackup, getBackups } from '@/services/api'

const backups = ref<BackupArchive[]>([])
const loading = ref(true)
const creating = ref(false)
const error = ref('')

async function load() {
  loading.value = true
  try {
    backups.value = await getBackups()
    error.value = ''
  } catch {
    error.value = 'Could not load the list of backups.'
  } finally {
    loading.value = false
  }
}

async function createNow() {
  creating.value = true
  try {
    await createBackup()
    error.value = ''
    await load()
  } catch {
    // The backend already distinguishes a full disk from an unwritable directory; repeating its
    // wording here would drift, so this stays generic and points at where the detail lives.
    error.value = 'Backup failed. Check the logs and that the config directory is writable.'
  } finally {
    creating.value = false
  }
}

function formatSize(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`
  }

  const units = ['KB', 'MB', 'GB']
  let value = bytes / 1024
  let unit = 0
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024
    unit += 1
  }

  return `${value.toFixed(1)} ${units[unit]}`
}

function formatAge(createdAtUtc: string): string {
  const created = new Date(createdAtUtc)
  if (Number.isNaN(created.getTime())) {
    return 'unknown'
  }

  const minutes = Math.floor((Date.now() - created.getTime()) / 60000)
  if (minutes < 1) {
    return 'just now'
  }
  if (minutes < 60) {
    return `${minutes} min ago`
  }

  const hours = Math.floor(minutes / 60)
  if (hours < 24) {
    return `${hours} hr ago`
  }

  const days = Math.floor(hours / 24)
  return `${days} day${days === 1 ? '' : 's'} ago`
}

onMounted(load)

defineExpose({ load })
</script>

<style scoped>
.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 0.75rem;
}

h3 {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 500;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  color: #fff;
}

.panel-intro {
  margin: 0 0 1rem 0;
  font-size: 0.9rem;
  line-height: 1.5;
  color: #adb5bd;
}

.panel-error {
  margin: 0 0 1rem 0;
  padding: 0.75rem;
  border: 1px solid #a12; /* keeps failure visible without a toast dependency */
  border-radius: 6px;
  color: #ffb3b3;
  font-size: 0.9rem;
}

.panel-empty {
  margin: 0;
  font-size: 0.9rem;
  color: #6c757d;
}

.backup-list {
  list-style: none;
  margin: 0;
  padding: 0;
}

.backup-entry {
  display: grid;
  grid-template-columns: 1fr auto auto auto;
  gap: 1rem;
  align-items: center;
  padding: 0.65rem 0.25rem;
  border-bottom: 1px solid #2c2c2c;
  font-size: 0.9rem;
  color: #dee2e6;
}

.backup-entry:last-child {
  border-bottom: none;
}

.backup-name {
  overflow-wrap: anywhere;
  font-family: monospace;
}

.backup-trigger,
.backup-size,
.backup-age {
  white-space: nowrap;
  color: #adb5bd;
}

.spinning {
  animation: spin 1s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
</style>
