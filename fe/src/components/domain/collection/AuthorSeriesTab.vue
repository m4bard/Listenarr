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
  <div class="author-series-tab">
    <EmptyState
      v-if="seriesRows.length === 0"
      title="No series found"
      message="This author has no books with series information yet."
    >
      <template #icon>
        <PhBookOpen :size="48" />
      </template>
    </EmptyState>

    <template v-else>
      <div class="author-series-tab-header">
        <span class="section-count">{{ seriesRows.length }} series</span>
        <button
          type="button"
          class="author-series-expand-all"
          :aria-pressed="allExpanded"
          @click="toggleAll"
        >
          <component :is="allExpanded ? PhArrowsInLineVertical : PhArrowsOutLineVertical" />
          {{ allExpanded ? 'Collapse All' : 'Expand All' }}
        </button>
      </div>

      <div class="author-series-list">
        <article v-for="row in seriesRows" :key="row.key" class="author-series-card">
          <div class="author-series-row">
            <button
              type="button"
              class="author-series-disclosure"
              :aria-expanded="row.expanded"
              :aria-controls="row.panelId"
              :aria-label="`${row.expanded ? 'Hide' : 'Show'} the books in ${row.name}`"
              @click="toggleExpanded(row.key)"
            >
              <component :is="row.expanded ? PhCaretDown : PhCaretRight" />
            </button>

            <div class="author-series-covers">
              <div
                v-for="(cover, index) in row.covers"
                :key="cover.key"
                class="author-series-cover-item"
                :class="{ 'is-not-added': !cover.inLibrary }"
                :style="seriesCoverMosaicStyle(index, row.covers.length)"
              >
                <img
                  :src="getProtectedImageSrc(cover.imageUrl, getPlaceholderUrl())"
                  :alt="`${cover.title} cover`"
                  class="author-series-cover-image"
                  loading="lazy"
                  decoding="async"
                  @error="handleImageError"
                />
              </div>
              <div v-if="row.covers.length === 0" class="author-series-cover-empty">
                <PhBookOpen />
              </div>
            </div>

            <div class="author-series-identity">
              <RouterLink class="author-series-name" :to="row.route">{{ row.name }}</RouterLink>
              <Pill variant="success" size="small">
                {{ row.libraryCount }} of {{ row.totalCount }} in library
              </Pill>
            </div>

            <div class="author-series-monitor">
              <button
                type="button"
                class="author-series-monitor-btn"
                :class="{ active: row.monitored }"
                :aria-disabled="row.monitorDisabled"
                :aria-describedby="row.monitorNote ? row.noteId : undefined"
                :title="row.monitorTitle"
                @click="toggleMonitoring(row.key)"
              >
                <PhArrowClockwise v-if="row.busy" class="spin-icon" />
                <component v-else :is="row.monitored ? PhEye : PhPlus" />
                {{ row.monitored ? 'Monitoring' : 'Monitor' }}
              </button>
              <p v-if="row.monitorNote" :id="row.noteId" class="author-series-monitor-note">
                {{ row.monitorNote }}
              </p>
            </div>
          </div>

          <div v-show="row.expanded" :id="row.panelId" class="author-series-books">
            <div v-for="member in row.members" :key="member.book.key" class="author-series-book">
              <span class="author-series-position">{{
                member.position ? `#${member.position}` : ''
              }}</span>
              <span class="author-series-book-title">{{ safeText(member.book.title) }}</span>
              <Pill :variant="member.book.inLibrary ? 'success' : 'warning'" size="small">
                {{ member.book.inLibrary ? 'In Library' : 'Not Added' }}
              </Pill>
              <span
                v-if="member.book.inLibrary"
                class="author-series-monitored-badge"
                :class="{ unmonitored: !member.book.monitored }"
              >
                <component :is="member.book.monitored ? PhEye : PhEyeSlash" />
                {{ formatMonitoringLabel(member.book) }}
              </span>
            </div>
          </div>
        </article>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import {
  PhArrowClockwise,
  PhArrowsInLineVertical,
  PhArrowsOutLineVertical,
  PhBookOpen,
  PhCaretDown,
  PhCaretRight,
  PhEye,
  PhEyeSlash,
  PhPlus,
} from '@phosphor-icons/vue'
import { EmptyState, Pill } from '@/components/base'
import { apiService } from '@/services/api'
import { errorTracking } from '@/services/errorTracking'
import { useToast } from '@/services/toastService'
import { useProtectedImages } from '@/composables/useProtectedImages'
import { getPlaceholderUrl } from '@/utils/placeholder'
import { seriesCoverMosaicStyle } from '@/utils/seriesUtils'
import { formatMonitoringLabel } from '@/utils/audiobookStatus'
import { safeText } from '@/utils/textUtils'
import type { MonitoredSeries } from '@/types'
import {
  authorSeriesElementId,
  groupAuthorSeries,
  type AuthorSeriesBook,
  type AuthorSeriesGroup,
} from './authorSeriesGrouping'

// This panel is the whole content of the Series tab: the same card content, grouping rules
// and monitoring wiring as the Series section shape, laid out as a standalone tab rather
// than a strip above the book grid. See authorSeriesGrouping.ts for the shared logic.

interface Props {
  books: readonly AuthorSeriesBook[]
  region: string
  language: string
}

interface Emits {
  (e: 'monitoring-changed'): void
}

const MAX_MOSAIC_COVERS = 4
// The monitoring status endpoint answers about one series at a time, so a prolific author
// would otherwise open a request per series at once.
const MONITORING_STATUS_CONCURRENCY = 5
const NO_ASIN_MONITOR_TITLE =
  'Listenarr monitors a series by its ASIN. This series is known by name only, ' +
  'so it cannot be monitored yet.'

const props = defineProps<Props>()
const emit = defineEmits<Emits>()

const toast = useToast()
const { getProtectedImageSrc } = useProtectedImages()

const expandedKeys = ref<string[]>([])
const busyKeys = ref<string[]>([])
const monitoredByKey = ref<Record<string, MonitoredSeries | null>>({})
let monitoringRequestId = 0

const seriesGroups = computed(() => groupAuthorSeries(props.books))

const seriesRows = computed(() =>
  seriesGroups.value.map((group) => ({
    ...group,
    route: `/collection/series/${encodeURIComponent(group.name)}`,
    expanded: expandedKeys.value.includes(group.key),
    busy: busyKeys.value.includes(group.key),
    monitored: Boolean(monitoredByKey.value[group.key]),
    monitorDisabled: !group.asin || busyKeys.value.includes(group.key),
    monitorTitle: monitorTitleFor(group),
    monitorNote: group.asin ? '' : NO_ASIN_MONITOR_TITLE,
    panelId: authorSeriesElementId('author-series-tab-books', group.key),
    noteId: authorSeriesElementId('author-series-tab-monitor-note', group.key),
    covers: group.members
      .filter((member) => Boolean(member.book.imageUrl))
      .slice(0, MAX_MOSAIC_COVERS)
      .map((member) => member.book),
  })),
)

const allExpanded = computed(
  () =>
    seriesGroups.value.length > 0 &&
    seriesGroups.value.every((group) => expandedKeys.value.includes(group.key)),
)

// The monitoring status endpoint answers for one series at a time, so ask only about the
// series that could be monitored at all: a series known by name alone has no ASIN to monitor.
const monitorableSignature = computed(() =>
  seriesGroups.value
    .filter((group) => Boolean(group.asin))
    .map((group) => `${group.key}::${group.name}`)
    .join('|'),
)

function monitorTitleFor(group: AuthorSeriesGroup): string {
  if (!group.asin) return NO_ASIN_MONITOR_TITLE
  return monitoredByKey.value[group.key] ? `Stop monitoring ${group.name}` : `Monitor ${group.name}`
}

function toggleExpanded(key: string) {
  expandedKeys.value = expandedKeys.value.includes(key)
    ? expandedKeys.value.filter((expandedKey) => expandedKey !== key)
    : [...expandedKeys.value, key]
}

function toggleAll() {
  expandedKeys.value = allExpanded.value ? [] : seriesGroups.value.map((group) => group.key)
}

function setBusy(key: string, busy: boolean) {
  busyKeys.value = busy
    ? [...busyKeys.value, key]
    : busyKeys.value.filter((busyKey) => busyKey !== key)
}

async function loadMonitoringStatus() {
  const monitorableGroups = seriesGroups.value.filter((group) => Boolean(group.asin))
  const requestId = ++monitoringRequestId

  if (monitorableGroups.length === 0) {
    monitoredByKey.value = {}
    return
  }

  const pending = [...monitorableGroups]
  const statuses: Record<string, MonitoredSeries | null> = {}
  const workerCount = Math.min(MONITORING_STATUS_CONCURRENCY, pending.length)

  await Promise.all(
    Array.from({ length: workerCount }, async () => {
      for (let group = pending.shift(); group; group = pending.shift()) {
        statuses[group.key] = await fetchMonitoringStatus(group)
      }
    }),
  )

  if (requestId !== monitoringRequestId) return
  monitoredByKey.value = statuses
}

async function fetchMonitoringStatus(group: AuthorSeriesGroup): Promise<MonitoredSeries | null> {
  try {
    const response = await apiService.getSeriesMonitoringStatus(
      group.name,
      props.region,
      props.language,
    )
    const monitored = response.monitoredSeries ?? null
    if (!monitored) return null

    // The endpoint resolves a series by name, while this tab knows a series by its ASIN.
    // A record naming a different ASIN is a different series that happens to share the name.
    const recordAsin = (monitored.seriesAsin || '').trim().toUpperCase()
    if (recordAsin && recordAsin !== group.asin) return null
    return monitored
  } catch (err) {
    errorTracking.captureException(err as Error, {
      component: 'AuthorSeriesTab',
      operation: 'loadMonitoringStatus',
      metadata: { series: group.name, region: props.region, language: props.language },
    })
    return null
  }
}

async function toggleMonitoring(key: string) {
  const group = seriesGroups.value.find((candidate) => candidate.key === key)
  if (!group || !group.asin || busyKeys.value.includes(key)) return

  setBusy(key, true)
  try {
    const monitored = monitoredByKey.value[key]
    if (monitored) {
      await apiService.unmonitorSeries(monitored.id)
      monitoredByKey.value = { ...monitoredByKey.value, [key]: null }
      toast.success(
        'Series unmonitored',
        `"${group.name}" will no longer be checked for future audiobooks.`,
      )
      emit('monitoring-changed')
      return
    }

    const response = await apiService.monitorSeries({
      name: group.name,
      asin: group.asin,
      region: props.region,
      language: props.language,
    })

    monitoredByKey.value = { ...monitoredByKey.value, [key]: response.monitoredSeries }

    const details =
      response.addedCount > 0
        ? `Added ${response.addedCount} audiobook${response.addedCount === 1 ? '' : 's'} from the current series catalog.`
        : 'No new audiobooks needed to be added from the current series catalog.'
    toast.success('Series monitored', details)

    if (response.failedCount > 0 || response.errorMessage) {
      toast.warning(
        'Monitoring completed with warnings',
        response.errorMessage ||
          `${response.failedCount} audiobook${response.failedCount === 1 ? '' : 's'} could not be added automatically.`,
      )
    }

    emit('monitoring-changed')
  } catch (err) {
    const message = err instanceof Error ? err.message : 'Failed to update series monitoring.'
    toast.error('Series monitoring failed', message)
    errorTracking.captureException(err as Error, {
      component: 'AuthorSeriesTab',
      operation: 'toggleMonitoring',
      metadata: { series: group.name, region: props.region, language: props.language },
    })
  } finally {
    setBusy(key, false)
  }
}

// A placeholder that fails in turn would otherwise re-enter this handler forever.
function handleImageError(event: Event) {
  const image = event.target as HTMLImageElement | null
  if (!image || image.dataset.fallbackApplied === 'true') return
  image.dataset.fallbackApplied = 'true'
  image.src = getPlaceholderUrl()
}

watch(
  [monitorableSignature, () => props.region, () => props.language],
  () => {
    void loadMonitoringStatus()
  },
  { immediate: true },
)
</script>

<style scoped>
.author-series-tab {
  display: flex;
  flex-direction: column;
  gap: 0.85rem;
  padding: 18px 20px;
}

.author-series-tab-header {
  display: flex;
  align-items: center;
  gap: 12px;
  color: #cfd5df;
  font-size: 12px;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

.section-count {
  font-weight: 700;
}

.author-series-expand-all {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  margin-left: auto;
  padding: 6px 12px;
  background-color: transparent;
  border: 1px solid rgba(255, 255, 255, 0.06);
  border-radius: 6px;
  color: #e6eef8;
  font-size: 11px;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  cursor: pointer;
  transition: background-color 0.12s ease;
}

.author-series-expand-all:hover {
  background-color: rgba(255, 255, 255, 0.03);
}

.author-series-list {
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
}

.author-series-card {
  background: var(--card-bg);
  border: 1px solid rgba(255, 255, 255, 0.06);
  border-radius: 6px;
  overflow: hidden;
}

.author-series-row {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 10px 14px;
}

.author-series-disclosure {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  background-color: transparent;
  border: 1px solid rgba(255, 255, 255, 0.06);
  border-radius: 6px;
  color: #e6eef8;
  cursor: pointer;
  transition: background-color 0.12s ease;
}

.author-series-disclosure:hover {
  background-color: rgba(255, 255, 255, 0.03);
}

.author-series-covers {
  position: relative;
  flex-shrink: 0;
  width: 132px;
  aspect-ratio: 2 / 1;
  border-radius: 12px;
}

.author-series-cover-item {
  position: absolute;
  top: 0;
}

.author-series-cover-image {
  width: 100%;
  height: 100%;
  object-fit: cover;
  border-radius: 12px;
}

.author-series-cover-item.is-not-added .author-series-cover-image {
  filter: grayscale(0.58) brightness(0.56) saturate(0.72);
  opacity: 0.76;
}

.author-series-cover-empty {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 50%;
  height: 100%;
  margin-left: 25%;
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.04);
  color: rgba(230, 238, 248, 0.5);
}

.author-series-identity {
  display: flex;
  flex: 1;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
  min-width: 0;
}

.author-series-name {
  color: #f3f6fb;
  font-size: 0.95rem;
  font-weight: 600;
  text-decoration: none;
}

.author-series-name:hover {
  color: var(--brand-500);
  text-decoration: underline;
}

.author-series-monitor-btn {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 8px 14px;
  background-color: transparent;
  border: 1px solid rgba(255, 255, 255, 0.06);
  border-radius: 6px;
  color: #e6eef8;
  font-size: 12px;
  cursor: pointer;
  transition: background-color 0.12s ease;
}

.author-series-monitor {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 4px;
  max-width: 260px;
}

.author-series-monitor-btn:hover:not([aria-disabled='true']) {
  background-color: rgba(255, 255, 255, 0.03);
}

.author-series-monitor-btn.active {
  background-color: var(--brand-500);
  border-color: var(--brand-500);
  color: #fff;
}

.author-series-monitor-btn[aria-disabled='true'] {
  cursor: not-allowed;
  opacity: 0.5;
}

.author-series-monitor-note {
  margin: 0;
  color: rgba(230, 238, 248, 0.55);
  font-size: 0.7rem;
  line-height: 1.4;
  text-align: right;
}

.author-series-books {
  display: flex;
  flex-direction: column;
  border-top: 1px solid rgba(255, 255, 255, 0.06);
}

.author-series-book {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 14px 8px 56px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.03);
  color: #e6eef8;
  font-size: 0.85rem;
}

.author-series-book:last-child {
  border-bottom: none;
}

.author-series-position {
  flex-shrink: 0;
  width: 46px;
  color: rgba(230, 238, 248, 0.62);
  font-size: 0.78rem;
}

.author-series-book-title {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.author-series-monitored-badge {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: #2ecc71;
  font-size: 0.75rem;
}

.author-series-monitored-badge.unmonitored {
  color: rgba(230, 238, 248, 0.55);
}

.author-series-monitored-badge :deep(svg) {
  width: 0.875em;
  height: 0.875em;
}

.spin-icon {
  animation: author-series-spin 1s linear infinite;
}

@keyframes author-series-spin {
  from {
    transform: rotate(0deg);
  }
  to {
    transform: rotate(360deg);
  }
}

@media (max-width: 768px) {
  .author-series-row {
    flex-wrap: wrap;
  }

  .author-series-book {
    padding-left: 14px;
  }
}
</style>
