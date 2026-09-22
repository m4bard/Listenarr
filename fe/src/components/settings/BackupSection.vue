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
    <h3><PhArchive /> Backups</h3>
    <div class="form-body">
      <p class="section-intro">
        A backup of the database and config.json is taken automatically before a new release applies
        any schema change. Take one yourself, and see what exists, under System.
      </p>

      <FormRow
        label="Retention (days)"
        help="How long an automatic backup is kept. Backups you take yourself are never removed. Set to 0 to keep everything."
      >
        <input
          :value="settings.backupRetentionDays ?? DEFAULT_RETENTION_DAYS"
          @input="
            (e) =>
              updateField(
                'backupRetentionDays',
                clampRetention((e.target as HTMLInputElement).value),
              )
          "
          type="number"
          min="0"
          max="365"
        />
      </FormRow>
    </div>
  </div>
</template>

<script setup lang="ts">
import type { ApplicationSettings } from '@/types'
import { PhArchive } from '@phosphor-icons/vue'
import FormRow from '@/components/settings/FormRow.vue'

// Matches the backend default, which follows Readarr, Sonarr and Prowlarr.
const DEFAULT_RETENTION_DAYS = 28
const MAX_RETENTION_DAYS = 365

const props = defineProps<{ settings: Partial<ApplicationSettings> }>()
const emit = defineEmits<{
  'update:settings': [value: Partial<ApplicationSettings>]
}>()

function updateField(field: keyof ApplicationSettings, value: unknown) {
  const payload = { ...(props.settings || {}), [field]: value } as Partial<ApplicationSettings>
  emit('update:settings', payload)
}

function clampRetention(raw: string): number {
  // An empty box means the operator cleared it rather than asking for zero, so it falls back to
  // the default instead of silently switching retention off.
  if (raw.trim() === '') {
    return DEFAULT_RETENTION_DAYS
  }

  const parsed = Number(raw)
  if (!Number.isFinite(parsed)) {
    return DEFAULT_RETENTION_DAYS
  }

  return Math.min(Math.max(Math.trunc(parsed), 0), MAX_RETENTION_DAYS)
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

.section-intro {
  margin: 0 0 1.25rem 0;
  font-size: 0.9rem;
  line-height: 1.5;
  color: #adb5bd;
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

.form-group input:focus,
.form-row-control input:focus {
  outline: none;
  border-color: var(--brand-500);
  box-shadow: 0 0 0 3px rgba(77, 171, 247, 0.08);
}
</style>
