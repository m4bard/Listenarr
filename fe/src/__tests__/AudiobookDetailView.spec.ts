import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { describe, it, beforeEach, expect, vi } from 'vitest'
import { API_BASE_PATH } from '@/services/apiBase'
import { useLibraryStore } from '@/stores/library'
import { ensureImageCached } from '@/services/api'
import AudiobookDetailViewCmp from '@/views/library/AudiobookDetailView.vue'
const routerPushMock = vi.fn()
// Mock useRoute to provide params for the detail view
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { id: '5' } }),
  useRouter: () => ({ push: routerPushMock }),
}))

// Mock api service ensureImageCached and getImageUrl
vi.mock('@/services/api', () => ({
  apiService: {
    getImageUrl: vi.fn((url: string) => url || 'https://via.placeholder.com/300x450?text=No+Image'),
    getQualityProfiles: vi.fn(async () => []),
    getLibrary: vi.fn(async () => []),
  },
  ensureImageCached: vi.fn(async () => true),
}))

// Mock signalr service to provide missing hooks (e.g., onScanJobUpdate)
vi.mock('@/services/signalr', () => ({
  signalRService: {
    connect: vi.fn(async () => undefined),
    onQueueUpdate: vi.fn(() => () => undefined),
    onFilesRemoved: vi.fn(() => () => undefined),
    onToast: vi.fn(() => () => undefined),
    onAudiobookUpdate: vi.fn(() => () => undefined),
    onDownloadUpdate: vi.fn(() => () => undefined),
    onDownloadsList: vi.fn(() => () => undefined),
    onScanJobUpdate: vi.fn(() => () => undefined),
  },
}))

describe('AudiobookDetailView image recache behavior', () => {
  beforeEach(() => {
    const pinia = createPinia()
    setActivePinia(pinia)
    vi.clearAllMocks()
  })

  it('calls ensureImageCached for the audiobook cover on load', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const imagePath = `${API_BASE_PATH}/images/ASIN000005`
    const store = useLibraryStore()
    store.audiobooks = [
      { id: 5, title: 'Detail Book', imageUrl: imagePath, files: [] },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']

    store.fetchLibrary = vi.fn(async () => undefined)

    mount(AudiobookDetailViewCmp, { global: { plugins: [pinia] } })
    await new Promise((r) => setTimeout(r, 10))

    expect(ensureImageCached).toHaveBeenCalled()
    const ensureImageCachedMock = ensureImageCached as unknown as { mock: { calls: Array<[string]> } }
    expect(ensureImageCachedMock.mock.calls[0]?.[0]).toBe(imagePath)
  })

  it('navigates to the author, series, and genre collections when their tags are clicked', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useLibraryStore()
    store.audiobooks = [
      {
        id: 5,
        title: 'Detail Book',
        authors: ['Brandon Sanderson'],
        series: 'Mistborn',
        genres: ['Fantasy'],
        files: [],
      },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']

    store.fetchLibrary = vi.fn(async () => undefined)

    const wrapper = mount(AudiobookDetailViewCmp, { global: { plugins: [pinia] } })
    await new Promise((r) => setTimeout(r, 10))

    const linkTags = wrapper.findAll('.detail-link-tag')
    expect(linkTags).toHaveLength(3)

    await linkTags[0].trigger('click')

    expect(routerPushMock).toHaveBeenCalledWith('/collection/author/Brandon%20Sanderson')

    await linkTags[1].trigger('click')

    expect(routerPushMock).toHaveBeenCalledWith('/collection/series/Mistborn')

    await linkTags[2].trigger('click')

    expect(routerPushMock).toHaveBeenCalledWith('/collection/genre/Fantasy')
  })

  it('opens the edit metadata modal from the detail view action', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useLibraryStore()
    store.audiobooks = [
      {
        id: 5,
        title: 'Detail Book',
        authors: ['Author One'],
        files: [],
      },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']

    store.fetchLibrary = vi.fn(async () => undefined)

    const wrapper = mount(AudiobookDetailViewCmp, {
      global: {
        plugins: [pinia],
        stubs: {
          EditAudiobookModal: {
            name: 'EditAudiobookModal',
            props: ['isOpen'],
            template: '<div class="edit-audiobook-modal-stub" :data-open="String(isOpen)" />',
          },
        },
      },
    })
    await new Promise((r) => setTimeout(r, 10))

    const editButton = wrapper.find('button[aria-label="Edit Metadata"]')
    expect(editButton.exists()).toBe(true)

    await editButton.trigger('click')
    await new Promise((r) => setTimeout(r, 0))

    expect(wrapper.find('.edit-audiobook-modal-stub').attributes('data-open')).toBe('true')
  })
})
