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
  <div class="section scheduled-tasks">
    <div class="section-header">
      <h2>
        <PhListChecks />
        Tasks
      </h2>
      <div class="section-actions">
        <button class="refresh-button" :disabled="loading" @click="refresh">
          <component :is="loading ? PhSpinner : PhArrowClockwise" />
          {{ loading ? 'Refreshing...' : 'Refresh' }}
        </button>
      </div>
    </div>

    <div v-if="loadError" class="error-message" data-testid="tasks-load-error">
      <PhWarning />
      {{ loadError }}
    </div>

    <div v-if="runError" class="error-message" data-testid="tasks-run-error">
      <PhWarning />
      {{ runError }}
    </div>

    <div v-if="runNotice" class="run-notice" data-testid="tasks-run-notice">
      <PhCheckCircle />
      {{ runNotice }}
    </div>

    <LoadingState v-if="loading && tasks.length === 0" message="Loading tasks..." />

    <div v-else-if="tasks.length === 0" class="empty-message">
      <PhInfo />
      <span>No periodic workers are registered</span>
    </div>

    <table v-else class="tasks-table">
      <thead>
        <tr>
          <th>Name</th>
          <th>Interval</th>
          <th>Last run</th>
          <th>Duration</th>
          <th>Last outcome</th>
          <th>Next run</th>
          <th class="run-column"><span class="visually-hidden">Manual run</span></th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="task in tasks"
          :key="task.name"
          :class="{ 'worker-stopped': !task.isRegistered }"
          :data-testid="`task-row-${task.name}`"
        >
          <td class="task-name">{{ task.displayName }}</td>

          <td class="task-interval">
            <span :data-testid="`task-interval-${task.name}`">{{ describeInterval(task) }}</span>
            <span v-if="task.intervalError" class="interval-error" :title="task.intervalError">
              {{ task.intervalError }}
            </span>
          </td>

          <td :title="describeLastRun(task)">{{ relativeOrBlank(task.lastStartedAt) }}</td>

          <td>
            {{
              task.lastDurationSeconds === undefined
                ? ''
                : formatDurationSeconds(task.lastDurationSeconds)
            }}
          </td>

          <td>
            <span :class="['outcome-badge', outcomeClass(task.lastOutcome)]">
              {{ task.lastOutcome }}
            </span>
          </td>

          <td :data-testid="`task-next-${task.name}`">
            <span v-if="!task.isRegistered" class="stopped-note">Worker stopped</span>
            <span v-else-if="task.isRunning" class="running-note">Running now</span>
            <span v-else-if="task.nextExecution" :title="absoluteTime(task.nextExecution)">
              {{ relativeTime(task.nextExecution) }}
            </span>
          </td>

          <td class="run-column">
            <button
              class="run-button"
              :disabled="!canRun(task) || pendingRuns.includes(task.name)"
              :title="describeRunControl(task)"
              :aria-label="describeRunControl(task)"
              :data-testid="`task-run-${task.name}`"
              @click="() => runTask(task)"
            >
              <component :is="pendingRuns.includes(task.name) ? PhSpinner : PhPlay" />
              Run
            </button>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import {
  PhListChecks,
  PhSpinner,
  PhArrowClockwise,
  PhWarning,
  PhCheckCircle,
  PhInfo,
  PhPlay,
} from '@phosphor-icons/vue'
import { LoadingState } from '@/components/base'
import { getScheduledTasks, runScheduledTask } from '@/services/api'
import type { ScheduledTask } from '@/types'
import { logger } from '@/utils/logger'

// Slow enough not to be rude to a server that is already busy running the work it lists,
// fast enough that a cycle which takes a few seconds is seen starting and finishing.
const POLL_INTERVAL_MS = 12000

const SECONDS_PER_MINUTE = 60
const SECONDS_PER_HOUR = 3600
const SECONDS_PER_DAY = 86400

const OUTCOME_CLASSES: Record<string, string> = {
  Succeeded: 'succeeded',
  Failed: 'failed',
  Canceled: 'canceled',
  Unknown: 'unknown',
}

const tasks = ref<ScheduledTask[]>([])
const loading = ref(true)
const loadError = ref<string | null>(null)
const runError = ref<string | null>(null)
const runNotice = ref<string | null>(null)
const pendingRuns = ref<string[]>([])

// Relative times are computed against this rather than against Date.now() at render time,
// so that a row's wording changes when the data does instead of whenever Vue happens to
// re-render it.
const renderedAt = ref(Date.now())

let pollTimer: ReturnType<typeof setInterval> | null = null
let requestInFlight = false

function pluralize(value: number, unit: string): string {
  return `${value} ${unit}${value === 1 ? '' : 's'}`
}

// The largest unit the value fills, to one decimal place when it does not divide evenly.
// A zero is reported as a zero, because a worker that declares a zero interval has said
// something and this surface should not silently turn it into a dash.
function formatSeconds(totalSeconds: number): string {
  for (const [unit, size] of [
    ['day', SECONDS_PER_DAY],
    ['hour', SECONDS_PER_HOUR],
    ['minute', SECONDS_PER_MINUTE],
  ] as const) {
    if (totalSeconds >= size) {
      const value = totalSeconds / size
      return pluralize(Number.isInteger(value) ? value : Number(value.toFixed(1)), unit)
    }
  }

  return pluralize(totalSeconds, 'second')
}

// Whole units only, because "5 minutes ago" reads better than "5.4 minutes ago" and the
// extra digit says nothing a reader of this page wants.
function formatRoughSeconds(totalSeconds: number): string {
  if (totalSeconds >= SECONDS_PER_DAY) {
    return pluralize(Math.floor(totalSeconds / SECONDS_PER_DAY), 'day')
  }

  if (totalSeconds >= SECONDS_PER_HOUR) {
    return pluralize(Math.floor(totalSeconds / SECONDS_PER_HOUR), 'hour')
  }

  if (totalSeconds >= SECONDS_PER_MINUTE) {
    return pluralize(Math.floor(totalSeconds / SECONDS_PER_MINUTE), 'minute')
  }

  return pluralize(totalSeconds, 'second')
}

function formatDurationSeconds(seconds: number): string {
  if (seconds < SECONDS_PER_MINUTE) {
    return pluralize(Number.isInteger(seconds) ? seconds : Number(seconds.toFixed(1)), 'second')
  }

  return formatSeconds(Math.round(seconds))
}

// Null is a value here and not an absence: it means the worker's interval could not be
// stated in whole non-negative seconds, and intervalError carries the reason. Tested for
// explicitly, because `??` and `> 0` cannot tell it apart from a declared zero.
function describeInterval(task: ScheduledTask): string {
  return task.intervalSeconds === null ? '-' : formatSeconds(task.intervalSeconds)
}

function absoluteTime(isoDate: string): string {
  const parsed = new Date(isoDate)
  return Number.isNaN(parsed.getTime()) ? isoDate : parsed.toLocaleString()
}

function relativeTime(isoDate: string): string {
  const parsed = new Date(isoDate).getTime()
  if (Number.isNaN(parsed)) {
    return isoDate
  }

  const deltaSeconds = Math.round((parsed - renderedAt.value) / 1000)
  const magnitude = Math.abs(deltaSeconds)

  if (magnitude < 5) {
    return deltaSeconds < 0 ? 'just now' : 'in a moment'
  }

  return deltaSeconds < 0
    ? `${formatRoughSeconds(magnitude)} ago`
    : `in ${formatRoughSeconds(magnitude)}`
}

function relativeOrBlank(isoDate: string | undefined): string {
  return isoDate === undefined ? '' : relativeTime(isoDate)
}

function describeLastRun(task: ScheduledTask): string {
  if (task.lastStartedAt === undefined) {
    return 'This task has not run since the host started'
  }

  const absolute = absoluteTime(task.lastStartedAt)
  if (task.lastTrigger === 'Manual') {
    return `${absolute}, started on demand`
  }

  if (task.lastTrigger === 'Scheduled') {
    return `${absolute}, started on its schedule`
  }

  return absolute
}

function outcomeClass(outcome: string): string {
  return OUTCOME_CLASSES[outcome] ?? 'unknown'
}

// The three refusals the run endpoint can answer with, decided here from the row rather
// than discovered by pressing the button. The stopped worker is tested first because it
// answers 409 whatever the allowlist says.
function canRun(task: ScheduledTask): boolean {
  return task.isRegistered && task.isManualRunAllowed && !task.isRunning
}

function describeRunControl(task: ScheduledTask): string {
  if (!task.isRegistered) {
    return `${task.displayName} is listed but its worker has stopped, so a cycle cannot be started`
  }

  if (!task.isManualRunAllowed) {
    return `${task.displayName} runs on its schedule only and cannot be started on demand`
  }

  if (task.isRunning) {
    return `${task.displayName} is already running`
  }

  return `Run ${task.displayName} now`
}

function describeApiError(error: unknown, fallback: string): string {
  const candidate = error as (Error & { body?: string }) | null

  if (typeof candidate?.body === 'string' && candidate.body.length > 0) {
    try {
      const parsed = JSON.parse(candidate.body) as { error?: unknown }
      if (typeof parsed.error === 'string' && parsed.error.trim().length > 0) {
        return parsed.error
      }
    } catch {
      // The body was not JSON, so there is no sentence in it to show. The caller's own
      // wording beats putting a raw response body on the page.
    }

    return fallback
  }

  return typeof candidate?.message === 'string' && candidate.message.length > 0
    ? candidate.message
    : fallback
}

async function loadTasks(silent = false): Promise<void> {
  // The poll and the refresh button share this, so a slow response cannot stack a second
  // request on top of the first.
  if (requestInFlight) {
    return
  }

  requestInFlight = true
  if (!silent) {
    loading.value = true
  }

  try {
    tasks.value = await getScheduledTasks()
    renderedAt.value = Date.now()
    loadError.value = null
  } catch (error) {
    logger.error('Error loading scheduled tasks:', error)
    loadError.value = describeApiError(error, 'Failed to load tasks')
  } finally {
    requestInFlight = false
    loading.value = false
  }
}

async function refresh(): Promise<void> {
  await loadTasks()
}

async function runTask(task: ScheduledTask): Promise<void> {
  if (!canRun(task) || pendingRuns.value.includes(task.name)) {
    return
  }

  pendingRuns.value = [...pendingRuns.value, task.name]
  runError.value = null
  runNotice.value = null

  try {
    const result = await runScheduledTask(task.name)

    // The two accepted outcomes come back with identical rows, so the wording has to come
    // from the trigger result rather than from anything on the task.
    runNotice.value =
      result.triggered === 'already-running'
        ? `${task.displayName} was already running, so nothing further was queued.`
        : `${task.displayName} started.`

    await loadTasks(true)
  } catch (error) {
    logger.error('Error running scheduled task:', error)
    runError.value = describeApiError(error, `Could not start ${task.displayName}.`)
  } finally {
    pendingRuns.value = pendingRuns.value.filter((name) => name !== task.name)
  }
}

onMounted(() => {
  loadTasks()
  pollTimer = setInterval(() => {
    loadTasks(true)
  }, POLL_INTERVAL_MS)
})

onUnmounted(() => {
  if (pollTimer !== null) {
    clearInterval(pollTimer)
    pollTimer = null
  }
})
</script>

<style scoped>
.section {
  background: #232323;
  border: 1px solid #333;
  border-radius: 8px;
  padding: 1.5rem;
  box-shadow: 0 6px 18px rgba(0, 0, 0, 0.25);
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1.5rem;
  padding-bottom: 1rem;
  border-bottom: 1px solid #333;
}

.section-header h2 {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin: 0;
  color: #fff;
  font-size: 1.3rem;
  font-weight: 500;
}

.section-actions {
  display: flex;
  gap: 0.5rem;
}

.refresh-button {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.45rem 0.9rem;
  background: transparent;
  color: #ccc;
  border: 1px solid #444;
  border-radius: 6px;
  cursor: pointer;
  font-weight: 500;
  font-size: 0.85rem;
  transition: all 0.2s;
}

.refresh-button:hover:not(:disabled) {
  background: #2a2a2a;
  color: #fff;
}

.refresh-button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.error-message {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 1rem;
  background: rgba(231, 76, 60, 0.1);
  border: 1px solid rgba(231, 76, 60, 0.3);
  border-radius: 6px;
  color: #e74c3c;
  margin-bottom: 1rem;
}

.run-notice {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 1rem;
  background: rgba(39, 174, 96, 0.1);
  border: 1px solid rgba(39, 174, 96, 0.3);
  border-radius: 6px;
  color: #27ae60;
  margin-bottom: 1rem;
}

.empty-message {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 1rem;
  background: #252525;
  border-radius: 6px;
  color: #999;
  font-style: italic;
}

.tasks-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9rem;
}

.tasks-table th {
  text-align: left;
  padding: 0.6rem 0.75rem;
  color: #999;
  font-weight: 500;
  font-size: 0.8rem;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  border-bottom: 1px solid #333;
}

.tasks-table td {
  padding: 0.75rem;
  color: #ccc;
  border-bottom: 1px solid #2a2a2a;
  vertical-align: top;
}

.tasks-table tbody tr:last-child td {
  border-bottom: none;
}

.tasks-table tbody tr:hover {
  background: #2a2a2a;
}

.task-name {
  color: #fff;
  font-weight: 500;
}

.interval-error {
  display: block;
  margin-top: 0.35rem;
  max-width: 28rem;
  color: #f39c12;
  font-size: 0.8rem;
  line-height: 1.4;
}

.worker-stopped td {
  background: rgba(231, 76, 60, 0.08);
}

.stopped-note {
  color: #e74c3c;
  font-weight: 500;
}

.running-note {
  color: var(--brand-500);
  font-weight: 500;
}

.outcome-badge {
  display: inline-block;
  padding: 0.2rem 0.6rem;
  border-radius: 6px;
  font-size: 0.75rem;
  font-weight: 500;
}

.outcome-badge.succeeded {
  background: rgba(39, 174, 96, 0.15);
  color: #27ae60;
  border: 1px solid rgba(39, 174, 96, 0.3);
}

.outcome-badge.failed {
  background: rgba(231, 76, 60, 0.15);
  color: #e74c3c;
  border: 1px solid rgba(231, 76, 60, 0.3);
}

.outcome-badge.canceled {
  background: rgba(243, 156, 18, 0.15);
  color: #f39c12;
  border: 1px solid rgba(243, 156, 18, 0.3);
}

.outcome-badge.unknown {
  background: #2a2a2a;
  color: #999;
  border: 1px solid #3a3a3a;
}

.run-column {
  width: 1%;
  white-space: nowrap;
  text-align: right;
}

.run-button {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.4rem 0.8rem;
  background: transparent;
  color: var(--brand-500);
  border: 1px solid #444;
  border-radius: 6px;
  cursor: pointer;
  font-weight: 500;
  font-size: 0.85rem;
  transition: all 0.2s;
}

.run-button:hover:not(:disabled) {
  background: #2a2a2a;
  border-color: var(--brand-500);
}

.run-button:disabled {
  opacity: 0.45;
  cursor: not-allowed;
  color: #999;
}

.visually-hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
}

@media (max-width: 768px) {
  .tasks-table {
    display: block;
    overflow-x: auto;
  }

  .interval-error {
    max-width: 16rem;
  }
}
</style>
