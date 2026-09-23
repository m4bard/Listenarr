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
import { FIRST_DAY_OF_WEEK_STORAGE_KEY } from '@/utils/calendarFirstDayOfWeek'

// G7 (tracker #189): the week always started Sunday, hardcoded twice
// (fe/src/views/content/CalendarView.vue:302's weekDays array and :448's grid-start
// computation, per the tracker citation). This proves both hardcodes are gone: the month
// header and the grid start both follow a persisted per-browser preference, and the week
// view (the variant "further down the file") follows it too.

const TODAY = new Date('2026-06-15T12:00:00Z') // a Monday

vi.mock('@/services/api', () => ({
  apiService: {
    getApiKey: vi.fn().mockResolvedValue({ apiKey: 'feedkey' }),
  },
}))

vi.mock('@/stores/library', () => ({
  useLibraryStore: () => ({
    audiobooks: [],
    fetchLibrary: vi.fn().mockResolvedValue(undefined),
  }),
}))

vi.mock('@/stores/downloads', () => ({
  useDownloadsStore: () => ({ activeDownloads: [] }),
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
}))

async function mountView() {
  const wrapper = mount(CalendarView, { attachTo: document.body })
  await flushPromises()
  return wrapper
}

describe('CalendarView first day of week', () => {
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

  // Control: with no stored preference and the Intl-based default unavailable in this
  // jsdom run's stubbed globals, the header must still read Sun..Sat, exactly the sequence
  // the page already rendered before G7. If this fails, the apparatus itself broke, not
  // just the new option.
  it('control: defaults the header to Sun..Sat when nothing is persisted', async () => {
    const wrapper = await mountView()
    const headers = wrapper.findAll('.day-header').map((h) => h.text())
    expect(headers).toEqual(['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'])
  })

  it('rotates the month header to start on Monday when that preference is persisted', async () => {
    window.localStorage.setItem(FIRST_DAY_OF_WEEK_STORAGE_KEY, '1')
    const wrapper = await mountView()
    const headers = wrapper.findAll('.day-header').map((h) => h.text())
    expect(headers).toEqual(['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'])
  })

  it('starts the month grid exactly on the 1st when Monday is selected and the month starts on a Monday', async () => {
    // June 2026 starts on a Monday, so a Monday-first grid should start exactly on
    // 2026-06-01 with no lead days from May, unlike the Sunday-first grid (which leads
    // with 2026-05-31, an other-month day).
    window.localStorage.setItem(FIRST_DAY_OF_WEEK_STORAGE_KEY, '1')
    const wrapper = await mountView()
    const firstCell = wrapper.find('.calendar-day')
    expect(firstCell.classes()).not.toContain('other-month')
    expect(firstCell.find('.day-number').text()).toBe('1')
  })

  it('control: the Sunday-first grid leads with an other-month day for the same month', async () => {
    // Same month as the test above, default (Sunday) first-day preference. June 1 2026 is
    // a Monday, so a Sunday-first grid must lead with May 31st, marked other-month. If
    // this regresses, the grid-start rewrite broke the pre-existing Sunday behaviour, not
    // just the new Monday option.
    const wrapper = await mountView()
    const firstCell = wrapper.find('.calendar-day')
    expect(firstCell.classes()).toContain('other-month')
    expect(firstCell.find('.day-number').text()).toBe('31')
  })

  it('offers a control to change the first day of week, and it persists the choice', async () => {
    const wrapper = await mountView()
    const select = wrapper.find('[data-testid="calendar-first-day-of-week"]')
    expect(select.exists()).toBe(true)

    await select.setValue('1')
    await flushPromises()

    expect(window.localStorage.getItem(FIRST_DAY_OF_WEEK_STORAGE_KEY)).toBe('1')
  })

  it('rotates the week-view header too, not just the month view', async () => {
    window.localStorage.setItem(FIRST_DAY_OF_WEEK_STORAGE_KEY, '1')
    const wrapper = await mountView()

    await wrapper
      .findAll('.tab')
      .find((t) => t.text() === 'Week')!
      .trigger('click')
    await flushPromises()

    const headers = wrapper.findAll('.week-day-name').map((h) => h.text())
    expect(headers[0]).toBe('Mon')
    expect(headers[6]).toBe('Sun')
  })
})
