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
  <div class="history-view">
    <div class="page-header">
      <div class="header-content">
        <h1><PhClockCounterClockwise /> History</h1>
        <p>Every action Listenarr has recorded, newest first</p>
      </div>
      <div class="header-actions">
        <button class="action-button btn" :disabled="loading" @click="reload">
          <PhSpinner v-if="loading" class="ph-spin" />
          <PhArrowClockwise v-else />
          {{ loading ? 'Refreshing...' : 'Refresh' }}
        </button>
        <button
          class="action-button danger btn"
          :disabled="total === 0 || adminRefused"
          data-testid="clear-all"
          @click="showClearConfirm = true"
        >
          <PhTrash />
          Clear all {{ total }} entries
        </button>
      </div>
    </div>

    <div class="filters-section">
      <div class="filter-group">
        <label for="history-event-filter">Event</label>
        <select id="history-event-filter" v-model="presetId" @change="applyFilters">
          <option v-for="preset in presets" :key="preset.id" :value="preset.id">
            {{ preset.label }}
          </option>
        </select>
      </div>
      <div class="filter-group">
        <label for="history-outcome-filter">Outcome</label>
        <select id="history-outcome-filter" v-model="outcome" @change="applyFilters">
          <option value="">All outcomes</option>
          <option v-for="value in outcomes" :key="value" :value="value">{{ value }}</option>
        </select>
      </div>
      <div class="filter-group">
        <label for="history-range-filter">Time range</label>
        <select id="history-range-filter" v-model="rangeId" @change="applyFilters">
          <option v-for="range in ranges" :key="range.id" :value="range.id">
            {{ range.label }}
          </option>
        </select>
      </div>
    </div>

    <div v-if="adminMessage" class="admin-message" data-testid="admin-message">
      <PhLockKey />
      <span>{{ adminMessage }}</span>
    </div>

    <!--
      A failed fetch and an empty table have to look different. Rendering "No history" when the
      request threw makes a broken page indistinguishable from a correct one, and a fresh
      instance has an empty table anyway, so that is exactly where the confusion would land.
    -->
    <div v-if="error" class="error-message" data-testid="history-error">
      <PhWarningCircle />
      <div>
        <strong>Could not load history</strong>
        <p>{{ error }}</p>
      </div>
    </div>

    <LoadingState v-else-if="loading && entries.length === 0" message="Loading history..." />

    <div v-else-if="entries.length > 0" class="history-wrapper">
      <div class="history-table" role="table">
        <div class="history-head" role="row">
          <button
            v-for="column in sortableColumns"
            :key="column.key"
            class="col-head sortable"
            :class="[column.key, { active: sortBy === column.key }]"
            role="columnheader"
            :data-testid="`sort-${column.key}`"
            @click="toggleSort(column.key)"
          >
            {{ column.label }}
            <PhCaretUp v-if="sortBy === column.key && sortDirection === 'asc'" />
            <PhCaretDown v-else-if="sortBy === column.key" />
          </button>
          <span class="col-head audiobook" role="columnheader">Audiobook</span>
          <span class="col-head source-title" role="columnheader">Release</span>
          <span class="col-head actions" role="columnheader">
            <span class="sr-only">Actions</span>
          </span>
        </div>

        <template v-for="entry in entries" :key="entry.id">
          <div class="history-row" role="row" :data-testid="`history-row-${entry.id}`">
            <span class="cell event" role="cell">
              <span class="event-icon" :class="getEventTypeClass(entry.eventType)">
                <component :is="getEventIconComponent(entry.eventType)" />
              </span>
              <span class="event-label">{{ formatEventTitle(entry.eventType) }}</span>
              <Pill v-if="entry.notificationSent" variant="count">Notified</Pill>
            </span>
            <span class="cell outcome" role="cell">
              <span class="outcome-pill" :class="`outcome-${(entry.outcome || '').toLowerCase()}`">
                {{ entry.outcome || 'Unknown' }}
              </span>
            </span>
            <span class="cell source" role="cell">{{ entry.source || '—' }}</span>
            <span class="cell date" role="cell" :title="absoluteTime(entry.timestamp)">
              {{ relativeTime(entry.timestamp) }}
            </span>
            <span class="cell audiobook" role="cell">
              <!--
                AudiobookId is null for everything written through the download path, so the
                link is conditional on the id rather than on the title. A row with a title and
                no id renders as text; that is the normal case, not an error.
              -->
              <RouterLink
                v-if="entry.audiobookId"
                :to="`/audiobooks/${entry.audiobookId}`"
                class="audiobook-link"
              >
                {{ entry.audiobookTitle || `Audiobook ${entry.audiobookId}` }}
              </RouterLink>
              <span v-else-if="entry.audiobookTitle">{{ entry.audiobookTitle }}</span>
              <span v-else class="muted">—</span>
            </span>
            <span class="cell source-title" role="cell" :title="entry.sourceTitle || ''">
              {{ entry.sourceTitle || '—' }}
            </span>
            <span class="cell actions" role="cell">
              <button
                class="btn-icon"
                title="Show details"
                :data-testid="`expand-${entry.id}`"
                :aria-expanded="expandedId === entry.id"
                @click="toggleExpanded(entry.id)"
              >
                <PhCaretUp v-if="expandedId === entry.id" />
                <PhCaretDown v-else />
              </button>
              <button
                class="btn-icon danger"
                title="Delete this entry"
                :disabled="adminRefused"
                :data-testid="`delete-${entry.id}`"
                @click="askDelete(entry)"
              >
                <PhTrash />
              </button>
            </span>
          </div>

          <div
            v-if="expandedId === entry.id"
            class="history-details"
            :data-testid="`details-${entry.id}`"
          >
            <LoadingState v-if="detailsLoading" message="Loading details..." :size="20" />
            <div v-else-if="detailsError" class="error-message" data-testid="details-error">
              <PhWarningCircle />
              <div>
                <strong>Could not load details</strong>
                <p>{{ detailsError }}</p>
              </div>
            </div>
            <template v-else-if="details">
              <!--
                One row with nothing correlated to it is the common case for library events,
                which mostly take the entity's default correlation id and so correlate with
                nothing. Drawing timeline chrome around a single entry would suggest a chain
                that does not exist.
              -->
              <div v-if="details.related.length > 1" class="attempt-chain">
                <h4>Attempt chain</h4>
                <div
                  v-for="step in details.related"
                  :key="step.id"
                  class="chain-step"
                  :class="{ current: step.id === entry.id }"
                >
                  <span class="event-icon small" :class="getEventTypeClass(step.eventType)">
                    <component :is="getEventIconComponent(step.eventType)" />
                  </span>
                  <span class="chain-label">{{ formatEventTitle(step.eventType) }}</span>
                  <span
                    class="outcome-pill"
                    :class="`outcome-${(step.outcome || '').toLowerCase()}`"
                  >
                    {{ step.outcome || 'Unknown' }}
                  </span>
                  <span class="chain-time" :title="absoluteTime(step.timestamp)">
                    {{ relativeTime(step.timestamp) }}
                  </span>
                  <span class="chain-message">{{ step.message || '' }}</span>
                </div>
              </div>

              <dl class="detail-list">
                <template v-if="details.entry.message">
                  <dt>Message</dt>
                  <dd>{{ details.entry.message }}</dd>
                </template>
                <template
                  v-if="details.entry.error && details.entry.error !== details.entry.message"
                >
                  <dt>Error</dt>
                  <dd class="error-text">{{ details.entry.error }}</dd>
                </template>
                <template v-if="details.entry.sourceTitle">
                  <dt>Release</dt>
                  <dd>{{ details.entry.sourceTitle }}</dd>
                </template>
                <template v-if="details.entry.indexer">
                  <dt>Indexer</dt>
                  <dd>{{ details.entry.indexer }}</dd>
                </template>
                <template v-if="details.entry.quality">
                  <dt>Quality</dt>
                  <dd>{{ details.entry.quality }}</dd>
                </template>
                <template v-if="details.entry.size">
                  <dt>Size</dt>
                  <dd>{{ formatSize(details.entry.size) }}</dd>
                </template>
                <template v-if="details.entry.downloadId">
                  <dt>Download</dt>
                  <dd class="identifier">{{ details.entry.downloadId }}</dd>
                </template>
                <template v-if="details.entry.downloadClientId">
                  <dt>Download client</dt>
                  <dd class="identifier">{{ details.entry.downloadClientId }}</dd>
                </template>
              </dl>

              <!--
                The raw payload is written by several producers with no shared schema, and it
                can carry absolute paths and raw exception text straight from the file layer
                (upstream #975, which is about the API returning them at all rather than about
                this view). It is not a field to put in front of someone who did not ask for it,
                so it stays behind its own toggle rather than rendering with the rest.
              -->
              <div v-if="details.entry.data" class="raw-data">
                <button
                  class="raw-toggle"
                  data-testid="raw-toggle"
                  :aria-expanded="rawVisible"
                  @click="rawVisible = !rawVisible"
                >
                  <PhCaretUp v-if="rawVisible" />
                  <PhCaretDown v-else />
                  {{ rawVisible ? 'Hide raw event data' : 'Show raw event data' }}
                </button>
                <pre v-if="rawVisible" data-testid="raw-data">{{
                  prettyJson(details.entry.data)
                }}</pre>
              </div>
            </template>
          </div>
        </template>
      </div>

      <div class="pagination">
        <div class="pagination-info" data-testid="pagination-info">
          Showing {{ firstShown }} - {{ lastShown }} of {{ total }} entries
        </div>
        <div class="pagination-controls">
          <button
            class="page-button"
            title="Previous page"
            data-testid="page-prev"
            :disabled="currentPage === 1"
            @click="goToPage(currentPage - 1)"
          >
            <PhCaretLeft />
          </button>
          <button
            v-for="page in visiblePages"
            :key="page"
            class="page-button"
            :class="{ active: page === currentPage }"
            :data-testid="`page-${page}`"
            @click="goToPage(page)"
          >
            {{ page }}
          </button>
          <button
            class="page-button"
            title="Next page"
            data-testid="page-next"
            :disabled="currentPage >= totalPages"
            @click="goToPage(currentPage + 1)"
          >
            <PhCaretRight />
          </button>
        </div>
        <div class="pagination-size">
          <label for="history-page-size">Per page:</label>
          <select id="history-page-size" v-model.number="pageSize" @change="changePageSize">
            <option :value="25">25</option>
            <option :value="50">50</option>
            <option :value="100">100</option>
          </select>
        </div>
      </div>
    </div>

    <EmptyState
      v-else
      data-testid="history-empty"
      title="No history yet"
      :message="
        filtersActive
          ? 'No events match these filters'
          : 'Listenarr records grabs, imports, scans and library changes here'
      "
    >
      <template #icon>
        <PhClockCounterClockwise :size="48" />
      </template>
    </EmptyState>

    <ConfirmModal
      :visible="pendingDelete !== null"
      title="Delete history entry"
      :message="`Delete the ${pendingDelete ? formatEventTitle(pendingDelete.eventType) : ''} entry? This cannot be undone.`"
      confirmLabel="Delete"
      :confirming="deleting"
      @confirm="confirmDelete"
      @cancel="pendingDelete = null"
    />

    <ConfirmModal
      :visible="showClearConfirm"
      title="Clear all history"
      :message="`Delete all ${total} history entries? This cannot be undone and affects every audiobook, not just what is shown.`"
      confirmLabel="Clear all"
      :confirming="clearing"
      @confirm="confirmClearAll"
      @cancel="showClearConfirm = false"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { RouterLink } from 'vue-router'
import {
  PhArrowClockwise,
  PhCaretDown,
  PhCaretLeft,
  PhCaretRight,
  PhCaretUp,
  PhClockCounterClockwise,
  PhLockKey,
  PhSpinner,
  PhTrash,
  PhWarningCircle,
} from '@phosphor-icons/vue'
import { EmptyState, LoadingState, Pill } from '@/components/base'
import { ConfirmModal } from '@/components/feedback'
import { errorTracking } from '@/services/errorTracking'
import { apiService } from '@/services/api'
import {
  HISTORY_EVENT_PRESETS,
  findEventPreset,
  formatEventTitle,
  getEventIconComponent,
  getEventTypeClass,
} from '@/utils/historyEvents'
import { HISTORY_OUTCOMES } from '@/types'
import type { History, HistoryDetails, HistoryOutcome, HistorySortKey } from '@/types'

const presets = HISTORY_EVENT_PRESETS
const outcomes = HISTORY_OUTCOMES

const ranges = [
  { id: 'all', label: 'All time', hours: 0 },
  { id: '24h', label: 'Last 24 hours', hours: 24 },
  { id: '7d', label: 'Last 7 days', hours: 24 * 7 },
  { id: '30d', label: 'Last 30 days', hours: 24 * 30 },
] as const

const sortableColumns: { key: HistorySortKey; label: string }[] = [
  { key: 'eventType', label: 'Event' },
  { key: 'outcome', label: 'Outcome' },
  { key: 'source', label: 'Source' },
  { key: 'timestamp', label: 'Date' },
]

const entries = ref<History[]>([])
const total = ref(0)
const loading = ref(true)
const error = ref<string | null>(null)

const presetId = ref('all')
const outcome = ref<HistoryOutcome | ''>('')
const rangeId = ref<string>('all')
const sortBy = ref<HistorySortKey>('timestamp')
const sortDirection = ref<'asc' | 'desc'>('desc')
const currentPage = ref(1)
const pageSize = ref(25)

const expandedId = ref<number | null>(null)
const details = ref<HistoryDetails | null>(null)
const detailsLoading = ref(false)
const detailsError = ref<string | null>(null)
const rawVisible = ref(false)
const detailsCache = new Map<number, HistoryDetails>()

const pendingDelete = ref<History | null>(null)
const deleting = ref(false)
const showClearConfirm = ref(false)
const clearing = ref(false)

/*
 * The destructive endpoints are the only ones in the codebase behind
 * RequireAdministratorSession, and the auth store carries no role, so there is no way to know
 * in advance whether this session may use them. The controls are rendered and the refusal is
 * handled: after one 401 or 403 they are disabled, so nobody is invited to fail twice.
 */
const adminRefused = ref(false)
const adminMessage = ref<string | null>(null)

const totalPages = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)))
const firstShown = computed(() =>
  total.value === 0 ? 0 : (currentPage.value - 1) * pageSize.value + 1,
)
const lastShown = computed(() => Math.min(currentPage.value * pageSize.value, total.value))

const filtersActive = computed(
  () => presetId.value !== 'all' || outcome.value !== '' || rangeId.value !== 'all',
)

const visiblePages = computed(() => {
  const pages: number[] = []
  const maxVisible = 5
  let start = Math.max(1, currentPage.value - Math.floor(maxVisible / 2))
  const end = Math.min(totalPages.value, start + maxVisible - 1)
  if (end - start < maxVisible - 1) {
    start = Math.max(1, end - maxVisible + 1)
  }
  for (let page = start; page <= end; page++) {
    pages.push(page)
  }
  return pages
})

function rangeStart(): string | undefined {
  const range = ranges.find((candidate) => candidate.id === rangeId.value)
  if (!range || range.hours === 0) return undefined
  return new Date(Date.now() - range.hours * 3600_000).toISOString()
}

async function loadHistory() {
  loading.value = true
  error.value = null

  const preset = findEventPreset(presetId.value)
  try {
    // The whole preset goes in one request. The server matches any of a comma-separated list,
    // so total stays the count of the filtered set and the pager below stays honest.
    const page = await apiService.getHistory({
      limit: pageSize.value,
      offset: (currentPage.value - 1) * pageSize.value,
      sortBy: sortBy.value,
      sortDirection: sortDirection.value,
      eventType: preset.eventTypes.length > 0 ? preset.eventTypes.join(',') : undefined,
      outcome: outcome.value || undefined,
      from: rangeStart(),
    })
    entries.value = page.history ?? []
    // Derived from the response, never from the length of the returned array: with server-side
    // paging those differ by construction and the array would give a pager of exactly one page.
    total.value = page.total ?? 0
  } catch (err) {
    entries.value = []
    total.value = 0
    error.value = err instanceof Error ? err.message : 'Failed to load history'
    errorTracking.captureException(err as Error, {
      component: 'HistoryView',
      operation: 'loadHistory',
    })
  } finally {
    loading.value = false
  }
}

function collapseDetails() {
  expandedId.value = null
  details.value = null
  detailsError.value = null
  rawVisible.value = false
}

// Every filter change resets to the first page. Without this the next request asks for page 4
// of a result set that no longer exists, and the pager happily labels it page 4 of the new one.
function applyFilters() {
  currentPage.value = 1
  collapseDetails()
  void loadHistory()
}

function reload() {
  collapseDetails()
  detailsCache.clear()
  void loadHistory()
}

function toggleSort(key: HistorySortKey) {
  if (sortBy.value === key) {
    sortDirection.value = sortDirection.value === 'asc' ? 'desc' : 'asc'
  } else {
    sortBy.value = key
    sortDirection.value = 'desc'
  }
  applyFilters()
}

function goToPage(page: number) {
  if (page < 1 || page > totalPages.value || page === currentPage.value) return
  currentPage.value = page
  collapseDetails()
  void loadHistory()
}

function changePageSize() {
  currentPage.value = 1
  collapseDetails()
  void loadHistory()
}

async function toggleExpanded(id: number) {
  if (expandedId.value === id) {
    collapseDetails()
    return
  }

  expandedId.value = id
  detailsError.value = null
  rawVisible.value = false

  const cached = detailsCache.get(id)
  if (cached) {
    details.value = cached
    return
  }

  details.value = null
  detailsLoading.value = true
  try {
    const loaded = await apiService.getHistoryDetails(id)
    detailsCache.set(id, loaded)
    if (expandedId.value === id) details.value = loaded
  } catch (err) {
    detailsError.value = err instanceof Error ? err.message : 'Failed to load details'
  } finally {
    detailsLoading.value = false
  }
}

function noteAdminRefusal(err: unknown): boolean {
  const status = (err as { status?: number } | null)?.status
  if (status !== 401 && status !== 403) return false
  adminRefused.value = true
  adminMessage.value =
    'History deletion requires an administrator session. Sign in as an administrator to remove entries.'
  return true
}

function askDelete(entry: History) {
  pendingDelete.value = entry
}

async function confirmDelete() {
  const entry = pendingDelete.value
  if (!entry) return

  deleting.value = true
  try {
    await apiService.deleteHistoryEntry(entry.id)
    entries.value = entries.value.filter((candidate) => candidate.id !== entry.id)
    total.value = Math.max(0, total.value - 1)
    detailsCache.delete(entry.id)
    if (expandedId.value === entry.id) collapseDetails()
    pendingDelete.value = null
  } catch (err) {
    pendingDelete.value = null
    if (!noteAdminRefusal(err)) {
      adminMessage.value = err instanceof Error ? err.message : 'Failed to delete the entry'
    }
  } finally {
    deleting.value = false
  }
}

async function confirmClearAll() {
  clearing.value = true
  try {
    await apiService.clearAllHistory()
    showClearConfirm.value = false
    currentPage.value = 1
    collapseDetails()
    detailsCache.clear()
    await loadHistory()
  } catch (err) {
    showClearConfirm.value = false
    if (!noteAdminRefusal(err)) {
      adminMessage.value = err instanceof Error ? err.message : 'Failed to clear history'
    }
  } finally {
    clearing.value = false
  }
}

function relativeTime(timestamp: string): string {
  const then = new Date(timestamp).getTime()
  if (Number.isNaN(then)) return 'Unknown'
  const minutes = Math.floor((Date.now() - then) / 60000)
  if (minutes < 1) return 'Just now'
  if (minutes < 60) return `${minutes} minute${minutes === 1 ? '' : 's'} ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours} hour${hours === 1 ? '' : 's'} ago`
  const days = Math.floor(hours / 24)
  if (days < 7) return `${days} day${days === 1 ? '' : 's'} ago`
  return new Date(timestamp).toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

function absoluteTime(timestamp: string): string {
  const date = new Date(timestamp)
  return Number.isNaN(date.getTime()) ? '' : date.toLocaleString()
}

function formatSize(bytes: number): string {
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let value = bytes
  let unit = 0
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024
    unit++
  }
  return `${value.toFixed(unit === 0 ? 0 : 1)} ${units[unit]}`
}

function prettyJson(raw: string): string {
  try {
    return JSON.stringify(JSON.parse(raw), null, 2)
  } catch {
    // Several producers write this field and they share no schema, so anything that is not
    // JSON is shown as it was stored rather than swallowed.
    return raw
  }
}

onMounted(() => {
  void loadHistory()
})
</script>

<style scoped>
.history-view {
  padding: 2rem;
  max-width: 1600px;
  margin: 0 auto;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 2rem;
  gap: 2rem;
}

.header-content h1 {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin: 0 0 0.5rem 0;
  color: #fff;
  font-size: 2rem;
  font-weight: 500;
}

.header-content p {
  color: #999;
  font-size: 1rem;
  margin: 0;
}

.header-actions {
  display: flex;
  gap: 0.75rem;
}

.action-button {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.65rem 1.25rem;
  background: #2a2a2a;
  color: #fff;
  border: 1px solid #444;
  border-radius: 6px;
  font-size: 0.95rem;
  cursor: pointer;
  transition: all 0.2s;
}

.action-button:hover:not(:disabled) {
  background: #333;
  border-color: var(--brand-500);
}

.action-button.danger:hover:not(:disabled) {
  border-color: #e74c3c;
  color: #e74c3c;
}

.action-button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.filters-section {
  display: flex;
  gap: 1rem;
  margin-bottom: 1.5rem;
  padding: 1.25rem;
  background: #1e1e1e;
  border: 1px solid #333;
  border-radius: 6px;
}

.filter-group {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.filter-group label {
  color: #999;
  font-size: 0.85rem;
  font-weight: 500;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.filter-group select {
  padding: 0.65rem;
  background: #252525;
  color: #fff;
  border: 1px solid #444;
  border-radius: 6px;
  font-size: 0.95rem;
}

.filter-group select:focus {
  outline: none;
  border-color: var(--brand-focus);
}

.admin-message {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.85rem 1rem;
  margin-bottom: 1.5rem;
  background: rgba(243, 156, 18, 0.1);
  border: 1px solid rgba(243, 156, 18, 0.3);
  border-radius: 6px;
  color: #f39c12;
  font-size: 0.9rem;
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
  margin-bottom: 2rem;
}

.error-message strong {
  display: block;
  margin-bottom: 0.25rem;
}

.error-message p {
  margin: 0;
  font-size: 0.9rem;
}

.history-wrapper {
  background: #1e1e1e;
  border: 1px solid #333;
  border-radius: 6px;
  overflow: hidden;
}

.history-table {
  background: #252525;
}

.history-head,
.history-row {
  display: grid;
  grid-template-columns:
    minmax(180px, 1.4fr) 110px 140px 150px minmax(160px, 1.2fr) minmax(180px, 1.6fr)
    84px;
  gap: 0.75rem;
  align-items: center;
  padding: 0.75rem 1rem;
  border-bottom: 1px solid #2a2a2a;
}

.history-head {
  background: #1e1e1e;
  color: #999;
  font-size: 0.78rem;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.col-head {
  display: flex;
  align-items: center;
  gap: 0.35rem;
  background: none;
  border: none;
  padding: 0;
  color: inherit;
  font: inherit;
  text-align: left;
  text-transform: inherit;
  letter-spacing: inherit;
}

.col-head.sortable {
  cursor: pointer;
}

.col-head.sortable:hover,
.col-head.active {
  color: #fff;
}

.col-head svg {
  width: 12px;
  height: 12px;
}

.history-row {
  font-size: 0.88rem;
  color: #ccc;
  transition: background 0.15s;
}

.history-row:hover {
  background: #2a2a2a;
}

.cell {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.cell.event {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.event-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  flex-shrink: 0;
  border-radius: 6px;
}

.event-icon.small {
  width: 20px;
  height: 20px;
}

.event-icon svg {
  width: 15px;
  height: 15px;
}

.event-success {
  background: rgba(46, 204, 113, 0.15);
  color: #2ecc71;
}

.event-info {
  background: rgba(var(--brand-rgb), 0.15);
  color: var(--brand-500);
}

.event-warning {
  background: rgba(243, 156, 18, 0.15);
  color: #f39c12;
}

.event-danger {
  background: rgba(231, 76, 60, 0.15);
  color: #e74c3c;
}

.event-default {
  background: #333;
  color: #999;
}

.event-label {
  overflow: hidden;
  text-overflow: ellipsis;
}

.outcome-pill {
  display: inline-block;
  padding: 0.2rem 0.5rem;
  border-radius: 6px;
  font-size: 0.75rem;
  background: #333;
  color: #999;
}

.outcome-succeeded {
  background: rgba(46, 204, 113, 0.15);
  color: #2ecc71;
}

.outcome-failed {
  background: rgba(231, 76, 60, 0.15);
  color: #e74c3c;
}

.outcome-retrying,
.outcome-requested {
  background: rgba(var(--brand-rgb), 0.15);
  color: var(--brand-500);
}

.outcome-skipped {
  background: rgba(243, 156, 18, 0.15);
  color: #f39c12;
}

.cell.date {
  color: #888;
}

.audiobook-link {
  color: var(--brand-500);
  text-decoration: none;
}

.audiobook-link:hover {
  text-decoration: underline;
}

.muted {
  color: #666;
}

.cell.actions {
  display: flex;
  gap: 0.25rem;
  justify-content: flex-end;
}

.btn-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 0.35rem;
  background: transparent;
  color: #888;
  border: none;
  border-radius: 6px;
  cursor: pointer;
}

.btn-icon:hover:not(:disabled) {
  background: #333;
  color: #fff;
}

.btn-icon.danger:hover:not(:disabled) {
  color: #e74c3c;
}

.btn-icon:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.history-details {
  padding: 1rem 1.5rem 1.25rem;
  background: #1c1c1c;
  border-bottom: 1px solid #2a2a2a;
}

.attempt-chain h4 {
  margin: 0 0 0.75rem 0;
  color: #999;
  font-size: 0.78rem;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.chain-step {
  display: grid;
  grid-template-columns: 26px minmax(140px, auto) 100px 130px 1fr;
  gap: 0.6rem;
  align-items: center;
  padding: 0.4rem 0;
  font-size: 0.85rem;
  color: #bbb;
}

.chain-step.current {
  color: #fff;
}

.chain-time {
  color: #777;
  font-size: 0.8rem;
}

.chain-message {
  color: #888;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.detail-list {
  display: grid;
  grid-template-columns: 140px 1fr;
  gap: 0.35rem 1rem;
  margin: 1rem 0 0 0;
  font-size: 0.87rem;
}

.detail-list dt {
  color: #888;
}

.detail-list dd {
  margin: 0;
  color: #ddd;
  overflow-wrap: anywhere;
}

.detail-list dd.error-text {
  color: #e74c3c;
}

.detail-list dd.identifier {
  font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
  font-size: 0.8rem;
  color: #999;
}

.raw-data {
  margin-top: 1rem;
}

.raw-toggle {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  padding: 0.35rem 0.6rem;
  background: #252525;
  color: #999;
  border: 1px solid #383838;
  border-radius: 6px;
  font-size: 0.82rem;
  cursor: pointer;
}

.raw-toggle:hover {
  color: #fff;
  border-color: var(--brand-500);
}

.raw-toggle svg {
  width: 13px;
  height: 13px;
}

.raw-data pre {
  margin: 0.6rem 0 0 0;
  padding: 0.85rem;
  max-height: 320px;
  overflow: auto;
  background: #141414;
  border: 1px solid #2a2a2a;
  border-radius: 6px;
  color: #bbb;
  font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
  font-size: 0.8rem;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}

.pagination {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1rem;
  background: #1e1e1e;
  border-top: 1px solid #333;
}

.pagination-info {
  color: #999;
  font-size: 0.9rem;
}

.pagination-controls {
  display: flex;
  gap: 0.5rem;
}

.page-button {
  display: flex;
  align-items: center;
  justify-content: center;
  min-width: 40px;
  padding: 0.5rem 0.75rem;
  background: transparent;
  color: #999;
  border: 1px solid #444;
  border-radius: 6px;
  font-size: 0.9rem;
  cursor: pointer;
  transition: all 0.2s;
}

.page-button:hover:not(:disabled) {
  background: #333;
  color: #fff;
  border-color: var(--brand-500);
}

.page-button.active {
  background: var(--brand-500);
  color: #fff;
  border-color: var(--brand-500);
}

.page-button:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.pagination-size {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.pagination-size label {
  color: #999;
  font-size: 0.9rem;
}

.pagination-size select {
  padding: 0.5rem;
  background: #1e1e1e;
  color: #fff;
  border: 1px solid #444;
  border-radius: 6px;
  font-size: 0.9rem;
  cursor: pointer;
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

@media (max-width: 1100px) {
  .history-head,
  .history-row {
    grid-template-columns: minmax(150px, 1.4fr) 100px 130px 84px;
  }

  .col-head.source,
  .cell.source,
  .col-head.audiobook,
  .cell.audiobook,
  .col-head.source-title,
  .cell.source-title {
    display: none;
  }
}
</style>
