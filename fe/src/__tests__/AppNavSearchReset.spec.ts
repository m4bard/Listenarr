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
import { mount, type VueWrapper } from '@vue/test-utils'
import { computed } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory, type Router } from 'vue-router'

vi.mock('@/stores/moveJobs', () => ({
  useMoveJobsStore: () => ({
    trackedJobs: [],
    start: vi.fn(),
    stop: vi.fn(),
    loadActiveJobs: vi.fn(async () => undefined),
  }),
}))

vi.mock('@/stores/libraryDeleteOperations', () => ({
  useLibraryDeleteOperationsStore: () => ({
    operations: [],
    dismiss: vi.fn(),
    clearFinished: vi.fn(),
  }),
}))

vi.mock('@/stores/downloads', () => ({
  useDownloadsStore: () => ({
    activeDownloads: computed(() => []),
    loadDownloads: vi.fn(async () => undefined),
  }),
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    user: { authenticated: true },
    loadCurrentUser: vi.fn(async () => undefined),
    logout: vi.fn(async () => undefined),
  }),
}))

vi.mock('@/services/signalr', () => ({
  signalRService: {
    connect: vi.fn(async () => undefined),
    onConnected: vi.fn(() => () => undefined),
    onQueueUpdate: vi.fn(() => () => undefined),
    onFilesRemoved: vi.fn(() => () => undefined),
    onScanJobUpdate: vi.fn(() => () => undefined),
    onToast: vi.fn(() => () => undefined),
    onDownloadUpdate: vi.fn(() => () => undefined),
    onDownloadsList: vi.fn(() => () => undefined),
    onNotification: vi.fn(() => () => undefined),
  },
}))

const getLibrary = vi.fn(async () => [])

vi.mock('@/services/api', () => ({
  apiService: {
    getQueue: vi.fn(async () => []),
    getServiceHealth: vi.fn(async () => ({ version: '0.0.0' })),
    getBootstrapConfig: vi.fn(async () => ({ authenticationRequired: false })),
    getStartupConfig: vi.fn(async () => ({ authenticationRequired: false })),
    getLibrary: () => getLibrary(),
    getScanJobStatus: vi.fn(async () => ({ id: 'x', status: 'Completed' })),
    getImageUrl: vi.fn((url: string) => url || ''),
  },
}))

vi.mock('@/router', () => ({
  preloadRoute: vi.fn(),
}))

type NavSearchVm = {
  searchQuery: string
  suggestions: Array<{ id: number; title: string }>
  searching: boolean
  searchOpen: boolean
  onSearchInput: () => Promise<void>
}

const LIBRARY = [
  { id: 1, title: 'Treasure Island', authors: ['Robert Louis Stevenson'] },
  { id: 2, title: 'Treasure of the Sierra Madre', authors: ['B Traven'] },
]

async function mountApp(): Promise<{ wrapper: VueWrapper; router: Router; vm: NavSearchVm }> {
  const { default: AppComponent } = await import('@/App.vue')
  const { useLibraryStore } = await import('@/stores/library')

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'home', component: { template: '<div />' } },
      { path: '/wanted', name: 'wanted', component: { template: '<div />' } },
      {
        path: '/audiobooks/:id',
        name: 'audiobook-detail',
        component: { template: '<div />' },
      },
    ],
  })
  await router.push('/')
  await router.isReady().catch(() => {})

  const pinia = createPinia()
  setActivePinia(pinia)

  const wrapper = mount(AppComponent, {
    global: { stubs: ['RouterLink', 'RouterView'], plugins: [pinia, router] },
  })
  await new Promise((resolve) => setTimeout(resolve, 20))

  const libraryStore = useLibraryStore()
  libraryStore.audiobooks = LIBRARY as unknown as typeof libraryStore.audiobooks

  return { wrapper, router, vm: wrapper.vm as unknown as NavSearchVm }
}

describe('nav search panel across navigation', () => {
  let wrapper: VueWrapper | undefined

  beforeEach(() => {
    vi.resetModules()
    setActivePinia(createPinia())
    if (typeof (globalThis as unknown as { localStorage?: unknown }).localStorage === 'undefined') {
      Object.defineProperty(globalThis, 'localStorage', {
        value: {
          _store: {} as Record<string, string>,
          getItem(key: string) {
            return this._store[key] ?? null
          },
          setItem(key: string, value: string) {
            this._store[key] = value + ''
          },
          removeItem(key: string) {
            delete this._store[key]
          },
        },
        configurable: true,
      })
    }
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
    vi.clearAllMocks()
  })

  it('clears the query, the results and the mobile overlay when the route changes', async () => {
    const mounted = await mountApp()
    wrapper = mounted.wrapper
    const { router, vm } = mounted

    vm.searchOpen = true
    vm.searchQuery = 'Treasure'
    await vm.onSearchInput()
    await new Promise((resolve) => setTimeout(resolve, 350))
    await wrapper.vm.$nextTick()

    // Precondition: the panel is on screen with results in it.
    expect(vm.suggestions.length).toBe(2)
    expect(wrapper.find('.search-results-inline').exists()).toBe(true)

    await router.push('/wanted')
    await wrapper.vm.$nextTick()

    expect(vm.searchQuery).toBe('')
    expect(vm.suggestions).toEqual([])
    expect(vm.searchOpen).toBe(false)
    expect(wrapper.find('.search-results-inline').exists()).toBe(false)
  })

  it('cancels the pending debounce so an in-flight search cannot repopulate the panel', async () => {
    const mounted = await mountApp()
    wrapper = mounted.wrapper
    const { router, vm } = mounted

    // Type, then navigate before the 250ms debounce fires.
    vm.searchQuery = 'Treasure'
    await vm.onSearchInput()

    await router.push('/wanted')
    await wrapper.vm.$nextTick()

    expect(wrapper.find('.search-results-inline').exists()).toBe(false)

    // Well past the debounce window: the cancelled timer must never have run.
    await new Promise((resolve) => setTimeout(resolve, 400))
    await wrapper.vm.$nextTick()

    expect(vm.suggestions).toEqual([])
    expect(vm.searchQuery).toBe('')
    expect(vm.searching).toBe(false)
    expect(wrapper.find('.search-results-inline').exists()).toBe(false)
  })
})
