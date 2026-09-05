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
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import WantedView from '@/views/content/WantedView.vue'
import { useLibraryStore } from '@/stores/library'
import { useDownloadsStore } from '@/stores/downloads'
import type { Audiobook, Download } from '@/types'

const { mockSearchAndDownload } = vi.hoisted(() => ({
  mockSearchAndDownload: vi.fn(async (id: number) => ({
    success: true,
    indexerUsed: 'Stub Indexer',
    message: `queued ${id}`,
  })),
}))

vi.mock('@/services/api', () => ({
  apiService: {
    searchAndDownload: mockSearchAndDownload,
    getQualityProfiles: vi.fn(async () => []),
    getDownloads: vi.fn(async () => []),
    getImageUrl: vi.fn((url: string) => url || ''),
    getBootstrapConfig: vi.fn(async () => ({})),
    getStartupConfig: vi.fn(async () => ({})),
    getApplicationSettings: vi.fn(async () => ({})),
    updateAudiobook: vi.fn(async () => undefined),
  },
}))

type WantedVm = {
  filterText: string
  selectedCount: number
  selectedWantedIds: number[]
  toggleSelection: (id: number) => void
  selectAll: () => void
  clearSelection: () => void
  searchSelected: () => Promise<void>
  searchMissing: () => Promise<void>
  categorizedWanted: { all: Audiobook[]; missing: Audiobook[] }
}

const book = (id: number, title: string): Audiobook =>
  ({
    id,
    title,
    authors: ['An Author'],
    narrators: [],
    monitored: true,
    files: [],
    imageUrl: '',
  }) as unknown as Audiobook

const activeDownloadFor = (audiobookId: number): Download =>
  ({
    id: `dl-${audiobookId}`,
    title: `download ${audiobookId}`,
    artist: '',
    album: '',
    status: 'Downloading',
    progress: 10,
    totalSize: 100,
    downloadedSize: 10,
    audiobookId,
  }) as unknown as Download

// Every book in the fixture is missing, so "Search All" would touch four rows.
// Selections in these tests are deliberately smaller than that, otherwise an
// implementation that ignored the selection and looped the missing set would
// still pass.
const FIXTURE = [book(1, 'Alpha'), book(2, 'Beta'), book(3, 'Gamma'), book(4, 'Delta')]

async function mountWanted() {
  const pinia = createPinia()
  setActivePinia(pinia)

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/wanted', component: WantedView },
      { path: '/audiobooks/:id', component: { template: '<div />' } },
      { path: '/collection/author/:name', component: { template: '<div />' } },
      { path: '/collection/series/:name', component: { template: '<div />' } },
    ],
  })
  await router.push('/wanted')
  await router.isReady().catch(() => undefined)

  const libraryStore = useLibraryStore()
  libraryStore.fetchLibrary = vi.fn(async () => undefined)
  libraryStore.audiobooks = FIXTURE.map((b) => ({ ...b }))

  const downloadsStore = useDownloadsStore()
  downloadsStore.loadDownloads = vi.fn(async () => undefined)
  downloadsStore.downloads = []

  const wrapper = mount(WantedView, {
    global: {
      plugins: [pinia, router],
      stubs: {
        ManualSearchModal: true,
        ManualImportModal: true,
      },
    },
  })

  await flushPromises()
  return { wrapper, libraryStore, downloadsStore, vm: wrapper.vm as unknown as WantedVm }
}

// The search loops space their requests a second apart on purpose, so drive the
// clock rather than waiting on it.
async function runSearch(run: Promise<void>) {
  await vi.advanceTimersByTimeAsync(60_000)
  await run
  await flushPromises()
}

const searchedIds = () => mockSearchAndDownload.mock.calls.map((call) => call[0]).sort()

describe('WantedView multi-select', () => {
  beforeEach(() => {
    mockSearchAndDownload.mockClear()
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('Search Selected searches the ticked books and leaves the rest alone', async () => {
    const { vm } = await mountWanted()

    vm.toggleSelection(1)
    vm.toggleSelection(3)
    await flushPromises()

    expect(vm.selectedCount).toBe(2)
    expect(vm.categorizedWanted.missing.length).toBe(4)

    await runSearch(vm.searchSelected())

    expect(searchedIds()).toEqual([1, 3])
  })

  it('Search All still covers every missing book, whatever the filter and selection say', async () => {
    const { vm } = await mountWanted()

    // A filter narrow enough to leave one row, and a selection narrower still.
    vm.filterText = 'Alpha'
    vm.toggleSelection(1)
    await flushPromises()

    expect(vm.selectedCount).toBe(1)

    await runSearch(vm.searchMissing())

    // Search All is deliberately not scoped by either control. If this ever
    // reads the filter or the selection, that is a behaviour change that has to
    // be argued for rather than arrived at.
    expect(searchedIds()).toEqual([1, 2, 3, 4])
  })

  it('the filter narrows what Search Selected acts on', async () => {
    const { vm } = await mountWanted()

    vm.selectAll()
    await flushPromises()
    expect(vm.selectedCount).toBe(4)

    vm.filterText = 'Alpha'
    await flushPromises()
    expect(vm.selectedCount).toBe(1)

    await runSearch(vm.searchSelected())

    expect(searchedIds()).toEqual([1])
  })

  it('a book that starts downloading after being ticked is dropped from the count and the run', async () => {
    const { vm, downloadsStore } = await mountWanted()

    vm.toggleSelection(1)
    vm.toggleSelection(2)
    await flushPromises()
    expect(vm.selectedCount).toBe(2)

    // The downloads hub pushes an update: book 2 is now downloading.
    downloadsStore.downloads = [activeDownloadFor(2)]
    await flushPromises()

    expect(vm.selectedCount).toBe(1)

    await runSearch(vm.searchSelected())

    expect(searchedIds()).toEqual([1])
  })

  it('a book already downloading cannot be ticked at all', async () => {
    const { vm, downloadsStore } = await mountWanted()

    downloadsStore.downloads = [activeDownloadFor(4)]
    await flushPromises()

    vm.selectAll()
    await flushPromises()

    expect(vm.selectedWantedIds).toEqual([1, 2, 3])

    await runSearch(vm.searchSelected())

    expect(searchedIds()).toEqual([1, 2, 3])
  })

  it('renders a disabled checkbox for a row that is already downloading', async () => {
    const { wrapper, downloadsStore } = await mountWanted()

    downloadsStore.downloads = [activeDownloadFor(4)]
    await flushPromises()

    const rowBoxes = wrapper.findAll('.wanted-row .col-select input[type="checkbox"]')
    expect(rowBoxes).toHaveLength(4)
    expect(rowBoxes.map((box) => box.attributes('disabled') !== undefined)).toEqual([
      false,
      false,
      false,
      true,
    ])
  })

  it('ticking a row checkbox in the table selects that row', async () => {
    const { wrapper, vm } = await mountWanted()

    const rowBoxes = wrapper.findAll('.wanted-row .col-select input[type="checkbox"]')
    await rowBoxes[1]!.setValue(true)
    await flushPromises()

    expect(vm.selectedWantedIds).toEqual([2])
  })

  it('the header checkbox selects everything selectable, then clears it', async () => {
    const { wrapper, vm, downloadsStore } = await mountWanted()

    downloadsStore.downloads = [activeDownloadFor(4)]
    await flushPromises()

    const headerBox = wrapper.find('.wanted-header .col-select input[type="checkbox"]')
    await headerBox.setValue(true)
    await flushPromises()
    expect(vm.selectedWantedIds).toEqual([1, 2, 3])

    await headerBox.setValue(false)
    await flushPromises()
    expect(vm.selectedCount).toBe(0)
  })

  it('Search Selected is disabled until something is ticked', async () => {
    const { wrapper, vm } = await mountWanted()

    const button = wrapper
      .findAll('button')
      .find((candidate) => candidate.text().includes('Search Selected'))
    expect(button).toBeDefined()
    expect(button!.attributes('disabled')).toBeDefined()

    vm.toggleSelection(1)
    await flushPromises()

    expect(button!.attributes('disabled')).toBeUndefined()
    expect(button!.text()).toContain('(1)')
  })

  it('clears the selection once a run finishes', async () => {
    const { vm } = await mountWanted()

    vm.toggleSelection(2)
    await flushPromises()

    await runSearch(vm.searchSelected())

    expect(vm.selectedCount).toBe(0)
  })
})
