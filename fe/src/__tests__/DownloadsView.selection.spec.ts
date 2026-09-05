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
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import DownloadsView from '@/views/activity/DownloadsView.vue'
import { useDownloadsStore } from '@/stores/downloads'
import type { Download } from '@/types'

const { mockShowConfirm } = vi.hoisted(() => ({
  mockShowConfirm: vi.fn(async () => true),
}))

vi.mock('@/composables/useConfirm', () => ({
  showConfirm: mockShowConfirm,
  useConfirm: () => ({}),
}))

vi.mock('@/services/signalr', () => ({
  signalRService: {
    onDownloadUpdate: vi.fn(() => () => undefined),
    onDownloadsList: vi.fn(() => () => undefined),
    onQueueUpdate: vi.fn(() => () => undefined),
    onAudiobookUpdate: vi.fn(() => () => undefined),
  },
}))

vi.mock('@/services/api', () => ({
  apiService: {
    getDownloads: vi.fn(async () => []),
    cancelDownload: vi.fn(async () => undefined),
    getCachedAnnounces: vi.fn(async () => ({ announces: [] })),
  },
}))

type DownloadsVm = {
  activeTab: 'active' | 'completed' | 'failed'
  selectedCount: number
  selectedDownloadIds: string[]
  hasSelectableDownloads: boolean
  toggleSelection: (id: string) => void
  selectAll: () => void
  cancelSelected: () => Promise<void>
}

const download = (id: string, status: Download['status']): Download =>
  ({
    id,
    title: `Title ${id}`,
    artist: '',
    album: '',
    originalUrl: '',
    status,
    progress: 0,
    totalSize: 100,
    downloadedSize: 0,
    downloadPath: '',
    finalPath: '',
    startedAt: new Date().toISOString(),
  }) as unknown as Download

// The Active tab holds four rows, three of which the per-row Cancel button
// would offer. Selections below are smaller than three on purpose: an
// implementation that cancelled everything cancellable would still pass a test
// where the selection happened to be everything.
const ACTIVE_FIXTURE = [
  download('d1', 'Downloading'),
  download('d2', 'Paused'),
  download('d3', 'Downloading'),
  download('d4', 'Queued'),
]

async function mountDownloads(items: Download[] = ACTIVE_FIXTURE) {
  const pinia = createPinia()
  setActivePinia(pinia)

  const store = useDownloadsStore()
  store.loadDownloads = vi.fn(async () => undefined)
  store.downloads = items.map((item) => ({ ...item }))

  const cancelDownload = vi.fn(async () => undefined)
  store.cancelDownload = cancelDownload

  const wrapper = mount(DownloadsView, {
    global: {
      plugins: [pinia],
      stubs: { InspectTorrentModal: true, CustomSelect: true },
    },
  })

  await flushPromises()
  return { wrapper, store, cancelDownload, vm: wrapper.vm as unknown as DownloadsVm }
}

const cancelledIds = (mock: ReturnType<typeof vi.fn>) =>
  mock.mock.calls.map((call) => call[0]).sort()

describe('DownloadsView multi-select', () => {
  beforeEach(() => {
    mockShowConfirm.mockClear()
    mockShowConfirm.mockResolvedValue(true)
  })

  it('cancels the ticked downloads and leaves the other cancellable ones alone', async () => {
    const { vm, cancelDownload } = await mountDownloads()

    vm.toggleSelection('d1')
    vm.toggleSelection('d4')
    await flushPromises()

    expect(vm.selectedCount).toBe(2)

    await vm.cancelSelected()
    await flushPromises()

    expect(cancelledIds(cancelDownload)).toEqual(['d1', 'd4'])
  })

  it('a paused download cannot be ticked, matching the row that offers no Cancel', async () => {
    const { vm } = await mountDownloads()

    vm.selectAll()
    await flushPromises()

    // d2 is Paused. The per-row Cancel button is not rendered for it, so bulk
    // cancel must not be able to reach it either.
    expect(vm.selectedDownloadIds).toEqual(['d1', 'd3', 'd4'])
  })

  it('renders a disabled checkbox for a row that offers no Cancel', async () => {
    const { wrapper } = await mountDownloads()

    // The list is virtualised, so assert by row rather than by position.
    const disabledByRow = new Map(
      wrapper
        .findAll('.download-select input[type="checkbox"]')
        .map((box) => [box.attributes('aria-label'), box.attributes('disabled') !== undefined]),
    )

    expect(disabledByRow.get('Select Title d1')).toBe(false)
    expect(disabledByRow.get('Select Title d2')).toBe(true)
    expect(disabledByRow.get('Select Title d3')).toBe(false)
  })

  it('skips an item that stopped being cancellable while the run was in flight', async () => {
    const { vm, store } = await mountDownloads()

    const cancelDownload = vi.fn(async (id: string) => {
      if (id === 'd1') {
        // The client reports d4 finished while the loop is partway through.
        store.downloads = store.downloads.map((item) =>
          item.id === 'd4' ? { ...item, status: 'Completed' as const } : item,
        )
      }
    })
    store.cancelDownload = cancelDownload

    vm.toggleSelection('d1')
    vm.toggleSelection('d4')
    await flushPromises()
    expect(vm.selectedCount).toBe(2)

    await vm.cancelSelected()
    await flushPromises()

    expect(cancelledIds(cancelDownload)).toEqual(['d1'])
  })

  it('one failed cancel does not stop the rest of the run', async () => {
    const { vm, store } = await mountDownloads()

    const cancelDownload = vi.fn(async (id: string) => {
      if (id === 'd1') throw new Error('client refused')
    })
    store.cancelDownload = cancelDownload

    vm.selectAll()
    await flushPromises()

    await vm.cancelSelected()
    await flushPromises()

    expect(cancelledIds(cancelDownload)).toEqual(['d1', 'd3', 'd4'])
  })

  it('declining the confirmation cancels nothing', async () => {
    const { vm, cancelDownload } = await mountDownloads()
    mockShowConfirm.mockResolvedValue(false)

    vm.selectAll()
    await flushPromises()

    await vm.cancelSelected()
    await flushPromises()

    expect(mockShowConfirm).toHaveBeenCalledTimes(1)
    expect(cancelDownload).not.toHaveBeenCalled()
    expect(vm.selectedCount).toBe(3)
  })

  it('names the number of downloads in the confirmation', async () => {
    const { vm } = await mountDownloads()

    vm.toggleSelection('d1')
    vm.toggleSelection('d3')
    await flushPromises()

    await vm.cancelSelected()
    await flushPromises()

    expect(mockShowConfirm.mock.calls[0]?.[0]).toContain('2')
  })

  it('a tab that cannot act on the selection reports a count of zero', async () => {
    const { vm } = await mountDownloads([
      ...ACTIVE_FIXTURE,
      download('d5', 'Failed'),
      download('d6', 'Completed'),
    ])

    vm.selectAll()
    await flushPromises()
    expect(vm.selectedCount).toBe(3)

    vm.activeTab = 'failed'
    await flushPromises()

    // Nothing on the Failed tab is cancellable, so the count cannot promise
    // rows that this tab has no action for.
    expect(vm.selectedCount).toBe(0)
    expect(vm.selectedDownloadIds).toEqual([])
  })

  it('returning to the tab brings the ticks back', async () => {
    const { vm, cancelDownload } = await mountDownloads([
      ...ACTIVE_FIXTURE,
      download('d5', 'Failed'),
      download('d6', 'Completed'),
    ])

    vm.toggleSelection('d1')
    vm.toggleSelection('d3')
    await flushPromises()

    vm.activeTab = 'completed'
    await flushPromises()
    expect(vm.selectedCount).toBe(0)

    // Glancing at another tab is not a decision to discard the selection, and a
    // bulk cancel after coming back acts on exactly what was ticked.
    vm.activeTab = 'active'
    await flushPromises()
    expect(vm.selectedDownloadIds).toEqual(['d1', 'd3'])

    await vm.cancelSelected()
    await flushPromises()

    expect(cancelledIds(cancelDownload)).toEqual(['d1', 'd3'])
  })

  it('hides the selection bar on a tab with nothing cancellable', async () => {
    const { wrapper, vm } = await mountDownloads([
      ...ACTIVE_FIXTURE,
      download('d5', 'Failed'),
      download('d6', 'Completed'),
    ])

    expect(wrapper.find('.downloads-selection-bar').exists()).toBe(true)

    vm.activeTab = 'completed'
    await flushPromises()

    expect(vm.hasSelectableDownloads).toBe(false)
    expect(wrapper.find('.downloads-selection-bar').exists()).toBe(false)
  })

  it('clears the selection once a run finishes', async () => {
    const { vm } = await mountDownloads()

    vm.toggleSelection('d3')
    await flushPromises()

    await vm.cancelSelected()
    await flushPromises()

    expect(vm.selectedCount).toBe(0)
  })

  it('Cancel Selected is disabled until something is ticked', async () => {
    const { wrapper, vm } = await mountDownloads()

    const button = wrapper
      .findAll('button')
      .find((candidate) => candidate.text().includes('Cancel Selected'))
    expect(button).toBeDefined()
    expect(button!.attributes('disabled')).toBeDefined()

    vm.toggleSelection('d1')
    await flushPromises()

    expect(button!.attributes('disabled')).toBeUndefined()
    expect(button!.text()).toContain('(1)')
  })

  it('ticking a row checkbox selects that download', async () => {
    const { wrapper, vm } = await mountDownloads()

    const boxes = wrapper.findAll('.download-select input[type="checkbox"]')
    await boxes[2]!.setValue(true)
    await flushPromises()

    expect(vm.selectedDownloadIds).toEqual(['d3'])
  })
})
