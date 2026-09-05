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
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import CollectionView from '@/views/library/CollectionView.vue'
import { useLibraryStore } from '@/stores/library'
import type { Audiobook } from '@/types'

vi.mock('@/services/api', () => ({
  apiService: {
    getImageUrl: vi.fn((url: string) => url || 'placeholder.png'),
    getBootstrapConfig: vi.fn(async () => ({})),
    getStartupConfig: vi.fn(async () => ({})),
    getLibrary: vi.fn(async () => []),
    getApplicationSettings: vi.fn(async () => ({})),
    getAuthorCatalog: vi.fn(async () => null),
    getAuthorLookup: vi.fn(async () => null),
    getSeriesCatalog: vi.fn(async () => null),
    getSeriesLookup: vi.fn(async () => null),
    getAuthorMonitoringStatus: vi.fn(async () => ({ isMonitored: false, monitoredAuthor: null })),
    getSeriesMonitoringStatus: vi.fn(async () => ({ isMonitored: false, monitoredSeries: null })),
    getAudiobookDeleteCapabilities: vi.fn(async () => ({
      canRemoveFromLibrary: true,
      canDeleteTrackedFiles: true,
      canDeleteFolder: true,
      reason: null,
      fallbackAction: 'RemoveFromLibraryOnly',
    })),
  },
}))

vi.mock('@/services/toastService', () => ({
  useToast: () => ({ success: vi.fn(), warning: vi.fn(), error: vi.fn(), info: vi.fn() }),
}))

const ensureBrowserGlobals = () => {
  const host = globalThis as unknown as Record<string, unknown>
  if (typeof host.ResizeObserver === 'undefined') {
    host.ResizeObserver = class {
      observe() {}
      disconnect() {}
    }
  }
  if (typeof host.WebSocket === 'undefined') {
    host.WebSocket = function () {}
  }
}

const mountListView = async () => {
  ensureBrowserGlobals()
  const pinia = createPinia()
  setActivePinia(pinia)

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'home', component: { template: '<div />' } },
      { path: '/audiobooks/:id', name: 'audiobook', component: { template: '<div />' } },
      { path: '/collection/:type/:name', name: 'collection', component: CollectionView },
    ],
  })
  await router.push('/collection/series/Series%201')
  await router.isReady().catch(() => {})
  const push = vi.spyOn(router, 'push')

  const store = useLibraryStore()
  store.audiobooks = [
    { id: 1, title: 'Book A', authors: ['Author A'], series: 'Series 1', files: [] },
  ] as unknown as Audiobook[]
  store.fetchLibrary = vi.fn(async () => undefined)

  const wrapper = mount(CollectionView, {
    global: {
      plugins: [pinia, router],
      stubs: ['EditAudiobookModal', 'CustomSelect', 'AddLibraryModal'],
    },
  })
  await new Promise((resolve) => setTimeout(resolve, 0))

  wrapper.vm.viewMode = 'list'
  await wrapper.vm.$nextTick()

  push.mockClear()
  return { wrapper, push }
}

describe('CollectionView list-view status badge', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('carries its status as readable text, which is what should be announced', async () => {
    const { wrapper } = await mountListView()
    const badge = wrapper.find('.status-badge')

    expect(badge.exists()).toBe(true)
    expect(badge.text().trim().length).toBeGreaterThan(0)
  })

  it('is not exposed to assistive technology as a button', async () => {
    const { wrapper } = await mountListView()
    const badge = wrapper.find('.status-badge')

    expect(badge.attributes('role')).toBeUndefined()
  })

  it('is not placed in the keyboard tab order', async () => {
    const { wrapper } = await mountListView()
    const badge = wrapper.find('.status-badge')

    expect(badge.attributes('tabindex')).toBeUndefined()
    expect((badge.element as HTMLElement).tabIndex).toBe(-1)
  })

  // The aria-label overrode the badge's own text while the role was present. With
  // the role gone the text is announced in reading order, so re-adding the label
  // in any form would say the same thing twice.
  it('does not carry an aria-label that would double up on its text', async () => {
    const { wrapper } = await mountListView()
    const badge = wrapper.find('.status-badge')

    expect(badge.attributes('aria-label')).toBeUndefined()
  })

  // A pointer cursor is the visual half of the same false affordance: it tells a
  // sighted user the badge can be clicked, and it cannot. Scoped styles are not
  // applied to a test mount, so this reads the rule out of the component source.
  it('does not offer a pointer cursor on an element that does nothing', () => {
    const source = readFileSync(
      resolve(process.cwd(), 'src/views/library/CollectionView.vue'),
      'utf-8',
    )
    const rules = [...source.matchAll(/^\.status-badge \{([^}]*)\}/gm)].map((match) => match[1])

    expect(rules.length).toBeGreaterThan(0)
    for (const body of rules) {
      expect(body).not.toMatch(/cursor:\s*pointer/)
    }
  })

  // The whole claim of this change is that only the exposure changes. The click
  // swallow is real: it stops the row handler from opening the audiobook.
  it('still stops a click on the badge from opening the audiobook', async () => {
    const { wrapper, push } = await mountListView()

    await wrapper.find('.status-badge').trigger('click')

    expect(push).not.toHaveBeenCalled()
  })

  it('still lets a click on the row itself open the audiobook', async () => {
    const { wrapper, push } = await mountListView()

    await wrapper.find('.audiobook-list-item').trigger('click')

    expect(push).toHaveBeenCalledWith('/audiobooks/1')
  })
})
