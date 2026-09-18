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
import CalendarFeedModal from '@/components/domain/calendar/CalendarFeedModal.vue'

const getApiKey = vi.fn()

vi.mock('@/services/api', () => ({
  apiService: {
    getApiKey: () => getApiKey(),
  },
}))

// Modal teleports its content to document.body, so the assertions read the document rather
// than the wrapper subtree. Same approach as ConfirmModal.spec.ts.
async function mountModal(props: Record<string, unknown> = {}) {
  const wrapper = mount(CalendarFeedModal, {
    props: { visible: true, ...props },
    attachTo: document.body,
  })
  await flushPromises()
  return wrapper
}

function field(testId: string): HTMLInputElement | null {
  return document.querySelector<HTMLInputElement>(`input[data-testid="${testId}"]`)
}

function httpUrl(): string {
  return field('calendar-feed-http-url')?.value ?? ''
}

async function type(testId: string, value: string) {
  const input = field(testId)!
  input.value = value
  input.dispatchEvent(new Event('input'))
  await flushPromises()
}

async function check(testId: string) {
  const input = field(testId)!
  input.checked = true
  input.dispatchEvent(new Event('change'))
  await flushPromises()
}

async function press(testId: string) {
  document
    .querySelector<HTMLElement>(`[data-testid="${testId}"]`)!
    .dispatchEvent(new Event('click', { bubbles: true }))
  await flushPromises()
}

describe('CalendarFeedModal', () => {
  beforeEach(() => {
    getApiKey.mockReset()
    getApiKey.mockResolvedValue({ apiKey: 'feedkey' })
  })

  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('offers a subscribe URL carrying the api key, which is the only way to reach the feed', async () => {
    // The endpoint is guarded by [RequireApiKey] and a calendar client cannot send a header,
    // so a URL without the key in the query is not a usable subscription.
    await mountModal()

    expect(httpUrl()).toContain('/feed/v1/calendar/Listenarr.ics?')
    expect(httpUrl()).toContain('apikey=feedkey')
    expect(httpUrl()).toContain('pastDays=7')
    expect(httpUrl()).toContain('futureDays=28')
  })

  it('offers a webcal form of the same URL', async () => {
    await mountModal()

    expect(field('calendar-feed-webcal-url')?.value.startsWith('webcal://')).toBe(true)
  })

  it('fetches the api key rather than expecting the caller to supply it', async () => {
    await mountModal()

    expect(getApiKey).toHaveBeenCalledTimes(1)
  })

  it('does not fetch the key while it is closed', async () => {
    await mountModal({ visible: false })

    expect(getApiKey).not.toHaveBeenCalled()
  })

  it('rebuilds the URL when a control changes', async () => {
    await mountModal()

    expect(httpUrl()).not.toContain('unmonitored=true')

    await check('calendar-feed-unmonitored')

    expect(httpUrl()).toContain('unmonitored=true')
  })

  it('carries a typed tag list into the tags parameter', async () => {
    await mountModal()

    await type('calendar-feed-tags', 'tbr, classics')

    expect(httpUrl()).toContain('tags=tbr%2Cclassics')
  })

  it('reflects an edited future-day count', async () => {
    await mountModal()

    await type('calendar-feed-future-days', '90')

    expect(httpUrl()).toContain('futureDays=90')
  })

  it('copies the URL to the clipboard', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(navigator, 'clipboard', {
      value: { writeText },
      configurable: true,
    })

    await mountModal()
    await press('calendar-feed-copy-http')

    expect(writeText).toHaveBeenCalledTimes(1)
    expect(writeText.mock.calls[0][0]).toContain('apikey=feedkey')
  })

  it('says so rather than showing a dead link when no api key exists yet', async () => {
    getApiKey.mockResolvedValue({ apiKey: '' })

    await mountModal()

    expect(field('calendar-feed-http-url')).toBeNull()
    expect(document.querySelector('[data-testid="calendar-feed-no-key"]')).not.toBeNull()
  })

  it('surfaces a failed key lookup instead of rendering an empty box', async () => {
    getApiKey.mockRejectedValue(new Error('nope'))

    await mountModal()

    expect(field('calendar-feed-http-url')).toBeNull()
    expect(document.querySelector('[data-testid="calendar-feed-error"]')).not.toBeNull()
  })

  it('warns that a dev-server URL is not servable, without hiding it', async () => {
    // vitest runs with import.meta.env.DEV true, which is the branch under test. The dev server
    // proxies /api and /hubs and not /feed (fe/vite.config.ts), so the URL falls through to the
    // SPA and answers with HTML and a 200 -- a failure that looks like a success. The notice sits
    // beside the URL rather than replacing it, because a developer still wants to read it.
    await mountModal()

    expect(document.querySelector('[data-testid="calendar-feed-dev"]')).not.toBeNull()
    expect(httpUrl()).toContain('/feed/v1/calendar/Listenarr.ics?')
  })

  it('emits close', async () => {
    const wrapper = await mountModal()

    await press('calendar-feed-close')

    expect(wrapper.emitted('close')).toBeTruthy()
  })
})
