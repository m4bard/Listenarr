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
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import AudiobooksView from '@/views/library/AudiobooksView.vue'
import { useLibraryStore } from '@/stores/library'
import type { Audiobook } from '@/types'

vi.mock('@/services/api', () => ({
  apiService: {
    getQualityProfiles: vi.fn(async () => []),
    getImageUrl: vi.fn((url: string) => url || ''),
    getBootstrapConfig: vi.fn(async () => ({})),
    getStartupConfig: vi.fn(async () => ({})),
    getApplicationSettings: vi.fn(async () => ({})),
    getAudiobookDeleteCapabilities: vi.fn(async () => ({
      canRemoveFromLibrary: true,
      canDeleteTrackedFiles: true,
      canDeleteFolder: true,
      reason: null,
      fallbackAction: 'RemoveFromLibraryOnly' as const,
    })),
  },
}))

type Vm = {
  searchQuery: string
  audiobooks: Array<{ id: number }>
  selectAllLabel: string
  selectedCount: number
}

const LIBRARY = [
  { id: 1, title: 'Treasure Island', authors: ['Robert Louis Stevenson'], files: [] },
  { id: 2, title: 'Kidnapped', authors: ['Robert Louis Stevenson'], files: [] },
  { id: 3, title: 'Moby Dick', authors: ['Herman Melville'], files: [] },
  { id: 4, title: 'Walden', authors: ['Henry David Thoreau'], files: [] },
] as unknown as Audiobook[]

function installBrowserStubs() {
  const g = globalThis as unknown as Record<string, unknown>
  if (typeof g.ResizeObserver === 'undefined') {
    g.ResizeObserver = class {
      observe() {}
      disconnect() {}
    }
  }
  if (typeof g.WebSocket === 'undefined') {
    g.WebSocket = function () {
      /* noop */
    }
  }
}

async function mountLibrary() {
  installBrowserStubs()
  const pinia = createPinia()
  setActivePinia(pinia)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'home', component: { template: '<div />' } },
      { path: '/audiobooks', name: 'audiobooks', component: AudiobooksView },
    ],
  })
  await router.push('/audiobooks')
  await router.isReady().catch(() => {})

  const store = useLibraryStore()
  store.audiobooks = LIBRARY
  store.fetchLibrary = vi.fn(async () => undefined)

  const wrapper = mount(AudiobooksView, {
    global: {
      plugins: [pinia, router],
      stubs: [
        'BulkEditModal',
        'EditAudiobookModal',
        'CustomFilterModal',
        'FiltersDropdown',
        'CustomSelect',
      ],
    },
  })
  await new Promise((r) => setTimeout(r, 0))
  return { wrapper, store, vm: wrapper.vm as unknown as Vm }
}

const findSelectAll = (wrapper: Awaited<ReturnType<typeof mountLibrary>>['wrapper']) =>
  wrapper.findAll('button.toolbar-btn').find((b) => b.text().includes('Select All'))

describe('AudiobooksView Select All scope', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    // The view persists its search box to localStorage, so a query from one test
    // would otherwise be restored on the next mount.
    localStorage.clear()
  })

  it('selects only the filtered rows the grid is showing', async () => {
    const { wrapper, store, vm } = await mountLibrary()

    vm.searchQuery = 'Stevenson'
    await wrapper.vm.$nextTick()
    expect(vm.audiobooks.map((b) => b.id).sort()).toEqual([1, 2])

    await findSelectAll(wrapper)?.trigger('click')
    await wrapper.vm.$nextTick()

    expect([...store.selectedIds].sort()).toEqual([1, 2])
    expect(store.selectedIds.has(3)).toBe(false)
    expect(store.selectedIds.has(4)).toBe(false)
  })

  it('selects the whole library when no filter is active', async () => {
    const { wrapper, store } = await mountLibrary()

    await findSelectAll(wrapper)?.trigger('click')
    await wrapper.vm.$nextTick()

    expect([...store.selectedIds].sort()).toEqual([1, 2, 3, 4])
  })

  it('disables Select All when the filter matches nothing', async () => {
    const { wrapper, store, vm } = await mountLibrary()

    vm.searchQuery = 'no such book anywhere'
    await wrapper.vm.$nextTick()
    expect(vm.audiobooks).toHaveLength(0)

    const button = findSelectAll(wrapper)
    expect(button?.exists()).toBe(true)
    expect(button?.attributes('disabled')).toBeDefined()

    // Even if it is reached some other way, it must select nothing.
    await button?.trigger('click')
    await wrapper.vm.$nextTick()
    expect(store.selectedIds.size).toBe(0)
  })

  it('puts the count on the label while a filter is active', async () => {
    const { wrapper, vm } = await mountLibrary()

    expect(vm.selectAllLabel).toBe('Select All')

    vm.searchQuery = 'Stevenson'
    await wrapper.vm.$nextTick()
    expect(vm.selectAllLabel).toBe('Select All (2)')
    expect(findSelectAll(wrapper)?.text()).toContain('Select All (2)')
  })
})

describe('library store selectAll', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('selects only the subset it is handed', () => {
    const store = useLibraryStore()
    store.audiobooks = LIBRARY

    store.selectAll([{ id: 2 }, { id: 4 }])

    expect([...store.selectedIds].sort()).toEqual([2, 4])
  })

  it('falls back to the whole library when handed nothing', () => {
    const store = useLibraryStore()
    store.audiobooks = LIBRARY

    store.selectAll()

    expect([...store.selectedIds].sort()).toEqual([1, 2, 3, 4])
  })
})
