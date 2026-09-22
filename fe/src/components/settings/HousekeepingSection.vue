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
    <h3><PhBroom /> Housekeeping</h3>
    <div class="form-body">
      <p class="section-intro">
        A daily sweep removes finished job records and stale cache rows older than the retention
        window below. Dry run controls whether that sweep actually deletes anything.
      </p>

      <div class="housekeeping-status" :class="statusClass">
        <PhCheckCircle v-if="statusLevel === 'safe'" />
        <PhInfo v-else-if="statusLevel === 'off'" />
        <PhWarningCircle v-else />
        <span>{{ statusText }}</span>
      </div>

      <CheckboxCard
        :modelValue="dryRun"
        @update:modelValue="updateHousekeepingDryRun"
        title="Dry Run"
        description="On: each sweep only counts and logs what it would remove, and deletes nothing. Off: the next sweep deletes matching rows for real. Ships on, so an upgraded install reports before it deletes anything."
      />

      <FormRow
        label="Retention (days)"
        help="How many days a finished record is kept before the sweep removes it. 0 keeps every record and turns the sweep off entirely, it does not mean unset and it does not mean delete everything now."
      >
        <input
          :value="settings.housekeepingRetentionDays ?? 30"
          @input="
            (e) =>
              updateField(
                'housekeepingRetentionDays',
                Number((e.target as HTMLInputElement).value || 0),
              )
          "
          type="number"
          min="0"
        />
      </FormRow>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { ApplicationSettings } from '@/types'
import { PhBroom, PhCheckCircle, PhInfo, PhWarningCircle } from '@phosphor-icons/vue'
import CheckboxCard from '@/components/settings/CheckboxCard.vue'
import FormRow from '@/components/settings/FormRow.vue'

const props = defineProps<{ settings: Partial<ApplicationSettings> }>()
const emit = defineEmits<{
  'update:settings': [value: Partial<ApplicationSettings>]
}>()

function updateField(field: keyof ApplicationSettings, value: unknown) {
  const payload = { ...(props.settings || {}), [field]: value } as Partial<ApplicationSettings>
  emit('update:settings', payload)
}

function updateHousekeepingDryRun(value: boolean) {
  updateField('housekeepingDryRun', value)
}

// housekeepingDryRun ships true, so an unset value (a settings object loaded before this
// field existed) reads as dry run, the same default the backend applies.
const dryRun = computed(() => props.settings.housekeepingDryRun ?? true)
const retentionDays = computed(() => props.settings.housekeepingRetentionDays ?? 30)

const statusLevel = computed<'safe' | 'off' | 'live'>(() => {
  if (retentionDays.value <= 0) return 'off'
  return dryRun.value ? 'safe' : 'live'
})

const statusClass = computed(() => `status-${statusLevel.value}`)

const statusText = computed(() => {
  if (statusLevel.value === 'off') {
    return 'Retention is 0. The sweep is disabled and nothing is removed, regardless of dry run.'
  }
  if (statusLevel.value === 'safe') {
    return `Reporting only. Records older than ${retentionDays.value} day(s) will be counted and logged, not deleted.`
  }
  return `Deleting for real. Records older than ${retentionDays.value} day(s) are removed on the next sweep.`
})
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
  color: #adb5bd;
  font-size: 0.9rem;
  line-height: 1.5;
}

.housekeeping-status {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  padding: 0.75rem 1rem;
  border-radius: 6px;
  margin-bottom: 1.25rem;
  font-size: 0.9rem;
  line-height: 1.4;
}

.housekeeping-status.status-safe {
  background: rgba(76, 175, 80, 0.1);
  border: 1px solid rgba(76, 175, 80, 0.3);
  color: #81c784;
}

.housekeeping-status.status-off {
  background: rgba(77, 171, 247, 0.1);
  border: 1px solid rgba(77, 171, 247, 0.3);
  color: #4dabf7;
}

.housekeeping-status.status-live {
  background: rgba(244, 67, 54, 0.1);
  border: 1px solid rgba(244, 67, 54, 0.3);
  color: #f44336;
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
