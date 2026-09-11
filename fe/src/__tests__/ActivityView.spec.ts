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

type ActivityItem = {
  id: string
  title?: string
  status?: string
  downloadClientId?: string
  downloadClient?: string
  downloadClientType?: string
  progress?: number
  canRemove?: boolean
}

type ActivityViewVm = {
  allActivityItems: ActivityItem[]
  filteredQueue: ActivityItem[]
  filterText: string
  showRemoveModal: boolean
  clientHasQueueEntry: boolean | null
  queueHealthClients: Array<{ name: string; isUnavailable?: boolean }>
  removeFromQueue: (item: ActivityItem) => Promise<void> | void
  confirmRemove: () => Promise<void>
}

const mockSignalR = () => {
  vi.doMock('@/services/signalr', () => ({
    signalRService: {
      onQueueUpdate: vi.fn(() => () => undefined),
    },
  }))
}

const mockApi = (overrides: Record<string, unknown> = {}) => {
  const apiService = {
    getQueue: vi.fn(async () => []),
    removeFromQueue: vi.fn(async () => undefined),
    cancelDownload: vi.fn(async () => undefined),
    retryBlockedImport: vi.fn(async () => ({
      message: 'queued',
      id: 'x',
      status: 'ImportPending',
    })),
    clearCompletedDownloads: vi.fn(async () => ({ message: 'cleared', count: 2 })),
    clearFailedDownloads: vi.fn(async () => ({ message: 'cleared', count: 1 })),
    ...overrides,
  }

  vi.doMock('@/services/api', () => ({
    apiService,
  }))

  return apiService
}

const mockConfigurationStore = (showCompletedExternalDownloads = false) => {
  vi.doMock('@/stores/configuration', () => ({
    useConfigurationStore: () => ({
      applicationSettings: { showCompletedExternalDownloads },
      loadApplicationSettings: vi.fn(async () => undefined),
    }),
  }))
}

const mockLibraryStore = (audiobooks: Array<{ id: number; title: string }> = []) => {
  vi.doMock('@/stores/library', () => ({
    useLibraryStore: () => ({
      audiobooks,
    }),
  }))
}

let currentMoveJobsStore: Record<string, unknown>

const mockMoveJobsStore = (overrides: Record<string, unknown> = {}) => {
  currentMoveJobsStore = {
    trackedJobs: [],
    start: vi.fn(),
    ...overrides,
  }

  return currentMoveJobsStore
}

const mockDownloadsStore = (overrides: Record<string, unknown> = {}) => {
  const store = {
    activeDownloads: [],
    completedDownloads: [],
    failedDownloads: [],
    loadDownloads: vi.fn(async () => undefined),
    ...overrides,
  }

  vi.doMock('@/stores/downloads', () => ({
    useDownloadsStore: () => store,
  }))

  return store
}

const mountActivityView = async () => {
  const { default: ActivityViewComponent } = await import('@/views/activity/ActivityView.vue')
  const wrapper = mount(ActivityViewComponent, {
    global: {
      stubs: {
        CustomSelect: true,
        RouterLink: { template: '<a><slot /></a>' },
      },
    },
  })

  await flushPromises()
  await new Promise((resolve) => setTimeout(resolve, 0))
  return wrapper
}

describe('ActivityView', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.clearAllMocks()
    mockMoveJobsStore()
    vi.doMock('@/stores/moveJobs', () => ({
      useMoveJobsStore: () => currentMoveJobsStore,
    }))
    vi.spyOn(globalThis, 'setInterval').mockReturnValue(
      1 as unknown as ReturnType<typeof setInterval>,
    )
    vi.spyOn(globalThis, 'clearInterval').mockImplementation(() => undefined)
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('shows active library move progress in the unified activity list', async () => {
    mockSignalR()
    mockApi()
    mockConfigurationStore(false)
    mockLibraryStore([{ id: 42, title: 'Book' }])
    mockDownloadsStore()
    mockMoveJobsStore({
      trackedJobs: [
        {
          jobId: 'job-1',
          audiobookId: 42,
          status: 'Running',
          progress: 37.5,
          phase: 'Copying',
          target: '/library/book',
        },
      ],
    })

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as ActivityViewVm
    const move = vm.allActivityItems.find((item) => item.id === 'move:job-1')

    expect(move).toMatchObject({ status: 'moving', progress: 37.5 })
    expect(wrapper.text()).toContain('38%')
    expect(wrapper.text()).toContain('Moving')
  })

  it('includes completed external downloads from the downloads store in the unified list', async () => {
    mockSignalR()
    mockApi()
    mockConfigurationStore(false)
    mockLibraryStore()
    mockDownloadsStore({
      completedDownloads: [
        {
          id: 'd1',
          status: 'Completed',
          progress: 100,
          downloadClientId: 'SABnzbd',
          startedAt: new Date().toISOString(),
          title: 'One',
          downloadedSize: 1000,
          totalSize: 1000,
        },
        {
          id: 'd2',
          status: 'Completed',
          progress: 100,
          downloadClientId: 'qbittorrent',
          startedAt: new Date().toISOString(),
          title: 'Two',
          downloadedSize: 2000,
          totalSize: 2000,
        },
        {
          id: 'd3',
          status: 'Completed',
          progress: 100,
          downloadClientId: 'transmission',
          startedAt: new Date().toISOString(),
          title: 'Three',
          downloadedSize: 3000,
          totalSize: 3000,
        },
        {
          id: 'd4',
          status: 'Completed',
          progress: 100,
          downloadClientId: 'nzbget',
          startedAt: new Date().toISOString(),
          title: 'Four',
          downloadedSize: 4000,
          totalSize: 4000,
        },
      ],
    })

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as ActivityViewVm

    expect(vm.allActivityItems.map((item) => item.id)).toEqual(
      expect.arrayContaining(['d1', 'd2', 'd3', 'd4']),
    )
    expect(vm.filteredQueue).toHaveLength(4)
  })

  it('filters the unified activity list by text', async () => {
    mockSignalR()
    mockApi()
    mockConfigurationStore(true)
    mockLibraryStore()
    mockDownloadsStore({
      completedDownloads: [
        {
          id: 'd1',
          status: 'Completed',
          progress: 100,
          downloadClientId: 'SABnzbd',
          startedAt: new Date().toISOString(),
          title: 'One',
          downloadedSize: 1000,
          totalSize: 1000,
        },
        {
          id: 'd2',
          status: 'Completed',
          progress: 100,
          downloadClientId: 'qbittorrent',
          startedAt: new Date().toISOString(),
          title: 'Two',
          downloadedSize: 2000,
          totalSize: 2000,
        },
      ],
    })

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as ActivityViewVm

    vm.filterText = 'two'
    await flushPromises()

    expect(vm.filteredQueue).toHaveLength(1)
    expect(vm.filteredQueue[0]?.id).toBe('d2')
  })

  it('removes a queue-backed item from the client', async () => {
    const queueItem = {
      id: 'q1',
      title: 'Queue Item',
      status: 'downloading',
      progress: 50,
      size: 1000,
      downloaded: 500,
      downloadClientId: 'qbittorrent',
      downloadClient: 'qbittorrent',
      canRemove: true,
    }

    mockSignalR()
    const apiService = mockApi({
      getQueue: vi.fn(async () => [queueItem]),
    })
    mockConfigurationStore(false)
    mockLibraryStore()
    mockDownloadsStore()

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as ActivityViewVm
    const item = vm.allActivityItems.find((entry) => entry.id === 'q1')

    expect(item).toBeDefined()

    await vm.removeFromQueue(item!)
    expect(vm.showRemoveModal).toBe(true)
    expect(vm.clientHasQueueEntry).toBe(true)

    await vm.confirmRemove()
    expect(apiService.removeFromQueue).toHaveBeenCalledWith('q1', 'qbittorrent')
  })

  it('offers Listenarr-only removal when an external item is no longer in the client queue', async () => {
    mockSignalR()
    const apiService = mockApi({
      getQueue: vi.fn(async () => []),
    })
    mockConfigurationStore(false)
    mockLibraryStore()
    const downloadsStore = mockDownloadsStore({
      completedDownloads: [
        {
          id: 'ext-1',
          status: 'Completed',
          progress: 100,
          downloadClientId: 'SABnzbd',
          startedAt: new Date().toISOString(),
          title: 'Completed External',
          downloadedSize: 100,
          totalSize: 100,
        },
      ],
    })

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as ActivityViewVm
    const item = vm.allActivityItems.find((entry) => entry.id === 'ext-1')

    expect(item).toBeDefined()

    await vm.removeFromQueue(item!)
    expect(vm.showRemoveModal).toBe(true)
    expect(vm.clientHasQueueEntry).toBe(false)

    await vm.confirmRemove()
    expect(apiService.cancelDownload).toHaveBeenCalledWith('ext-1')
    expect(downloadsStore.loadDownloads).toHaveBeenCalled()
  })

  it('deduplicates failed queue items against failed download records', async () => {
    const queueFailed = {
      id: 'q1',
      title: 'Queue Failed',
      status: 'failed',
      progress: 0,
      size: 0,
      downloaded: 0,
      downloadClientId: 'qbittorrent',
      downloadClient: 'qbittorrent',
    }

    mockSignalR()
    mockApi({
      getQueue: vi.fn(async () => [queueFailed]),
    })
    mockConfigurationStore(false)
    mockLibraryStore()
    mockDownloadsStore({
      failedDownloads: [
        {
          id: 'q1',
          status: 'Failed',
          progress: 0,
          downloadClientId: 'qbittorrent',
          title: 'Queue Failed (DB copy)',
        },
        { id: 'd1', status: 'Failed', progress: 0, downloadClientId: 'DDL', title: 'DDL Failed' },
      ],
    })

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as ActivityViewVm

    expect(vm.allActivityItems).toHaveLength(2)
    expect(vm.allActivityItems.filter((item) => item.id === 'q1')).toHaveLength(1)
    expect(vm.allActivityItems.some((item) => item.id === 'd1')).toBe(true)
  })

  it('removes a failed DDL download through Listenarr cancellation', async () => {
    mockSignalR()
    const apiService = mockApi()
    mockConfigurationStore(false)
    mockLibraryStore()
    const downloadsStore = mockDownloadsStore({
      failedDownloads: [
        { id: 'd1', status: 'Failed', progress: 0, downloadClientId: 'DDL', title: 'DDL Failed' },
      ],
    })

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as ActivityViewVm
    const item = vm.allActivityItems.find((entry) => entry.id === 'd1')

    expect(item).toBeDefined()

    await vm.removeFromQueue(item!)
    expect(vm.clientHasQueueEntry).toBe(true)

    await vm.confirmRemove()
    expect(apiService.cancelDownload).toHaveBeenCalledWith('d1')
    expect(downloadsStore.loadDownloads).toHaveBeenCalled()
  })

  it('maps ImportPending and ImportBlocked downloads to activity rows', async () => {
    mockSignalR()
    mockApi()
    mockConfigurationStore(false)
    mockLibraryStore()
    mockDownloadsStore({
      activeDownloads: [
        {
          id: 'd-importpending',
          title: 'Import Pending',
          status: 'ImportPending',
          progress: 99,
          totalSize: 1000,
          downloadedSize: 990,
          downloadClientId: 'qbittorrent',
          startedAt: new Date().toISOString(),
        },
      ],
      failedDownloads: [
        {
          id: 'd-importblocked',
          title: 'Import Blocked',
          status: 'ImportBlocked',
          progress: 100,
          totalSize: 1000,
          downloadedSize: 1000,
          downloadClientId: 'qbittorrent',
          startedAt: new Date().toISOString(),
        },
      ],
    })

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as ActivityViewVm

    expect(vm.allActivityItems.find((item) => item.id === 'd-importpending')?.status).toBe(
      'importpending',
    )
    expect(vm.allActivityItems.find((item) => item.id === 'd-importblocked')?.status).toBe(
      'importblocked',
    )
  })

  it('shows unavailable client health even when no queue items are returned', async () => {
    mockSignalR()
    mockApi({
      getQueue: vi.fn(async () => ({
        items: [],
        clients: [
          {
            clientId: 'qb-1',
            clientName: 'qBittorrent',
            clientType: 'qbittorrent',
            snapshotState: 'unavailable',
            isStaleSnapshot: false,
            isUnavailable: true,
            snapshotFailureReason: 'timeout',
            itemCount: 0,
          },
        ],
        generatedAt: new Date().toISOString(),
        hasStaleData: false,
        hasUnavailableClients: true,
      })),
    })
    mockConfigurationStore(false)
    mockLibraryStore()
    mockDownloadsStore()

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as ActivityViewVm

    expect(vm.queueHealthClients).toHaveLength(1)
    expect(vm.queueHealthClients[0]?.name).toBe('qBittorrent')
    expect(wrapper.text()).toContain('Some queue data is unavailable')
    expect(wrapper.text()).toContain('qBittorrent unavailable after a timeout')
  })

  it('prefers the queue snapshot over a DDL active download with the same tracked id', async () => {
    mockSignalR()
    mockApi({
      getQueue: vi.fn(async () => ({
        items: [
          {
            id: 'ddl-alice',
            title: 'Alice in Wonderland',
            status: 'downloading',
            progress: 42,
            size: 1000,
            downloaded: 420,
            downloadSpeed: 0,
            quality: 'M4B',
            downloadClient: 'Direct Download',
            downloadClientId: 'DDL',
            downloadClientType: 'ddl',
            addedAt: new Date().toISOString(),
            canPause: false,
            canRemove: true,
          },
        ],
        clients: [],
        generatedAt: new Date().toISOString(),
        hasStaleData: false,
        hasUnavailableClients: false,
      })),
    })
    mockConfigurationStore(false)
    mockLibraryStore()
    mockDownloadsStore({
      activeDownloads: [
        {
          id: 'ddl-alice',
          title: 'Alice in Wonderland',
          status: 'Queued',
          progress: 0,
          totalSize: 1000,
          downloadedSize: 0,
          downloadClientId: 'DDL',
          startedAt: new Date().toISOString(),
        },
      ],
    })

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as ActivityViewVm

    expect(vm.allActivityItems).toHaveLength(1)
    expect(vm.allActivityItems[0]?.id).toBe('ddl-alice')
    expect(vm.allActivityItems[0]?.status).toBe('downloading')
    expect(vm.allActivityItems[0]?.progress).toBe(42)
    expect(vm.allActivityItems[0]?.downloadClientType).toBe('ddl')
  })

  it('prefers the queue snapshot over an external active download with the same tracked id', async () => {
    mockSignalR()
    mockApi({
      getQueue: vi.fn(async () => ({
        items: [
          {
            id: 'tracked-artemis',
            title: 'Artemis',
            status: 'completed',
            progress: 100,
            size: 489100000,
            downloaded: 489100000,
            downloadSpeed: 77300,
            quality: 'Unknown',
            downloadClient: 'QBIT',
            downloadClientId: 'qb-1',
            downloadClientType: 'qbittorrent',
            addedAt: new Date().toISOString(),
            canPause: false,
            canRemove: true,
          },
        ],
        clients: [],
        generatedAt: new Date().toISOString(),
        hasStaleData: false,
        hasUnavailableClients: false,
      })),
    })
    mockConfigurationStore(true)
    mockLibraryStore()
    mockDownloadsStore({
      activeDownloads: [
        {
          id: 'tracked-artemis',
          title: 'Artemis',
          status: 'Downloading',
          progress: 100,
          totalSize: 489100000,
          downloadedSize: 489100000,
          downloadClientId: 'qb-1',
          startedAt: new Date().toISOString(),
        },
      ],
    })

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as ActivityViewVm

    expect(vm.allActivityItems).toHaveLength(1)
    expect(vm.allActivityItems[0]?.id).toBe('tracked-artemis')
    expect(vm.allActivityItems[0]?.status).toBe('completed')
    expect(vm.allActivityItems[0]?.title).toBe('Artemis')
  })

  it('defaults the queue to the soonest ETA first, with unknown ETAs last', async () => {
    mockSignalR()
    mockApi({
      getQueue: vi.fn(async () => [
        { id: 'slow', title: 'Slow', status: 'Downloading', progress: 10, eta: 900 },
        { id: 'none', title: 'NoEta', status: 'Downloading', progress: 5 },
        { id: 'soon', title: 'Soon', status: 'Downloading', progress: 90, eta: 30 },
      ]),
    })
    mockConfigurationStore(false)
    mockLibraryStore()
    mockDownloadsStore()
    mockMoveJobsStore()

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as { sortedQueue: { id: string }[]; sortKey: string }

    // The sibling apps default to time remaining ascending, so the row finishing next is on top.
    expect(vm.sortKey).toBe('eta')
    expect(vm.sortedQueue.map((item) => item.id)).toEqual(['soon', 'slow', 'none'])
  })

  it('keeps a missing value last when the sort direction is reversed', async () => {
    mockSignalR()
    mockApi({
      getQueue: vi.fn(async () => [
        { id: 'slow', title: 'Slow', status: 'Downloading', progress: 10, eta: 900 },
        { id: 'none', title: 'NoEta', status: 'Downloading', progress: 5 },
        { id: 'soon', title: 'Soon', status: 'Downloading', progress: 90, eta: 30 },
      ]),
    })
    mockConfigurationStore(false)
    mockLibraryStore()
    mockDownloadsStore()
    mockMoveJobsStore()

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as {
      sortedQueue: { id: string }[]
      toggleSort: (key: string) => void
    }

    vm.toggleSort('eta')
    await wrapper.vm.$nextTick()

    // Reversed, so the longest ETA leads. The row with no ETA still sorts last rather than being
    // promoted to the top by the flip: an unknown ETA is not the longest one.
    expect(vm.sortedQueue.map((item) => item.id)).toEqual(['slow', 'soon', 'none'])
  })

  it('sorts by when an item was added, which was previously not reachable from the page', async () => {
    mockSignalR()
    mockApi({
      getQueue: vi.fn(async () => [
        {
          id: 'newer',
          title: 'Newer',
          status: 'Downloading',
          progress: 1,
          addedAt: '2026-09-02T12:00:00Z',
        },
        {
          id: 'older',
          title: 'Older',
          status: 'Downloading',
          progress: 1,
          addedAt: '2026-09-01T12:00:00Z',
        },
      ]),
    })
    mockConfigurationStore(false)
    mockLibraryStore()
    mockDownloadsStore()
    mockMoveJobsStore()

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as {
      sortedQueue: { id: string }[]
      toggleSort: (key: string) => void
    }

    vm.toggleSort('added')
    await wrapper.vm.$nextTick()

    expect(vm.sortedQueue.map((item) => item.id)).toEqual(['older', 'newer'])
  })
  const queueRow = (id: string, status = 'downloading') => ({
    id,
    title: id,
    quality: '',
    status,
    progress: 0,
    size: 0,
    downloaded: 0,
    downloadSpeed: 0,
    downloadClient: 'client',
    downloadClientId: 'qbittorrent',
    downloadClientType: 'external',
    addedAt: new Date().toISOString(),
    canPause: false,
    canRemove: true,
  })

  type QueueSelectionVm = {
    selectedIds: Set<string>
    toggleSelection: (id: string) => void
    removeSelected: () => Promise<void>
    retrySelected: () => Promise<void>
  }

  const mockToasts = () => {
    const toasts = { success: vi.fn(), warning: vi.fn(), error: vi.fn(), info: vi.fn() }
    vi.doMock('@/services/toastService', () => ({ useToast: () => toasts }))
    return toasts
  }

  it('removes every selected row with one call each and one summary toast', async () => {
    mockSignalR()
    const toasts = mockToasts()
    const api = mockApi({ getQueue: vi.fn(async () => [queueRow('q1'), queueRow('q2')]) })
    mockConfigurationStore(false)
    mockLibraryStore()
    mockDownloadsStore()

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as QueueSelectionVm

    vm.toggleSelection('q1')
    vm.toggleSelection('q2')
    await vm.removeSelected()
    await flushPromises()

    expect(api.removeFromQueue).toHaveBeenCalledTimes(2)
    expect((api.removeFromQueue as ReturnType<typeof vi.fn>).mock.calls.map((c) => c[0])).toEqual([
      'q1',
      'q2',
    ])
    expect(toasts.success).toHaveBeenCalledTimes(1)
    expect(toasts.success.mock.calls[0][1]).toBe('Removed 2 of 2.')
    expect(Array.from(vm.selectedIds)).toEqual([])
  })

  it('leaves the rows that failed selected and says so once', async () => {
    mockSignalR()
    const toasts = mockToasts()
    const api = mockApi({
      getQueue: vi.fn(async () => [queueRow('q1'), queueRow('q2')]),
      removeFromQueue: vi.fn(async (id: string) => {
        if (id === 'q2') throw new Error('client refused')
      }),
    })
    mockConfigurationStore(false)
    mockLibraryStore()
    mockDownloadsStore()

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as QueueSelectionVm

    vm.toggleSelection('q1')
    vm.toggleSelection('q2')
    await vm.removeSelected()
    await flushPromises()

    expect(api.removeFromQueue).toHaveBeenCalledTimes(2)
    expect(toasts.warning).toHaveBeenCalledTimes(1)
    expect(toasts.warning.mock.calls[0][1]).toBe('Removed 1 of 2. 1 failed.')
    expect(Array.from(vm.selectedIds)).toEqual(['q2'])
  })

  it('drops an id from the selection once it stops arriving in the queue', async () => {
    mockSignalR()
    mockToasts()
    let rows = [queueRow('q1'), queueRow('q2')]
    mockApi({ getQueue: vi.fn(async () => rows) })
    mockConfigurationStore(false)
    mockLibraryStore()
    mockDownloadsStore()

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as QueueSelectionVm & { refreshQueue: () => Promise<void> }

    vm.toggleSelection('q1')
    vm.toggleSelection('q2')
    rows = [queueRow('q1')]
    await vm.refreshQueue()
    await flushPromises()

    expect(Array.from(vm.selectedIds)).toEqual(['q1'])
  })

  it('select all takes the filtered rows, not the whole queue', async () => {
    mockSignalR()
    mockToasts()
    mockApi({ getQueue: vi.fn(async () => [queueRow('q1'), queueRow('q2')]) })
    mockConfigurationStore(false)
    mockLibraryStore()
    mockDownloadsStore()

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as QueueSelectionVm & {
      filterText: string
      onSelectAll: (checked: boolean) => void
    }

    vm.filterText = 'q1'
    await wrapper.vm.$nextTick()
    vm.onSelectAll(true)

    expect(Array.from(vm.selectedIds)).toEqual(['q1'])
  })

  it('retries every selected row when all of them are import blocked', async () => {
    mockSignalR()
    const toasts = mockToasts()
    const api = mockApi({
      getQueue: vi.fn(async () => [
        queueRow('q1', 'importblocked'),
        queueRow('q2', 'importblocked'),
      ]),
    })
    mockConfigurationStore(false)
    mockLibraryStore()
    mockDownloadsStore()

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as QueueSelectionVm

    vm.toggleSelection('q1')
    vm.toggleSelection('q2')
    await vm.retrySelected()
    await flushPromises()

    expect(api.retryBlockedImport).toHaveBeenCalledTimes(2)
    expect(toasts.success.mock.calls[0][1]).toBe('Retried 2 of 2.')
  })
  it('unchecking select all releases only the rows the filter is showing', async () => {
    mockSignalR()
    mockToasts()
    mockApi({ getQueue: vi.fn(async () => [queueRow('q1'), queueRow('q2')]) })
    mockConfigurationStore(false)
    mockLibraryStore()
    mockDownloadsStore()

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as QueueSelectionVm & {
      filterText: string
      onSelectAll: (checked: boolean) => void
    }

    vm.toggleSelection('q1')
    vm.toggleSelection('q2')
    vm.filterText = 'q1'
    await wrapper.vm.$nextTick()
    vm.onSelectAll(false)

    expect(Array.from(vm.selectedIds)).toEqual(['q2'])
  })

  it('a bulk run leaves the selected rows the filter is hiding alone', async () => {
    mockSignalR()
    mockToasts()
    const api = mockApi({ getQueue: vi.fn(async () => [queueRow('q1'), queueRow('q2')]) })
    mockConfigurationStore(false)
    mockLibraryStore()
    mockDownloadsStore()

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as QueueSelectionVm & { filterText: string }

    vm.toggleSelection('q1')
    vm.toggleSelection('q2')
    vm.filterText = 'q1'
    await wrapper.vm.$nextTick()
    await vm.removeSelected()
    await flushPromises()

    expect(api.removeFromQueue).toHaveBeenCalledTimes(1)
    expect((api.removeFromQueue as ReturnType<typeof vi.fn>).mock.calls[0][0]).toBe('q1')
    expect(Array.from(vm.selectedIds)).toEqual(['q2'])
  })

  it('removes a direct download through cancelDownload instead of the client queue', async () => {
    mockSignalR()
    mockToasts()
    const api = mockApi({
      getQueue: vi.fn(async () => [{ ...queueRow('ddl-1'), downloadClientType: 'DDL' }]),
    })
    mockConfigurationStore(false)
    mockLibraryStore()
    mockDownloadsStore()

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as QueueSelectionVm

    vm.toggleSelection('ddl-1')
    await vm.removeSelected()
    await flushPromises()

    expect(api.cancelDownload).toHaveBeenCalledTimes(1)
    expect((api.cancelDownload as ReturnType<typeof vi.fn>).mock.calls[0][0]).toBe('ddl-1')
    expect(api.removeFromQueue).not.toHaveBeenCalled()
  })

  it('cancels a store download that the client queue does not carry', async () => {
    mockSignalR()
    mockToasts()
    const api = mockApi({ getQueue: vi.fn(async () => []) })
    mockConfigurationStore(false)
    mockLibraryStore()
    mockDownloadsStore({
      activeDownloads: [
        {
          id: 'ext-1',
          title: 'External',
          status: 'Downloading',
          progress: 40,
          totalSize: 1000,
          downloadedSize: 400,
          downloadClientId: 'qb-1',
          startedAt: new Date().toISOString(),
        },
      ],
    })

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as QueueSelectionVm & { allActivityItems: ActivityItem[] }

    expect(vm.allActivityItems.map((item) => item.id)).toEqual(['ext-1'])

    vm.toggleSelection('ext-1')
    await vm.removeSelected()
    await flushPromises()

    expect(api.cancelDownload).toHaveBeenCalledTimes(1)
    expect((api.cancelDownload as ReturnType<typeof vi.fn>).mock.calls[0][0]).toBe('ext-1')
    expect(api.removeFromQueue).not.toHaveBeenCalled()
  })

  it('disables select all once the filter leaves no removable row', async () => {
    mockSignalR()
    mockToasts()
    mockApi({ getQueue: vi.fn(async () => [queueRow('q1')]) })
    mockConfigurationStore(false)
    mockLibraryStore()
    mockDownloadsStore()
    mockMoveJobsStore({
      trackedJobs: [
        {
          jobId: 'job-1',
          audiobookId: 42,
          status: 'Running',
          progress: 10,
          phase: 'Copying',
        },
      ],
    })

    const wrapper = await mountActivityView()
    const vm = wrapper.vm as unknown as { filterText: string }

    expect(wrapper.get('[data-test="queue-select-all"]').attributes('disabled')).toBeUndefined()

    vm.filterText = 'library move'
    await wrapper.vm.$nextTick()

    expect(wrapper.get('[data-test="queue-select-all"]').attributes('disabled')).toBeDefined()
  })
})
