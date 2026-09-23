/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import CalendarView from '@/views/content/CalendarView.vue'
import type { Audiobook } from '@/types'

// G3 (tracker #189): the calendar's own item type used to be four fields (id, title,
// author, dateKey/date) with no status at all, so every day cell rendered the same neutral
// chip. This proves the month grid now colours events by the same precedence Readarr and
// Sonarr use (see src/utils/calendarEventStatus.ts for the citations) and that a legend
// naming those states is on the page.

const TODAY = new Date('2026-06-15T12:00:00Z')

function makeBook(overrides: Partial<Audiobook>): Audiobook {
  return {
    id: 1,
    title: 'Untitled',
    monitored: true,
    status: 'no-file',
    files: [],
    ...overrides,
  } as Audiobook
}

const books: Audiobook[] = [
  makeBook({
    id: 1,
    title: 'Downloaded Book',
    publishedDate: '2026-06-01',
    status: 'quality-match',
  }),
  makeBook({
    id: 2,
    title: 'Missing Book',
    publishedDate: '2026-06-02',
    status: 'no-file',
    monitored: true,
  }),
  makeBook({
    id: 3,
    title: 'Unmonitored Book',
    publishedDate: '2026-06-03',
    status: 'no-file',
    monitored: false,
  }),
  makeBook({
    id: 4,
    title: 'Unreleased Book',
    publishedDate: '2026-06-28',
    status: 'no-file',
    monitored: true,
  }),
  makeBook({
    id: 5,
    title: 'Downloading Book',
    publishedDate: '2026-06-04',
    status: 'no-file',
    monitored: true,
  }),
]

vi.mock('@/services/api', () => ({
  apiService: {
    getApiKey: vi.fn().mockResolvedValue({ apiKey: 'feedkey' }),
  },
}))

vi.mock('@/stores/library', () => ({
  useLibraryStore: () => ({
    audiobooks: books,
    fetchLibrary: vi.fn().mockResolvedValue(undefined),
  }),
}))

vi.mock('@/stores/downloads', () => ({
  useDownloadsStore: () => ({
    activeDownloads: [{ id: 'dl-1', audiobookId: 5, status: 'Downloading' }],
  }),
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
}))

async function mountView() {
  const wrapper = mount(CalendarView, { attachTo: document.body })
  await flushPromises()
  return wrapper
}

describe('CalendarView event status', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.useFakeTimers()
    vi.setSystemTime(TODAY)
    window.localStorage.clear()
    window.localStorage.setItem('listenarr.calendar.currentDate', TODAY.toISOString())
  })

  afterEach(() => {
    vi.useRealTimers()
    document.body.innerHTML = ''
  })

  it('assigns each month-grid event the status the family precedence resolves for it', async () => {
    const wrapper = await mountView()

    const downloaded = wrapper.find('[data-testid="calendar-event-1"]')
    const missing = wrapper.find('[data-testid="calendar-event-2"]')
    const unmonitored = wrapper.find('[data-testid="calendar-event-3"]')
    const unreleased = wrapper.find('[data-testid="calendar-event-4"]')
    const downloading = wrapper.find('[data-testid="calendar-event-5"]')

    expect(downloaded.classes()).toContain('status-downloaded')
    expect(missing.classes()).toContain('status-missing')
    expect(unmonitored.classes()).toContain('status-unmonitored')
    expect(unreleased.classes()).toContain('status-unreleased')
    expect(downloading.classes()).toContain('status-downloading')
  })

  // Control: this is the one case that already worked before G3 (the old boolean
  // `item.missing` marker). If this regresses, the rewrite broke behaviour the product
  // already had, not just behaviour it was gaining.
  it('control: the pre-existing missing-book search target list is unaffected by the status rewrite', async () => {
    const wrapper = await mountView()

    expect(wrapper.text()).toContain('Search')
    expect(wrapper.text()).toMatch(/Search \d+ missing/)
  })

  it('renders a legend naming the status states on the page', async () => {
    const wrapper = await mountView()

    const legend = wrapper.find('[data-testid="calendar-legend"]')
    expect(legend.exists()).toBe(true)
    expect(legend.text()).toContain('Downloaded')
    expect(legend.text()).toContain('Missing')
    expect(legend.text()).toContain('Unmonitored')
    expect(legend.text()).toContain('Unreleased')
    expect(legend.text()).toContain('Downloading')
  })
})
