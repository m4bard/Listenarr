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
import WantedView from '@/views/content/WantedView.vue'
import { useLibraryStore } from '@/stores/library'

const searchAndDownload = vi.fn(async () => ({ success: false, message: 'No matches found' }))

vi.mock('@/services/api', () => ({
  apiService: {
    getImageUrl: vi.fn((url: string) => url || ''),
    getQualityProfiles: vi.fn(async () => []),
    searchAndDownload: (id: number) => searchAndDownload(id),
    updateAudiobook: vi.fn(async () => undefined),
  },
  getImageUrl: vi.fn((url: string) => url || ''),
  ensureImageCached: vi.fn(async () => true),
}))

type Vm = {
  filterText: string
  searchTargets: Array<{ id: number }>
  searchButtonLabel: string
  showSearchConfirm: boolean
  requestSearchMissing: () => void
  confirmSearchMissing: () => Promise<void>
}

const LIBRARY = [
  { id: 1, title: 'Alpha Rising', authors: ['Ann Author'], monitored: true, files: [] },
  { id: 2, title: 'Beta Descending', authors: ['Bob Bard'], monitored: true, files: [] },
  { id: 3, title: 'Gamma Waits', authors: ['Ann Author'], monitored: true, files: [] },
]

async function mountWanted() {
  const pinia = createPinia()
  setActivePinia(pinia)

  const store = useLibraryStore()
  store.audiobooks = LIBRARY as unknown as ReturnType<typeof useLibraryStore>['audiobooks']
  store.fetchLibrary = vi.fn(async () => undefined)

  const wrapper = mount(WantedView, { global: { plugins: [pinia] } })
  await new Promise((r) => setTimeout(r, 10))
  return wrapper
}

describe('WantedView bulk search scope', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    searchAndDownload.mockImplementation(async () => ({
      success: false,
      message: 'No matches found',
    }))
    vi.stubGlobal(
      'matchMedia',
      vi.fn().mockImplementation(() => ({
        matches: false,
        media: '',
        onchange: null,
        addListener: vi.fn(),
        removeListener: vi.fn(),
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        dispatchEvent: vi.fn(),
      })),
    )
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('searches only the filtered subset, not the whole wanted list', async () => {
    const wrapper = await mountWanted()
    const vm = wrapper.vm as unknown as Vm

    vm.filterText = 'Ann Author'
    await wrapper.vm.$nextTick()

    expect(vm.searchTargets.map((b) => b.id)).toEqual([1, 3])

    await vm.confirmSearchMissing()

    const searchedIds = searchAndDownload.mock.calls.map((c) => c[0])
    expect(searchedIds).toEqual([1, 3])
    expect(searchedIds).not.toContain(2)
  })

  it('disables the button when the filter matches nothing, even though the base list is populated', async () => {
    const wrapper = await mountWanted()
    const vm = wrapper.vm as unknown as Vm

    vm.filterText = 'no such book anywhere'
    await wrapper.vm.$nextTick()

    // The unfiltered wanted list is still three books; the filtered one is empty.
    expect(vm.searchTargets).toHaveLength(0)

    const button = wrapper.findAll('button').find((b) => b.text().includes('Search'))
    expect(button?.attributes('disabled')).toBeDefined()

    // And pressing it anyway must not open the confirmation or search anything.
    vm.requestSearchMissing()
    await wrapper.vm.$nextTick()
    expect(vm.showSearchConfirm).toBe(false)

    await vm.confirmSearchMissing()
    expect(searchAndDownload).not.toHaveBeenCalled()
  })

  it('puts the count in the label while a filter is active', async () => {
    const wrapper = await mountWanted()
    const vm = wrapper.vm as unknown as Vm

    expect(vm.searchButtonLabel).toBe('Search All')

    vm.filterText = 'Ann Author'
    await wrapper.vm.$nextTick()
    expect(vm.searchButtonLabel).toBe('Search 2 (missing)')

    vm.filterText = 'Beta'
    await wrapper.vm.$nextTick()
    expect(vm.searchButtonLabel).toBe('Search 1 (missing)')
  })

  it('asks for confirmation before running, and runs nothing until confirmed', async () => {
    const wrapper = await mountWanted()
    const vm = wrapper.vm as unknown as Vm

    vm.requestSearchMissing()
    await wrapper.vm.$nextTick()

    expect(vm.showSearchConfirm).toBe(true)
    expect(searchAndDownload).not.toHaveBeenCalled()

    await vm.confirmSearchMissing()
    expect(searchAndDownload).toHaveBeenCalledTimes(3)
    expect(vm.showSearchConfirm).toBe(false)
  })
})
