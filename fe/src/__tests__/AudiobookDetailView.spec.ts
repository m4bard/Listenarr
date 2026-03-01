import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { describe, it, beforeEach, expect, vi } from 'vitest'
import { API_BASE_PATH } from '@/services/apiBase'
import { useLibraryStore } from '@/stores/library'
import { ensureImageCached } from '@/services/api'
import AudiobookDetailViewCmp from '@/views/library/AudiobookDetailView.vue'
// Mock useRoute to provide params for the detail view
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { id: '5' } }),
  useRouter: () => ({ push: vi.fn() }),
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
})
