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
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import AudiobooksView from '@/views/library/AudiobooksView.vue'
import { useLibraryStore } from '@/stores/library'
import type { Audiobook } from '@/types'

const searchAndDownload = vi.fn(async () => ({
  success: true,
  indexerUsed: 'Test Indexer',
}))

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
    searchAndDownload: (id: number) => searchAndDownload(id),
  },
}))

const { toastSuccessMock, toastInfoMock, toastErrorMock } = vi.hoisted(() => ({
  toastSuccessMock: vi.fn(),
  toastInfoMock: vi.fn(),
  toastErrorMock: vi.fn(),
}))
vi.mock('@/services/toastService', () => ({
  useToast: () => ({
    success: toastSuccessMock,
    info: toastInfoMock,
    error: toastErrorMock,
    warning: vi.fn(),
    push: vi.fn(),
    dismiss: vi.fn(),
    subscribe: vi.fn(() => () => undefined),
  }),
}))

vi.mock('@/services/errorTracking', () => ({
  errorTracking: {
    captureException: vi.fn(),
  },
}))

type Vm = {
  audiobooks: Array<{ id: number }>
  showSearchAction: boolean
  toggleSearchAction: () => void
  searching: Record<number, boolean>
  viewMode: 'grid' | 'list'
  toggleViewMode: () => void
}

const LIBRARY = [
  { id: 1, title: 'Treasure Island', authors: ['Robert Louis Stevenson'], files: [] },
  { id: 2, title: 'Kidnapped', authors: ['Robert Louis Stevenson'], files: [] },
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

describe('AudiobooksView per-item automatic search action', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    searchAndDownload.mockImplementation(async () => ({
      success: true,
      indexerUsed: 'Test Indexer',
    }))
    localStorage.clear()
  })

  afterEach(() => {
    localStorage.clear()
  })

  it('renders no search button in either renderer while the toggle is off', async () => {
    const { wrapper } = await mountLibrary()

    expect(wrapper.find('button.search-btn-small').exists()).toBe(false)

    const vm = wrapper.vm as unknown as Vm
    vm.viewMode = 'list'
    await wrapper.vm.$nextTick()
    expect(wrapper.find('button.search-btn-small').exists()).toBe(false)
  })

  it('renders one search button per row in grid mode when the toggle is on', async () => {
    const { wrapper, vm } = await mountLibrary()

    vm.showSearchAction = true
    await wrapper.vm.$nextTick()

    const buttons = wrapper.findAll('button.search-btn-small')
    expect(buttons).toHaveLength(LIBRARY.length)
  })

  it('renders one search button per row in list mode when the toggle is on', async () => {
    const { wrapper, vm } = await mountLibrary()

    vm.showSearchAction = true
    vm.viewMode = 'list'
    await wrapper.vm.$nextTick()

    const buttons = wrapper.findAll('button.search-btn-small')
    expect(buttons).toHaveLength(LIBRARY.length)
  })

  it('pressing the button calls searchAndDownload with that row id and no other', async () => {
    const { wrapper, vm } = await mountLibrary()

    vm.showSearchAction = true
    await wrapper.vm.$nextTick()

    // The view sorts/filters before rendering, so index into vm.audiobooks (the
    // rendered order) rather than assuming the fixture's own ordering.
    const expectedId = vm.audiobooks[1].id
    const buttons = wrapper.findAll('button.search-btn-small')
    await buttons[1]?.trigger('click')
    await wrapper.vm.$nextTick()

    expect(searchAndDownload).toHaveBeenCalledTimes(1)
    expect(searchAndDownload).toHaveBeenCalledWith(expectedId)
  })

  it('disables the pressed row while it is in flight, and not a sibling row', async () => {
    let resolveSearch: (v: { success: boolean; indexerUsed?: string }) => void = () => {}
    searchAndDownload.mockImplementation(
      () =>
        new Promise((resolve) => {
          resolveSearch = resolve
        }),
    )

    const { wrapper, vm } = await mountLibrary()
    vm.showSearchAction = true
    await wrapper.vm.$nextTick()

    const buttons = wrapper.findAll('button.search-btn-small')
    void buttons[0]?.trigger('click')
    await wrapper.vm.$nextTick()

    const buttonsAfter = wrapper.findAll('button.search-btn-small')
    expect(buttonsAfter[0]?.attributes('disabled')).toBeDefined()
    expect(buttonsAfter[1]?.attributes('disabled')).toBeUndefined()

    resolveSearch({ success: true, indexerUsed: 'Test Indexer' })
    await new Promise((r) => setTimeout(r, 0))
    await wrapper.vm.$nextTick()

    const buttonsResolved = wrapper.findAll('button.search-btn-small')
    expect(buttonsResolved[0]?.attributes('disabled')).toBeUndefined()
  })

  it('round-trips the toggle through localStorage under the documented key', async () => {
    const { wrapper, vm } = await mountLibrary()

    vm.showSearchAction = true
    await wrapper.vm.$nextTick()
    expect(localStorage.getItem('listenarr.showSearchAction')).toBe('true')

    const { vm: vm2 } = await mountLibrary()
    expect(vm2.showSearchAction).toBe(true)
  })

  it('falls back to off when the stored value is absent or corrupt', async () => {
    localStorage.setItem('listenarr.showSearchAction', 'not-a-boolean')
    const { vm } = await mountLibrary()
    expect(vm.showSearchAction).toBe(false)

    localStorage.removeItem('listenarr.showSearchAction')
    const { vm: vm2 } = await mountLibrary()
    expect(vm2.showSearchAction).toBe(false)
  })

  it('does not break mount when localStorage throws (private mode)', async () => {
    const originalGetItem = Storage.prototype.getItem
    const originalSetItem = Storage.prototype.setItem
    Storage.prototype.getItem = () => {
      throw new Error('SecurityError: private mode')
    }
    Storage.prototype.setItem = () => {
      throw new Error('SecurityError: private mode')
    }

    try {
      const { wrapper, vm } = await mountLibrary()
      expect(vm.showSearchAction).toBe(false)

      vm.showSearchAction = true
      await wrapper.vm.$nextTick()
      // Toggling still works in-memory even though persistence throws.
      expect(vm.showSearchAction).toBe(true)
    } finally {
      Storage.prototype.getItem = originalGetItem
      Storage.prototype.setItem = originalSetItem
    }
  })
})
