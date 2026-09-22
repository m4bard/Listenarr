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
    <h3><PhHardDrives /> Import Safeguards</h3>
    <div class="form-body">
      <FormRow
        label="Minimum Free Space (MB)"
        help="Reject an import when the destination would have less than this much space left after the files are written (0-1048576 MB). Matches Readarr's MinimumFreeSpaceWhenImporting."
      >
        <input
          :value="settings.minimumFreeSpaceWhenImporting ?? DEFAULT_MINIMUM_FREE_SPACE_MB"
          @input="updateMinimumFreeSpace"
          :disabled="settings.skipFreeSpaceCheckWhenImporting === true"
          type="number"
          min="0"
          max="1048576"
        />
      </FormRow>

      <CheckboxCard
        :modelValue="settings.skipFreeSpaceCheckWhenImporting"
        @update:modelValue="updateSkipFreeSpaceCheck"
        title="Skip Free Space Check"
        description="Turn off the free-space check entirely. Use this if a network filesystem reports free space Listenarr cannot trust, since a false 'not enough space' reading would otherwise block every import with no other way around it."
      />
    </div>
  </div>
</template>

<script setup lang="ts">
import type { ApplicationSettings } from '@/types'
import { PhHardDrives } from '@phosphor-icons/vue'
import FormRow from '@/components/settings/FormRow.vue'
import CheckboxCard from '@/components/settings/CheckboxCard.vue'

const props = defineProps<{ settings: Partial<ApplicationSettings> }>()
const emit = defineEmits<{
  'update:settings': [value: Partial<ApplicationSettings>]
}>()

// The shipped server default (ApplicationSettings.MinimumFreeSpaceWhenImporting = 100),
// matching Readarr's ConfigService.cs:195. Also this field's fallback display value, so an
// emptied box does not silently save as zero, which would remove the whole margin.
const DEFAULT_MINIMUM_FREE_SPACE_MB = 100

function updateField(field: keyof ApplicationSettings, value: unknown) {
  const payload = { ...(props.settings || {}), [field]: value } as Partial<ApplicationSettings>
  emit('update:settings', payload)
}

function updateMinimumFreeSpace(event: Event) {
  const raw = (event.target as HTMLInputElement).value
  const parsed = Number(raw)
  const value = raw.trim() === '' || Number.isNaN(parsed) ? DEFAULT_MINIMUM_FREE_SPACE_MB : parsed
  updateField('minimumFreeSpaceWhenImporting', Math.min(1048576, Math.max(0, Math.round(value))))
}

function updateSkipFreeSpaceCheck(value: boolean) {
  updateField('skipFreeSpaceCheckWhenImporting', value)
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

.form-group input[type='number'],
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
</style>
