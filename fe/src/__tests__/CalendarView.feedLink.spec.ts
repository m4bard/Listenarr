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

// The reason this test exists: without a control on the calendar page there is no path through
// the product to a feed URL, so the endpoint is unreachable and a test round cannot exercise it.
// Readarr puts the same control in the calendar page toolbar
// (frontend/src/Calendar/CalendarPage.js:101-105, onGetCalendarLinkPress at :48).

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

vi.mock('vue-router', () => ({
  useRouter: () => ({ push: vi.fn() }),
}))

async function mountView() {
  const wrapper = mount(CalendarView, { attachTo: document.body })
  await flushPromises()
  return wrapper
}

describe('CalendarView calendar feed link', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('offers a control on the calendar page that opens the feed dialog', async () => {
    const wrapper = await mountView()

    const trigger = wrapper.find('[data-testid="calendar-feed-open"]')
    expect(trigger.exists()).toBe(true)

    // The dialog teleports to document.body, so it is not there until the control is pressed.
    expect(document.querySelector('input[data-testid="calendar-feed-http-url"]')).toBeNull()

    await trigger.trigger('click')
    await flushPromises()

    const urlField = document.querySelector<HTMLInputElement>(
      'input[data-testid="calendar-feed-http-url"]',
    )
    expect(urlField).not.toBeNull()
    expect(urlField!.value).toContain('/feed/v1/calendar/Listenarr.ics?')
    expect(urlField!.value).toContain('apikey=feedkey')
  })

  it('closes the dialog again', async () => {
    const wrapper = await mountView()

    await wrapper.find('[data-testid="calendar-feed-open"]').trigger('click')
    await flushPromises()
    expect(document.querySelector('input[data-testid="calendar-feed-http-url"]')).not.toBeNull()

    document
      .querySelector<HTMLElement>('[data-testid="calendar-feed-close"]')!
      .dispatchEvent(new Event('click', { bubbles: true }))
    await flushPromises()

    expect(document.querySelector('input[data-testid="calendar-feed-http-url"]')).toBeNull()
  })
})
