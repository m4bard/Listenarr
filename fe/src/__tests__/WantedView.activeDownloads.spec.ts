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
import { describe, it, beforeEach, expect, vi } from 'vitest'
import WantedView from '@/views/content/WantedView.vue'
import { useLibraryStore } from '@/stores/library'
import { useDownloadsStore } from '@/stores/downloads'

const searchAndDownloadMock = vi.fn(async () => ({
  success: true,
  indexerUsed: 'stub-indexer',
}))

// Mock api service the way sibling WantedView specs do, plus searchAndDownload for the
// bulk-search handler under test here.
vi.mock('@/services/api', () => ({
  apiService: {
    getImageUrl: vi.fn((url: string) => url || 'https://via.placeholder.com/300x450?text=No+Image'),
    getQualityProfiles: vi.fn(async () => []),
    searchAndDownload: (...args: unknown[]) =>
      searchAndDownloadMock(...(args as [number])),
  },
  getImageUrl: vi.fn((url: string) => url || 'https://via.placeholder.com/300x450?text=No+Image'),
  ensureImageCached: vi.fn(async () => true),
}))

describe('WantedView bulk search skips active downloads', () => {
  beforeEach(() => {
    const pinia = createPinia()
    setActivePinia(pinia)
    vi.clearAllMocks()
    searchAndDownloadMock.mockClear()
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

  it('does not re-search a missing book that already has an active download', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)

    const libraryStore = useLibraryStore()
    libraryStore.audiobooks = [
      { id: 1, title: 'Idle Missing Book', monitored: true, files: [] },
      { id: 2, title: 'Actively Downloading Book', monitored: true, files: [] },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']
    libraryStore.fetchLibrary = vi.fn(async () => undefined)

    const downloadsStore = useDownloadsStore()
    downloadsStore.downloads = [
      {
        id: 'd-active',
        title: 'Actively Downloading Book',
        status: 'Downloading',
        progress: 42,
        totalSize: 1000,
        downloadedSize: 420,
        audiobookId: 2,
        startedAt: new Date().toISOString(),
        metadata: {},
      },
    ] as ReturnType<typeof useDownloadsStore>['downloads']

    const wrapper = mount(WantedView, { global: { plugins: [pinia] } })
    await new Promise((r) => setTimeout(r, 10))

    const vm = wrapper.vm as unknown as { searchConfirmCount: number; confirmSearchMissing: () => Promise<void> }
    // The confirmation promises only the book it will search, not the one already downloading.
    expect(vm.searchConfirmCount).toBe(1)
    await vm.confirmSearchMissing()

    expect(searchAndDownloadMock).toHaveBeenCalledTimes(1)
    expect(searchAndDownloadMock).toHaveBeenCalledWith(1)
    expect(searchAndDownloadMock).not.toHaveBeenCalledWith(2)
  })

  it('control: a missing book whose only download is in a terminal state is still searched', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)

    const libraryStore = useLibraryStore()
    libraryStore.audiobooks = [
      { id: 3, title: 'Failed Then Missing Book', monitored: true, files: [] },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']
    libraryStore.fetchLibrary = vi.fn(async () => undefined)

    const downloadsStore = useDownloadsStore()
    downloadsStore.downloads = [
      {
        id: 'd-failed',
        title: 'Failed Then Missing Book',
        status: 'Failed',
        progress: 0,
        totalSize: 1000,
        downloadedSize: 0,
        audiobookId: 3,
        startedAt: new Date().toISOString(),
        metadata: {},
      },
    ] as ReturnType<typeof useDownloadsStore>['downloads']

    const wrapper = mount(WantedView, { global: { plugins: [pinia] } })
    await new Promise((r) => setTimeout(r, 10))

    const vm = wrapper.vm as unknown as { searchConfirmCount: number; confirmSearchMissing: () => Promise<void> }
    await vm.confirmSearchMissing()

    expect(searchAndDownloadMock).toHaveBeenCalledTimes(1)
    expect(searchAndDownloadMock).toHaveBeenCalledWith(3)
  })
})
