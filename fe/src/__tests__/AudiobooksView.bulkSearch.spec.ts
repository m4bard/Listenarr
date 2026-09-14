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

const confirmMock = vi.hoisted(() => vi.fn(async () => true))
vi.mock('@/composables/useConfirm', () => ({
  showConfirm: confirmMock,
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
  searchQuery: string
  audiobooks: Array<{ id: number }>
  searchTargets: Array<{ id: number }>
  bulkSearchRunning: boolean
  confirmBulkSearch: () => Promise<void>
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

const findSearchSelected = (wrapper: Awaited<ReturnType<typeof mountLibrary>>['wrapper']) =>
  wrapper.findAll('button.toolbar-btn').find((b) => b.text().includes('Search Selected'))

describe('AudiobooksView bulk automatic search', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    confirmMock.mockResolvedValue(true)
    searchAndDownload.mockImplementation(async () => ({
      success: true,
      indexerUsed: 'Test Indexer',
    }))
    localStorage.clear()
  })

  it('is absent with an empty selection and present once something is selected', async () => {
    const { wrapper, store } = await mountLibrary()

    expect(findSearchSelected(wrapper)).toBeUndefined()

    store.selectedIds.add(1)
    await wrapper.vm.$nextTick()

    expect(findSearchSelected(wrapper)?.exists()).toBe(true)
  })

  it('the count on the face equals the number of calls the run makes', async () => {
    const { wrapper, store, vm } = await mountLibrary()

    store.selectedIds.add(1)
    store.selectedIds.add(2)
    store.selectedIds.add(3)
    await wrapper.vm.$nextTick()

    const button = findSearchSelected(wrapper)
    expect(button?.text()).toContain('Search Selected (3)')
    expect(vm.searchTargets).toHaveLength(3)

    await vm.confirmBulkSearch()

    expect(searchAndDownload).toHaveBeenCalledTimes(3)
  })

  it('gates the run on confirmation; declining calls nothing', async () => {
    confirmMock.mockResolvedValueOnce(false)
    const { wrapper, store, vm } = await mountLibrary()

    store.selectedIds.add(1)
    store.selectedIds.add(2)
    await wrapper.vm.$nextTick()

    await vm.confirmBulkSearch()

    expect(confirmMock).toHaveBeenCalledTimes(1)
    expect(searchAndDownload).not.toHaveBeenCalled()
  })

  it('is confined to selectedIds and never reaches an unselected book', async () => {
    const { wrapper, store, vm } = await mountLibrary()

    store.selectedIds.add(1)
    store.selectedIds.add(3)
    await wrapper.vm.$nextTick()

    await vm.confirmBulkSearch()

    const searchedIds = searchAndDownload.mock.calls.map((c) => c[0]).sort()
    expect(searchedIds).toEqual([1, 3])
    expect(searchedIds).not.toContain(2)
    expect(searchedIds).not.toContain(4)
  })

  it('331f605f7 regression: with a filter active, Select All plus the search run reaches only the filtered rows', async () => {
    const { wrapper, store, vm } = await mountLibrary()

    vm.searchQuery = 'Stevenson'
    await wrapper.vm.$nextTick()
    expect(vm.audiobooks.map((b) => b.id).sort()).toEqual([1, 2])

    // Exercise the same Select All the toolbar button calls, handing it the filtered
    // computed exactly as AudiobooksView.vue does, per 331f605f7.
    store.selectAll(vm.audiobooks as unknown as Audiobook[])
    await wrapper.vm.$nextTick()
    expect([...store.selectedIds].sort()).toEqual([1, 2])

    await vm.confirmBulkSearch()

    const searchedIds = searchAndDownload.mock.calls.map((c) => c[0]).sort()
    expect(searchedIds).toEqual([1, 2])
    expect(searchedIds).not.toContain(3)
    expect(searchedIds).not.toContain(4)
  })

  it('honours the spacing between searches so one click cannot burst the indexers', async () => {
    // Mount under real timers first: mountLibrary awaits a real setTimeout(0) internally,
    // which never resolves once fake timers are installed ahead of it.
    const { wrapper, store, vm } = await mountLibrary()

    store.selectedIds.add(1)
    store.selectedIds.add(2)
    await wrapper.vm.$nextTick()

    vi.useFakeTimers()
    try {
      const runPromise = vm.confirmBulkSearch()

      // Let the confirmation promise and first search resolve.
      await vi.advanceTimersByTimeAsync(0)
      expect(searchAndDownload).toHaveBeenCalledTimes(1)

      // Before the spacing interval elapses, the second call must not have fired yet.
      await vi.advanceTimersByTimeAsync(500)
      expect(searchAndDownload).toHaveBeenCalledTimes(1)

      // After the full 1000ms spacing, the second call fires.
      await vi.advanceTimersByTimeAsync(600)
      expect(searchAndDownload).toHaveBeenCalledTimes(2)

      // The loop spaces after every call, including the last, so flush the
      // trailing wait before the run promise settles.
      await vi.runAllTimersAsync()
      await runPromise
    } finally {
      vi.useRealTimers()
    }
  })
})
