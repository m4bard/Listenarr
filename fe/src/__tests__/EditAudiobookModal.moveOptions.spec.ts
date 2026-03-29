import { mount } from '@vue/test-utils'
import { vi, describe, it, expect, beforeEach } from 'vitest'

vi.mock('@/services/api', () => ({
  apiService: {
    getQualityProfiles: vi.fn().mockResolvedValue([]),
    getApplicationSettings: vi.fn().mockResolvedValue({ outputPath: 'C:\\root' }),
    getAudiobookIdentifiers: vi.fn().mockResolvedValue({ identifiers: [] }),
    checkVolume: vi.fn().mockResolvedValue({ sameVolume: true }),
    updateAudiobook: vi.fn().mockResolvedValue({ message: 'ok', audiobook: {} }),
    updateAudiobookIdentifiers: vi.fn().mockResolvedValue({ identifiers: [] }),
    moveAudiobook: vi.fn().mockResolvedValue({ message: 'queued', jobId: 'job-1' }),
  },
}))

vi.mock('@/services/toastService', () => ({
  useToast: () => ({ info: vi.fn(), success: vi.fn(), error: vi.fn() }),
}))

vi.mock('@/services/signalr', () => ({
  signalRService: {
    onMoveJobUpdate: vi.fn(() => () => {}),
  },
}))

import EditAudiobookModal from '@/components/domain/audiobook/EditAudiobookModal.vue'

const audiobook = {
  id: 1,
  title: 'Sample',
  authors: ['Author'],
  basePath: 'C:\\root\\Some Author\\Some Title',
  monitored: true,
  tags: [],
}

describe('EditAudiobookModal move options', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('Change without moving should update audiobook and not call move API', async () => {
    const wrapper = mount(EditAudiobookModal, {
      props: { isOpen: true, audiobook },
      attachTo: document.body,
      global: { plugins: [(await import('pinia')).createPinia()] },
    })

    // let init settle
    await new Promise((r) => setTimeout(r, 200))

    // Ensure there is a detectable change: set an explicit custom root and flip monitored
    ;(wrapper.vm as unknown).selectedRootId = 0
    ;(wrapper.vm as unknown).customRootPath = 'C:\\root\\New Author\\New Book'
    ;(wrapper.vm as unknown).formData.monitored = false
    await wrapper.vm.$nextTick()

    // Start save flow and resolve the in-component confirmation promise by
    // calling the module-scoped resolver if it was created. This avoids
    // relying on modal rendering in jsdom.
    const savePromise = (wrapper.vm as unknown).handleSave()
    await new Promise((r) => setTimeout(r, 10))
    const resolver = (wrapper.vm as unknown).moveConfirmResolver
    if (resolver) resolver({ proceed: true, moveFiles: false, deleteEmptySource: false })
    await savePromise
    // Allow async work to settle
    await new Promise((r) => setTimeout(r, 50))

    const { apiService } = await import('@/services/api')
    expect(apiService.updateAudiobook).toHaveBeenCalledTimes(1)
    expect(apiService.moveAudiobook).toHaveBeenCalledTimes(0)
  })

  it('Move should call move API with deleteEmptySource true by default', async () => {
    const wrapper = mount(EditAudiobookModal, {
      props: { isOpen: true, audiobook },
      attachTo: document.body,
      global: { plugins: [(await import('pinia')).createPinia()] },
    })

    await new Promise((r) => setTimeout(r, 200))

    // Ensure there is a detectable change: set an explicit custom root and flip monitored
    ;(wrapper.vm as unknown).selectedRootId = 0
    ;(wrapper.vm as unknown).customRootPath = 'C:\\root\\New Author\\New Book'
    ;(wrapper.vm as unknown).formData.monitored = false
    await wrapper.vm.$nextTick()

    // Start save flow and resolve the in-component confirmation promise to
    // simulate the user choosing to move files now.
    const savePromise2 = (wrapper.vm as unknown).handleSave()
    await new Promise((r) => setTimeout(r, 10))
    const resolver2 = (wrapper.vm as unknown).moveConfirmResolver
    if (resolver2) resolver2({ proceed: true, moveFiles: true, deleteEmptySource: true })
    await savePromise2

    // Wait for async update + move to settle
    await new Promise((r) => setTimeout(r, 50))

    const { apiService } = await import('@/services/api')
    expect(apiService.updateAudiobook).toHaveBeenCalledTimes(1)
    expect(apiService.moveAudiobook).toHaveBeenCalledTimes(1)
    expect(apiService.moveAudiobook).toHaveBeenCalledWith(
      expect.anything(),
      expect.anything(),
      expect.objectContaining({ moveFiles: true, deleteEmptySource: true }),
    )
  })

  it('Edition-only changes should persist through updateAudiobook', async () => {
    const wrapper = mount(EditAudiobookModal, {
      props: { isOpen: true, audiobook },
      attachTo: document.body,
      global: { plugins: [(await import('pinia')).createPinia()] },
    })

    await new Promise((r) => setTimeout(r, 200))

    ;(wrapper.vm as unknown as { formData: { edition: string } }).formData.edition = 'Revised Edition'
    await wrapper.vm.$nextTick()

    await (wrapper.vm as unknown as { handleSave: () => Promise<void> }).handleSave()
    await new Promise((r) => setTimeout(r, 50))

    const { apiService } = await import('@/services/api')
    expect(apiService.updateAudiobook).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ edition: 'Revised Edition' }),
    )
  })

  it('metadata changes should persist through updateAudiobook', async () => {
    const wrapper = mount(EditAudiobookModal, {
      props: {
        isOpen: true,
        audiobook: {
          ...audiobook,
          subtitle: 'Original Subtitle',
          narrators: ['Original Narrator'],
          description: 'Original description',
          publisher: 'Original Publisher',
          language: 'english',
          publishedDate: '2024-01-15',
          publishYear: '2024',
          runtime: 600,
          version: 'Original Version',
          series: 'Original Series',
          seriesNumber: '1',
          genres: ['Fantasy'],
          imageUrl: 'https://example.com/original.jpg',
        },
      },
      attachTo: document.body,
      global: { plugins: [(await import('pinia')).createPinia()] },
    })

    await new Promise((r) => setTimeout(r, 200))

    const vm = wrapper.vm as unknown as {
      formData: {
        title: string
        subtitle: string
        authors: string[]
        narrators: string[]
        description: string
        publisher: string
        language: string
        publishedDate: string
        publishYear: string
        runtime: string
        edition: string
        version: string
        series: string
        seriesNumber: string
        genres: string[]
        imageUrl: string
      }
      handleSave: () => Promise<void>
    }

    vm.formData.title = 'Edited Title'
    vm.formData.subtitle = 'Edited Subtitle'
    vm.formData.authors = ['Edited Author']
    vm.formData.narrators = ['Edited Narrator']
    vm.formData.description = 'Edited description'
    vm.formData.publisher = 'Edited Publisher'
    vm.formData.language = 'swedish'
    vm.formData.publishedDate = '2025-02-01'
    vm.formData.publishYear = '2025'
    vm.formData.runtime = '720'
    vm.formData.edition = 'Collector Edition'
    vm.formData.version = 'Edited Version'
    vm.formData.series = 'Edited Series'
    vm.formData.seriesNumber = '2'
    vm.formData.genres = ['Sci-Fi', 'Adventure']
    vm.formData.imageUrl = 'https://example.com/edited.jpg'
    await wrapper.vm.$nextTick()

    await vm.handleSave()
    await new Promise((r) => setTimeout(r, 50))

    const { apiService } = await import('@/services/api')
    expect(apiService.updateAudiobook).toHaveBeenCalledWith(
      1,
      expect.objectContaining({
        title: 'Edited Title',
        subtitle: 'Edited Subtitle',
        authors: ['Edited Author'],
        narrators: ['Edited Narrator'],
        description: 'Edited description',
        publisher: 'Edited Publisher',
        language: 'swedish',
        publishedDate: '2025-02-01',
        publishYear: '2025',
        runtime: 720,
        edition: 'Collector Edition',
        version: 'Edited Version',
        series: 'Edited Series',
        seriesNumber: '2',
        genres: ['Sci-Fi', 'Adventure'],
        imageUrl: 'https://example.com/edited.jpg',
      }),
    )
  })
})
