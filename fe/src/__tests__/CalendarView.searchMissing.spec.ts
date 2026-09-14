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
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { describe, it, beforeEach, afterEach, expect, vi } from 'vitest'
import CalendarView from '@/views/content/CalendarView.vue'
import { useLibraryStore } from '@/stores/library'

const searchAndDownload = vi.fn(async () => ({ success: false, message: 'No matches found' }))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
}))

vi.mock('@/services/api', () => ({
  apiService: {
    getImageUrl: vi.fn((url: string) => url || ''),
    searchAndDownload: (id: number) => searchAndDownload(id),
  },
  getImageUrl: vi.fn((url: string) => url || ''),
  ensureImageCached: vi.fn(async () => true),
}))

type ViewMode = 'month' | 'week' | 'forecast' | 'day' | 'agenda'

type Vm = {
  viewMode: ViewMode
  currentDate: Date
  selectedDay: Date
  searchTargets: Array<{ id: number }>
  searchButtonLabel: string
  showSearchConfirm: boolean
  bulkSearchRunning: boolean
  visibleItems: Array<{ id: number; missing: boolean }>
  requestSearchMissing: () => void
  cancelSearchMissing: () => void
  confirmSearchMissing: () => Promise<void>
  nextMonth: () => void
  previousMonth: () => void
}

/*
 * January 2026 starts on a Thursday, so the 42-cell month grid runs from Sunday
 * 2025-12-28 to Saturday 2026-02-07. That overflow is the whole point of case 2: a
 * December book is genuinely on screen in January's grid and the button has to agree.
 *
 * Every date below is deliberately away from a month boundary except the two chosen to
 * probe the overflow, because calendarItems builds its Date at UTC midnight while the
 * grid keys cells by local date.
 */
const LIBRARY = [
  // missing, mid-January: in the month grid, in the 11-17 Jan week, in forecast, agenda
  { id: 1, title: 'Alpha Rising', authors: ['Ann Author'], publishedDate: '2026-01-13' },
  // present on disk, same day as Alpha: on the grid, never a target
  {
    id: 2,
    title: 'Beta Descending',
    authors: ['Bob Bard'],
    publishedDate: '2026-01-13',
    files: [{ id: 9 }],
  },
  // missing but unmonitored: on the grid, never a target
  {
    id: 3,
    title: 'Gamma Waits',
    authors: ['Ann Author'],
    publishedDate: '2026-01-14',
    monitored: false,
  },
  // missing, later in January: outside the 11-17 Jan week, inside the month grid
  { id: 4, title: 'Delta Pending', authors: ['Dee Diarist'], publishedDate: '2026-01-22' },
  // missing, December: only on screen because the January grid overflows backwards
  { id: 5, title: 'Epsilon Past', authors: ['Eve Editor'], publishedDate: '2025-12-29' },
  // missing, March: off the January grid entirely at both ends
  { id: 6, title: 'Zeta Far', authors: ['Zed Zither'], publishedDate: '2026-03-18' },
  // missing, no published date at all: never lands on the calendar
  { id: 7, title: 'Eta Undated', authors: ['Eli Essayist'] },
].map((book) => ({ monitored: true, files: [], ...book }))

async function mountCalendar() {
  const pinia = createPinia()
  setActivePinia(pinia)

  const store = useLibraryStore()
  store.audiobooks = LIBRARY as unknown as ReturnType<typeof useLibraryStore>['audiobooks']
  store.fetchLibrary = vi.fn(async () => undefined)

  const wrapper = mount(CalendarView, { global: { plugins: [pinia] } })
  const vm = wrapper.vm as unknown as Vm

  // Pin the window. Mid-month rather than the 1st, so week mode has a full week either
  // side of it and month stepping stays unambiguous.
  vm.currentDate = new Date(2026, 0, 15)
  await wrapper.vm.$nextTick()

  return { wrapper, vm }
}

/** Drive the spaced bulk loop to completion under fake timers. */
async function runToCompletion(vm: Vm) {
  vi.useFakeTimers()
  try {
    const running = vm.confirmSearchMissing()
    await vi.runAllTimersAsync()
    await running
  } finally {
    vi.useRealTimers()
  }
}

const searchedIds = () => searchAndDownload.mock.calls.map((call) => call[0])

describe('CalendarView search for missing', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    searchAndDownload.mockImplementation(async () => ({
      success: false,
      message: 'No matches found',
    }))
    window.localStorage.clear()
  })

  afterEach(() => {
    vi.useRealTimers()
    window.localStorage.clear()
  })

  it('targets only the books that are missing by the wanted predicate', async () => {
    const { vm } = await mountCalendar()

    // The January grid shows five books: 1, 2, 3, 4 and the December overflow 5.
    expect(vm.visibleItems.map((item) => item.id).sort()).toEqual([1, 2, 3, 4, 5])

    // Of those, only three are missing. 2 has a file, 3 is unmonitored.
    expect(vm.searchTargets.map((book) => book.id)).toEqual([5, 1, 4])
  })

  it('honours a server-computed wanted flag over the local fallback', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useLibraryStore()
    store.fetchLibrary = vi.fn(async () => undefined)
    store.audiobooks = [
      // has a file, so the fallback would say present: the server says otherwise
      {
        id: 11,
        title: 'Server Says Wanted',
        publishedDate: '2026-01-13',
        monitored: true,
        files: [{ id: 1 }],
        wanted: true,
      },
      // the fallback would say missing: the server says otherwise
      {
        id: 12,
        title: 'Server Says Present',
        publishedDate: '2026-01-13',
        monitored: true,
        files: [],
        wanted: false,
      },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']

    const wrapper = mount(CalendarView, { global: { plugins: [pinia] } })
    const vm = wrapper.vm as unknown as Vm
    vm.currentDate = new Date(2026, 0, 15)
    await wrapper.vm.$nextTick()

    expect(vm.searchTargets.map((book) => book.id)).toEqual([11])
  })

  it('confines targets to the window the active view mode actually renders', async () => {
    const { vm, wrapper } = await mountCalendar()

    // Month: the 42-cell grid, which overflows backwards into December. The December
    // book is on screen, so it is a target; March is off the grid at the far end.
    expect(vm.viewMode).toBe('month')
    expect(vm.searchTargets.map((book) => book.id)).toContain(5)
    expect(vm.searchTargets.map((book) => book.id)).not.toContain(6)

    // Week: Sunday 2026-01-11 to Saturday 2026-01-17. That drops both the December
    // overflow book and the one on 2026-01-22, which month mode was including.
    vm.viewMode = 'week'
    await wrapper.vm.$nextTick()
    expect(vm.searchTargets.map((book) => book.id)).toEqual([1])

    // Day: just 2026-01-13, where the only missing book is 1. Switching into day mode
    // snaps selectedDay to currentDate, so the day under test is chosen afterwards.
    vm.viewMode = 'day'
    await wrapper.vm.$nextTick()
    vm.selectedDay = new Date(2026, 0, 13)
    await wrapper.vm.$nextTick()
    expect(vm.searchTargets.map((book) => book.id)).toEqual([1])

    // Forecast: a fixed 30 days from the 1st of the displayed month, so 2026-01-01 to
    // 2026-01-30. No December overflow, but 2026-01-22 is back in.
    vm.viewMode = 'forecast'
    await wrapper.vm.$nextTick()
    expect(vm.searchTargets.map((book) => book.id)).toEqual([1, 4])

    // Agenda: the calendar month, so again no December and no March.
    vm.viewMode = 'agenda'
    await wrapper.vm.$nextTick()
    expect(vm.searchTargets.map((book) => book.id)).toEqual([1, 4])

    // The undated book is never anywhere.
    expect(vm.visibleItems.map((item) => item.id)).not.toContain(7)
  })

  it('re-derives the targets when the month is stepped', async () => {
    const { vm, wrapper } = await mountCalendar()

    expect(vm.searchTargets.map((book) => book.id)).toEqual([5, 1, 4])
    expect(vm.searchButtonLabel).toBe('Search 3 missing')

    // March 2026: only the 2026-03-18 book, which January never saw.
    vm.nextMonth()
    vm.nextMonth()
    await wrapper.vm.$nextTick()

    expect(vm.searchTargets.map((book) => book.id)).toEqual([6])
    expect(vm.searchButtonLabel).toBe('Search 1 missing')
  })

  it('disables the button and opens nothing when the window holds no missing books', async () => {
    const { vm, wrapper } = await mountCalendar()

    // August 2026 is empty in this library, though the library itself is not.
    vm.currentDate = new Date(2026, 7, 15)
    await wrapper.vm.$nextTick()

    expect(vm.searchTargets).toHaveLength(0)
    expect(vm.searchButtonLabel).toBe('Search 0 missing')

    const button = wrapper.findAll('button').find((b) => b.text().includes('missing'))
    expect(button?.attributes('disabled')).toBeDefined()

    vm.requestSearchMissing()
    await wrapper.vm.$nextTick()
    expect(vm.showSearchConfirm).toBe(false)

    await runToCompletion(vm)
    expect(searchAndDownload).not.toHaveBeenCalled()
  })

  it('gates the run behind the confirmation', async () => {
    const { vm, wrapper } = await mountCalendar()

    vm.requestSearchMissing()
    await wrapper.vm.$nextTick()

    expect(vm.showSearchConfirm).toBe(true)
    expect(searchAndDownload).not.toHaveBeenCalled()

    // Declining calls nothing and leaves the targets alone.
    vm.cancelSearchMissing()
    await wrapper.vm.$nextTick()
    expect(vm.showSearchConfirm).toBe(false)
    expect(searchAndDownload).not.toHaveBeenCalled()
    expect(vm.searchTargets).toHaveLength(3)

    vm.requestSearchMissing()
    await runToCompletion(vm)

    expect(searchAndDownload).toHaveBeenCalledTimes(3)
    expect(vm.showSearchConfirm).toBe(false)
    expect(vm.bulkSearchRunning).toBe(false)
  })

  it('searches each target once, in the displayed order, and no other book on the grid', async () => {
    const { vm } = await mountCalendar()

    await runToCompletion(vm)

    expect(searchedIds()).toEqual([5, 1, 4])
    // 2 has a file and 3 is unmonitored, yet both are on the same grid.
    expect(searchedIds()).not.toContain(2)
    expect(searchedIds()).not.toContain(3)
    // 6 and 7 are not on this window at all.
    expect(searchedIds()).not.toContain(6)
    expect(searchedIds()).not.toContain(7)

    // Having been searched, they drop out of the targets, so a second press is a no-op.
    expect(vm.searchTargets).toHaveLength(0)
  })

  it('marks the missing entries on the grid, and only those', async () => {
    const { wrapper } = await mountCalendar()

    const entries = wrapper.findAll('.calendar-item')
    const marked = entries.filter((e) => e.classes().includes('status-no-file'))

    expect(entries.length).toBeGreaterThan(0)
    expect(marked).toHaveLength(3)
    expect(marked.map((e) => e.text())).toEqual(
      expect.arrayContaining(['Epsilon Past', 'Alpha Rising', 'Delta Pending']),
    )

    const present = entries.find((e) => e.text() === 'Beta Descending')
    expect(present?.classes()).not.toContain('status-no-file')
    expect(present?.attributes('title')).toBe('Beta Descending - Bob Bard')

    const missing = entries.find((e) => e.text() === 'Alpha Rising')
    expect(missing?.attributes('title')).toBe('Alpha Rising - Ann Author (Missing)')
  })
})
