import { describe, it, beforeEach, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'

/**
 * A download that fails to import is never polled, never swept and never cleaned up, so the
 * only way it leaves the queue is a user removing it. These cover the controls that make that
 * possible: a per-row Remove on the terminal states, and the bulk clear that wires up the
 * DELETE /downloads/failed endpoint the frontend previously never called.
 */

const buildDownload = (id: string, status: string) => ({
  id,
  title: `Title ${id}`,
  artist: 'Author',
  album: 'Album',
  status,
  progress: status === 'Queued' ? 0 : 100,
  totalSize: 1000,
  downloadedSize: 1000,
  downloadPath: '',
  finalPath: '',
  startedAt: new Date().toISOString(),
  downloadClientId: 'client-a',
})

const stubMatchMedia = () => {
  vi.stubGlobal(
    'matchMedia',
    vi.fn().mockImplementation(() => ({
      matches: true,
      media: '(max-width: 768px)',
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  )
}

interface StoreOverrides {
  activeDownloads?: ReturnType<typeof buildDownload>[]
  completedDownloads?: ReturnType<typeof buildDownload>[]
  failedDownloads?: ReturnType<typeof buildDownload>[]
}

const mockStore = (overrides: StoreOverrides = {}) => {
  const store = {
    isLoading: false,
    activeDownloads: overrides.activeDownloads ?? [],
    completedDownloads: overrides.completedDownloads ?? [],
    failedDownloads: overrides.failedDownloads ?? [],
    loadDownloads: vi.fn(async () => undefined),
    cancelDownload: vi.fn(async () => undefined),
    removeDownload: vi.fn(async () => undefined),
    clearCompletedDownloads: vi.fn(async () => 2),
    clearFailedDownloads: vi.fn(async () => 3),
  }
  vi.doMock('@/stores/downloads', () => ({ useDownloadsStore: () => store }))
  return store
}

const mockConfirm = (answer: boolean) => {
  const showConfirm = vi.fn(async () => answer)
  vi.doMock('@/composables/confirmService', () => ({ showConfirm }))
  return showConfirm
}

const mockSupportingModules = () => {
  vi.doMock('@/services/toastService', () => ({
    useToast: () => ({ success: vi.fn(), error: vi.fn(), info: vi.fn() }),
  }))
  vi.doMock('@/services/errorTracking', () => ({
    errorTracking: { captureException: vi.fn() },
  }))
  vi.doMock('@/utils/logger', () => ({ logger: { warn: vi.fn(), debug: vi.fn() } }))
  vi.doMock('@/services/api', () => ({
    apiService: { getCachedAnnounces: vi.fn(async () => ({ announces: [] })) },
  }))
}

const mountView = async (tab: 'active' | 'completed' | 'failed') => {
  const { default: DownloadsView } = await import('@/views/activity/DownloadsView.vue')
  const wrapper = mount(DownloadsView, {
    global: {
      stubs: {
        CustomSelect: true,
        EmptyState: true,
        ProgressBar: true,
        InspectTorrentModal: true,
      },
    },
  })

  ;(wrapper.vm as unknown as { activeTab: string }).activeTab = tab
  await nextTick()
  await new Promise((resolve) => setTimeout(resolve, 10))
  await nextTick()

  return wrapper
}

describe('DownloadsView terminal download removal', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.clearAllMocks()
    stubMatchMedia()
  })

  it('renders Remove on Failed and ImportBlocked rows', async () => {
    mockStore({
      failedDownloads: [buildDownload('d-failed', 'Failed'), buildDownload('d-blocked', 'ImportBlocked')],
    })
    mockConfirm(true)
    mockSupportingModules()

    const wrapper = await mountView('failed')

    expect(wrapper.findAll('.download-card')).toHaveLength(2)
    expect(wrapper.findAll('.action-button.remove')).toHaveLength(2)

    wrapper.unmount()
  })

  it('does not render Remove on downloads that are still live', async () => {
    mockStore({
      activeDownloads: [buildDownload('d-queued', 'Queued'), buildDownload('d-downloading', 'Downloading')],
    })
    mockConfirm(true)
    mockSupportingModules()

    const wrapper = await mountView('active')

    expect(wrapper.findAll('.download-card')).toHaveLength(2)
    expect(wrapper.findAll('.action-button.remove')).toHaveLength(0)

    wrapper.unmount()
  })

  it('does not render Remove on Completed downloads, which the queue cleans up itself', async () => {
    mockStore({ completedDownloads: [buildDownload('d-completed', 'Completed')] })
    mockConfirm(true)
    mockSupportingModules()

    const wrapper = await mountView('completed')

    expect(wrapper.findAll('.download-card')).toHaveLength(1)
    expect(wrapper.findAll('.action-button.remove')).toHaveLength(0)

    wrapper.unmount()
  })

  it('removes the download through the store when Remove is clicked', async () => {
    const store = mockStore({ failedDownloads: [buildDownload('d-blocked', 'ImportBlocked')] })
    mockConfirm(true)
    mockSupportingModules()

    const wrapper = await mountView('failed')
    await wrapper.find('.action-button.remove').trigger('click')
    await nextTick()

    expect(store.removeDownload).toHaveBeenCalledWith('d-blocked')

    wrapper.unmount()
  })

  it('clears every terminal download in bulk once the removal is confirmed', async () => {
    const store = mockStore({
      failedDownloads: [buildDownload('d-failed', 'Failed'), buildDownload('d-blocked', 'ImportBlocked')],
    })
    const showConfirm = mockConfirm(true)
    mockSupportingModules()

    const wrapper = await mountView('failed')
    const clearButton = wrapper.find('.clear-button')
    expect(clearButton.exists()).toBe(true)
    expect(clearButton.text()).toContain('Clear Failed')

    await clearButton.trigger('click')
    await nextTick()

    expect(showConfirm).toHaveBeenCalled()
    expect(store.clearFailedDownloads).toHaveBeenCalled()

    wrapper.unmount()
  })

  it('leaves the queue alone when the bulk removal is cancelled', async () => {
    const store = mockStore({ failedDownloads: [buildDownload('d-failed', 'Failed')] })
    mockConfirm(false)
    mockSupportingModules()

    const wrapper = await mountView('failed')
    await wrapper.find('.clear-button').trigger('click')
    await nextTick()

    expect(store.clearFailedDownloads).not.toHaveBeenCalled()

    wrapper.unmount()
  })
})
