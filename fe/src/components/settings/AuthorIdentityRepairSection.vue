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
    <h3><PhUserCircle /> Author Identity Repair</h3>
    <div class="form-body">
      <FormRow
        label="What this task does when it runs"
        help="Listenarr asks the metadata provider who each stored author is, then compares the answer with what it holds on file. Every other housekeeping task works from data Listenarr already has. This one depends on an outside provider, so it is only as right as the provider's answer."
      >
        <div class="radio-group">
          <RadioCard
            :modelValue="mode"
            @update:modelValue="(value) => selectMode(value as RepairMode)"
            value="off"
            name="authorIdentityRepairMode"
            title="Off"
            description="Nothing is looked up and nothing is changed. Listenarr ships in this state."
          />
          <RadioCard
            :modelValue="mode"
            @update:modelValue="(value) => selectMode(value as RepairMode)"
            value="preview"
            name="authorIdentityRepairMode"
            title="Preview only"
            description="Look up the authors at the head of the queue that already carry an Audible ID, and write what a repair would change to the log. Read it on the Logs page, or under Recent Logs on the System page. Nothing is written, not even a note that the author was checked, so every preview covers the same authors. A preview shows you what a repair would do. It does not survey the library."
          />
          <RadioCard
            :modelValue="mode"
            @update:modelValue="(value) => selectMode(value as RepairMode)"
            :disabled="!canSelectRepair"
            value="repair"
            name="authorIdentityRepairMode"
            title="Repair stored author identities"
            description="Look up the authors at the head of the queue and write the corrections, a batch per run, working through the library over several runs. This replaces or clears the Audible ID, biography and portrait in the author cache, replaces the Audible ID on the authors you monitor, and rewrites the author credits stored against your books. Listenarr keeps no copy of what those fields held before. Read a preview first."
          >
            <span v-if="!canSelectRepair" class="locked-hint">
              Not selectable yet. Tick the box below to make it selectable.
            </span>
          </RadioCard>
        </div>
      </FormRow>

      <CheckboxCard
        v-if="mode !== 'repair'"
        :modelValue="repairUnlocked"
        @update:modelValue="updateRepairUnlocked"
        title="Unlock the Repair option"
        description="Repair rewrites stored author records, so selecting it takes two steps. Ticking this box changes nothing on its own. It makes the Repair option above selectable."
      />

      <FormRow
        label="Run Every (hours)"
        help="How long Listenarr waits between runs (1 to 168). A repair run stops at the limit below and the authors it did not reach wait for the next run. A preview run does not move through the queue at all."
      >
        <input
          :value="numericValue('authorIdentityRepairIntervalHours')"
          @input="(e) => updateNumericField('authorIdentityRepairIntervalHours', e)"
          :disabled="mode === 'off'"
          type="number"
          min="1"
          max="168"
        />
      </FormRow>

      <FormRow
        label="Authors Per Run"
        help="How many stored authors a single run looks up (1 to 500). Every lookup spends from the same hourly provider budget the metadata refresh uses, so keep this low if refreshes are falling behind."
      >
        <input
          :value="numericValue('authorIdentityRepairMaxRowsPerRun')"
          @input="(e) => updateNumericField('authorIdentityRepairMaxRowsPerRun', e)"
          :disabled="mode === 'off'"
          type="number"
          min="1"
          max="500"
        />
      </FormRow>

      <FormRow
        label="Recheck Authors After (days)"
        help="How long Listenarr leaves an author alone once it has checked them (0 to 3650). Zero rechecks every author on every run."
      >
        <input
          :value="numericValue('authorIdentityRepairRecheckAfterDays')"
          @input="(e) => updateNumericField('authorIdentityRepairRecheckAfterDays', e)"
          :disabled="mode === 'off'"
          type="number"
          min="0"
          max="3650"
        />
      </FormRow>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import type { ApplicationSettings } from '@/types'
import { PhUserCircle } from '@phosphor-icons/vue'
import FormRow from '@/components/settings/FormRow.vue'
import CheckboxCard from '@/components/settings/CheckboxCard.vue'
import RadioCard from '@/components/settings/RadioCard.vue'

const props = defineProps<{ settings: Partial<ApplicationSettings> }>()
const emit = defineEmits<{
  'update:settings': [value: Partial<ApplicationSettings>]
}>()

type RepairMode = 'off' | 'preview' | 'repair'

type NumericField =
  | 'authorIdentityRepairIntervalHours'
  | 'authorIdentityRepairMaxRowsPerRun'
  | 'authorIdentityRepairRecheckAfterDays'

// The bounds AuthorIdentityRepairOptionsLoader clamps to when it reads a value, and the shipped
// defaults from ApplicationSettings. GET /settings hands back whatever was stored rather than
// what the pass will use, so a row holding an interval of 9999 would otherwise be displayed as
// 9999 while the pass ran hourly at 168.
//
// Three places have to agree for that to be fixed rather than moved: what is displayed, what is
// written, and the row itself. Displaying the clamp alone leaves the row saying 9999 for ever,
// because the section only emits a field the operator typed in and the save posts the settings
// object verbatim, so the number on screen becomes a claim about the row that nothing reconciles.
// So an out-of-range row is corrected once on mount and the next save carries the correction.
const BOUNDS: Record<NumericField, { min: number; max: number }> = {
  authorIdentityRepairIntervalHours: { min: 1, max: 168 },
  authorIdentityRepairMaxRowsPerRun: { min: 1, max: 500 },
  authorIdentityRepairRecheckAfterDays: { min: 0, max: 3650 },
}

const DEFAULTS: Record<NumericField, number> = {
  authorIdentityRepairIntervalHours: 24,
  authorIdentityRepairMaxRowsPerRun: 25,
  authorIdentityRepairRecheckAfterDays: 30,
}

// Two stored switches, three states an operator can be in. Enabled decides whether the pass runs
// at all, and DryRun decides whether a run that happens writes anything, so "enabled false,
// dry run false" is a fourth stored combination with the same behaviour as off. Reading it as
// off is right; leaving it stored that way is not, because the next thing to set Enabled lands
// straight in repair. Every mode change below therefore writes both switches.
const MODE_SETTINGS: Record<RepairMode, { enabled: boolean; dryRun: boolean }> = {
  off: { enabled: false, dryRun: true },
  preview: { enabled: true, dryRun: true },
  repair: { enabled: true, dryRun: false },
}

const mode = computed<RepairMode>(() => {
  if (!props.settings?.authorIdentityRepairEnabled) return 'off'
  // An install whose settings row predates the column sends nothing for it, and the column
  // ships true, so anything other than an explicit false is a preview.
  return props.settings?.authorIdentityRepairDryRun === false ? 'repair' : 'preview'
})

// Local to the section and deliberately not persisted: the operator re-arms on every visit.
const repairUnlocked = ref(false)

const canSelectRepair = computed(() => repairUnlocked.value || mode.value === 'repair')

// Entering repair spends the acknowledgement. Coming back to it later, whether the operator
// left through this section or the settings were reloaded underneath it, costs another one.
watch(mode, (current) => {
  if (current !== 'repair') return
  repairUnlocked.value = false
})

function updateSettings(patch: Partial<ApplicationSettings>) {
  emit('update:settings', { ...(props.settings || {}), ...patch })
}

function updateRepairUnlocked(value: boolean) {
  repairUnlocked.value = value
}

function selectMode(next: RepairMode) {
  // The one click that could rewrite stored author records is the one this refuses. Off and
  // preview are always a single click; repair needs the acknowledgement above first.
  if (next === 'repair' && !canSelectRepair.value) return
  if (next !== 'repair') repairUnlocked.value = false

  const chosen = MODE_SETTINGS[next]
  updateSettings({
    authorIdentityRepairEnabled: chosen.enabled,
    authorIdentityRepairDryRun: chosen.dryRun,
  })
}

function clamp(field: NumericField, value: number) {
  const { min, max } = BOUNDS[field]
  return Math.min(max, Math.max(min, Math.round(value)))
}

function numericValue(field: NumericField) {
  const stored = props.settings?.[field]
  if (typeof stored !== 'number' || Number.isNaN(stored)) return DEFAULTS[field]
  return clamp(field, stored)
}

// Only the fields that are actually out of range, and only when there is one, so an ordinary
// visit to the settings page emits nothing and leaves the page unchanged.
function outOfRangeCorrections(): Partial<ApplicationSettings> {
  const corrections: Record<string, number> = {}
  for (const field of Object.keys(BOUNDS) as NumericField[]) {
    const stored = props.settings?.[field]
    if (typeof stored !== 'number' || Number.isNaN(stored)) continue
    const clamped = clamp(field, stored)
    if (clamped !== stored) corrections[field] = clamped
  }
  return corrections as Partial<ApplicationSettings>
}

onMounted(async () => {
  // After the tab has finished seeding its local copy from the props, not during it. Its sync
  // window is closed on a nextTick callback registered before this component mounted, and a
  // callback registered here therefore runs behind it. Emitting inside the window instead gets
  // the correction applied to the local copy and then dropped on the way up to the view, so the
  // box would show 168 while the save still posted 9999. There is a test at the view level on
  // the posted body, which is the only level that can tell the difference.
  await nextTick()
  const corrections = outOfRangeCorrections()
  if (Object.keys(corrections).length === 0) return
  updateSettings(corrections)
})

function updateNumericField(field: NumericField, event: Event) {
  const raw = (event.target as HTMLInputElement).value
  const parsed = Number(raw)
  // An emptied box falls back to the shipped default rather than to zero, because zero is a
  // legal recheck age and means something quite different from "I cleared the box".
  const value = raw.trim() === '' || Number.isNaN(parsed) ? DEFAULTS[field] : parsed
  updateSettings({ [field]: clamp(field, value) } as Partial<ApplicationSettings>)
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

.radio-group {
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
}

/* RadioCard carries no visuals of its own outside a modal, so the three states get their card
   styling here rather than borrowing the modal stylesheet. */
.radio-group :deep(.radio-label) {
  display: flex;
  align-items: flex-start;
  gap: 0.75rem;
  padding: 0.9rem 0.85rem;
  border: 1px solid #444;
  border-radius: 6px;
  background-color: #1a1a1a;
  cursor: pointer;
  transition: border-color 0.12s ease;
}

.radio-group :deep(.radio-label:hover) {
  border-color: var(--brand-500, #4dabf7);
}

.radio-group :deep(.radio-label.active) {
  border-color: var(--brand-500, #4dabf7);
  box-shadow: 0 0 0 3px rgba(77, 171, 247, 0.08);
}

.radio-group :deep(.radio-label.disabled) {
  opacity: 0.5;
  cursor: not-allowed;
}

.radio-group :deep(.radio-label.disabled:hover) {
  border-color: #444;
}

.radio-group :deep(.radio-title) {
  font-weight: 500;
  color: #fff;
}

.radio-group :deep(.radio-content small) {
  font-size: 0.85rem;
  color: #adb5bd;
  line-height: 1.5;
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

/* FormRow owns .form-row-label and .form-help; they carry its scope id, not this section's, so
   a rule for them here can never match. The card and input rules below are on elements this
   template owns. */
.locked-hint {
  display: block;
  margin-top: 0.35rem;
  font-size: 0.85rem;
  color: #ffd43b;
  line-height: 1.5;
}
</style>
