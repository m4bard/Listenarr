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
  <div class="form-section">
    <h3><PhTrash /> Recycle Bin</h3>
    <div class="form-body">
      <FormRow
        label="Recycle Bin Path"
        help="Where a deleted library file is moved instead of being removed outright. This path must sit outside every root folder, or the retention sweep could walk back into the library it is meant to be protecting. Leave it empty for no recycle bin at all; deletes are then permanent."
      >
        <input
          :value="settings.recycleBinPath ?? ''"
          @input="updatePath"
          type="text"
          placeholder="/path/to/recycle-bin"
        />
      </FormRow>

      <FormRow
        label="Retention (days)"
        help="How long a recycled file is kept before the retention sweep removes it for good. Zero keeps everything until the bin is emptied by hand."
      >
        <input
          :value="settings.recycleBinCleanupDays ?? DEFAULT_CLEANUP_DAYS"
          @input="updateCleanupDays"
          type="number"
          min="0"
        />
      </FormRow>

      <FormRow
        label="Empty Recycle Bin"
        help="Permanently deletes every file currently sitting in the recycle bin right now. This does not change the path or retention settings above, and it cannot be undone."
      >
        <button
          type="button"
          class="btn btn-danger"
          :disabled="!canEmpty || emptying"
          @click="handleEmptyBin"
        >
          <PhSpinner v-if="emptying" class="ph-spin" />
          {{ emptying ? 'Emptying...' : 'Empty Recycle Bin Now' }}
        </button>
        <p v-if="emptyResult" class="recycle-bin-result recycle-bin-result-success">
          {{ emptyResult }}
        </p>
        <p v-if="emptyError" class="recycle-bin-result recycle-bin-result-error">
          {{ emptyError }}
        </p>
      </FormRow>
    </div>
  </div>
</template>

<script setup lang="ts">
import type { ApplicationSettings } from '@/types'
import { computed, ref } from 'vue'
import { PhTrash, PhSpinner } from '@phosphor-icons/vue'
import FormRow from '@/components/settings/FormRow.vue'
import { apiService } from '@/services/api'

const props = defineProps<{ settings: Partial<ApplicationSettings> }>()
const emit = defineEmits<{
  'update:settings': [value: Partial<ApplicationSettings>]
}>()

// Matches ApplicationSettings.RecycleBinCleanupDays's shipped default. Used when the box is
// emptied or a non-numeric value is typed, never silently as zero: zero is a legal retention
// value that means something quite different (keep until emptied by hand).
const DEFAULT_CLEANUP_DAYS = 7

const emptying = ref(false)
const emptyResult = ref<string | null>(null)
const emptyError = ref<string | null>(null)

// The button only ever acts on a configured bin. With no path there is nothing to empty, and
// every delete is already permanent, so the control stays disabled rather than reaching a
// server route that has nothing to do.
const canEmpty = computed(() => Boolean(props.settings.recycleBinPath?.trim()))

function updateField(field: keyof ApplicationSettings, value: unknown) {
  const payload = { ...(props.settings || {}), [field]: value } as Partial<ApplicationSettings>
  emit('update:settings', payload)
}

function updatePath(event: Event) {
  updateField('recycleBinPath', (event.target as HTMLInputElement).value)
}

function updateCleanupDays(event: Event) {
  const raw = (event.target as HTMLInputElement).value
  const parsed = Number(raw)
  const value = raw.trim() === '' || Number.isNaN(parsed) ? DEFAULT_CLEANUP_DAYS : parsed
  updateField('recycleBinCleanupDays', Math.max(0, Math.round(value)))
}

async function handleEmptyBin() {
  if (!canEmpty.value || emptying.value) return

  // This destroys files with no way back, so it asks first rather than firing on a single
  // click the way the form fields above do.
  const confirmed = window.confirm(
    'This permanently deletes every file currently in the recycle bin. This cannot be undone. Continue?',
  )
  if (!confirmed) return

  emptying.value = true
  emptyResult.value = null
  emptyError.value = null
  try {
    const result = await apiService.emptyRecycleBin()
    emptyResult.value =
      result.deletedCount === 1
        ? 'Removed 1 file from the recycle bin.'
        : `Removed ${result.deletedCount} files from the recycle bin.`
  } catch (err) {
    emptyError.value = err instanceof Error ? err.message : 'Failed to empty the recycle bin.'
  } finally {
    emptying.value = false
  }
}
</script>

<style scoped>
h3 {
  margin: 0 0 1.5rem 0;
  padding: 0;
  font-size: 1.1rem;
  font-weight: 500;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  color: #fff;
}

.form-body {
  padding: 1.25rem;
  border-radius: 6px;
  border: 1px solid #333;
  box-shadow: 0 4px 14px rgba(0, 0, 0, 0.6);
  background-color: #232323;
}

.form-row-label {
  margin-bottom: 0.5rem;
  font-weight: 500;
  color: #fff;
}

.form-group input[type='text'],
.form-group input[type='number'],
.form-row-control input[type='text'],
.form-row-control input[type='number'] {
  width: 100%;
  padding: 0.9rem 0.85rem;
  border: 1px solid #444;
  border-radius: 6px;
  background-color: #1a1a1a;
  color: #fff;
  font-size: 0.95rem;
}

.form-group input:disabled,
.form-row-control input:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.form-group input:focus,
.form-row-control input:focus {
  outline: none;
  border-color: var(--brand-500);
  box-shadow: 0 0 0 3px rgba(77, 171, 247, 0.08);
}

.form-help {
  display: block;
  margin-top: 0.5rem;
  font-size: 0.85rem;
  color: #adb5bd;
  line-height: 1.5;
}

.recycle-bin-result {
  margin: 0.75rem 0 0 0;
  font-size: 0.85rem;
  line-height: 1.5;
}

.recycle-bin-result-success {
  color: #4caf50;
}

.recycle-bin-result-error {
  color: #f44336;
}

/* @keyframes spin is centralized in src/assets/animations.css */
</style>
