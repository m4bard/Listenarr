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
    <h3><PhArrowsClockwise /> Metadata Refresh</h3>
    <div class="form-body">
      <CheckboxCard
        :modelValue="settings.metadataRefreshEnabled"
        @update:modelValue="updateMetadataRefreshEnabled"
        title="Enable Scheduled Metadata Refresh"
        description="Periodically re-fetch provider metadata for books whose details have gone stale. The refresh overwrites stored title, authors, narrators, genres and series with whatever the provider currently returns."
      />

      <FormRow
        label="Refresh Interval (hours)"
        help="How often the scheduled refresh wakes up and looks for books that are due (1-168 hours). It does not set how often a given book is touched; the staleness age below does that."
      >
        <input
          :value="settings.metadataRefreshIntervalHours ?? DEFAULTS.metadataRefreshIntervalHours"
          @input="(e) => updateNumericField('metadataRefreshIntervalHours', e)"
          :disabled="!settings.metadataRefreshEnabled"
          type="number"
          min="1"
          max="168"
        />
      </FormRow>

      <FormRow
        label="Refresh Books Older Than (days)"
        help="How old a book's provider metadata must be before it is refreshed again (0-3650 days). Zero makes every book due on each pass."
      >
        <input
          :value="settings.metadataRefreshStaleAfterDays ?? DEFAULTS.metadataRefreshStaleAfterDays"
          @input="(e) => updateNumericField('metadataRefreshStaleAfterDays', e)"
          :disabled="!settings.metadataRefreshEnabled"
          type="number"
          min="0"
          max="3650"
        />
      </FormRow>

      <FormRow
        label="Requests Per Hour"
        help="Ceiling on provider requests per hour (1-3600). Lower this if the provider is rate limiting you."
      >
        <input
          :value="
            settings.metadataRefreshRequestsPerHour ?? DEFAULTS.metadataRefreshRequestsPerHour
          "
          @input="(e) => updateNumericField('metadataRefreshRequestsPerHour', e)"
          :disabled="!settings.metadataRefreshEnabled"
          type="number"
          min="1"
          max="3600"
        />
      </FormRow>

      <FormRow
        label="Minimum Request Spacing (ms)"
        help="Smallest gap between two provider requests, in milliseconds (0-60000). Applied on top of the hourly ceiling so a burst cannot spend the whole budget at once."
      >
        <input
          :value="
            settings.metadataRefreshMinimumSpacingMs ?? DEFAULTS.metadataRefreshMinimumSpacingMs
          "
          @input="(e) => updateNumericField('metadataRefreshMinimumSpacingMs', e)"
          :disabled="!settings.metadataRefreshEnabled"
          type="number"
          min="0"
          max="60000"
        />
      </FormRow>
    </div>
  </div>
</template>

<script setup lang="ts">
import type { ApplicationSettings } from '@/types'
import { PhArrowsClockwise } from '@phosphor-icons/vue'
import FormRow from '@/components/settings/FormRow.vue'
import CheckboxCard from '@/components/settings/CheckboxCard.vue'

const props = defineProps<{ settings: Partial<ApplicationSettings> }>()
const emit = defineEmits<{
  'update:settings': [value: Partial<ApplicationSettings>]
}>()

type NumericField =
  | 'metadataRefreshIntervalHours'
  | 'metadataRefreshStaleAfterDays'
  | 'metadataRefreshRequestsPerHour'
  | 'metadataRefreshMinimumSpacingMs'

// The same bounds and the same shipped defaults the server clamps to. A number input's min and
// max are advisory: the browser will not stop a typed or pasted value outside them, and nothing
// between here and the settings row was rejecting one, so an operator could save a spacing of a
// day and watch the walk silently stop.
const BOUNDS: Record<NumericField, { min: number; max: number }> = {
  metadataRefreshIntervalHours: { min: 1, max: 168 },
  metadataRefreshStaleAfterDays: { min: 0, max: 3650 },
  metadataRefreshRequestsPerHour: { min: 1, max: 3600 },
  metadataRefreshMinimumSpacingMs: { min: 0, max: 60000 },
}

const DEFAULTS: Record<NumericField, number> = {
  metadataRefreshIntervalHours: 24,
  metadataRefreshStaleAfterDays: 30,
  metadataRefreshRequestsPerHour: 60,
  metadataRefreshMinimumSpacingMs: 1000,
}

function updateField(field: keyof ApplicationSettings, value: unknown) {
  const payload = { ...(props.settings || {}), [field]: value } as Partial<ApplicationSettings>
  emit('update:settings', payload)
}

function updateNumericField(field: NumericField, event: Event) {
  const raw = (event.target as HTMLInputElement).value
  const parsed = Number(raw)
  // An emptied box, or anything that is not a number, falls back to the shipped default rather
  // than to zero: zero is a legal value for two of these four and means something quite
  // different from "I cleared the box".
  const value = raw.trim() === '' || Number.isNaN(parsed) ? DEFAULTS[field] : parsed
  const { min, max } = BOUNDS[field]
  updateField(field, Math.min(max, Math.max(min, Math.round(value))))
}

function updateMetadataRefreshEnabled(value: boolean) {
  updateField('metadataRefreshEnabled', value)
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

/* Modal-like card and local form styles for Metadata Refresh */
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
