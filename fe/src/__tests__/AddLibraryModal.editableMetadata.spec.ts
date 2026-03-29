import { mount, flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { vi, describe, it, expect, beforeEach } from 'vitest'

const apiMocks = vi.hoisted(() => ({
  getAudibleMetadata: vi.fn(),
  previewLibraryPath: vi.fn(),
  getApplicationSettings: vi.fn(),
  getQualityProfiles: vi.fn(),
  getRootFolders: vi.fn(),
  addToLibrary: vi.fn(),
}))

vi.mock('@/services/api', () => ({
  apiService: apiMocks,
}))

import AddLibraryModal from '@/components/domain/audiobook/AddLibraryModal.vue'

const fakeBook = {
  title: 'Original Title',
  subtitle: 'Original Subtitle',
  authors: ['Original Author'],
  narrators: ['Original Narrator'],
  description: 'Original description',
  imageUrl: '',
  asin: 'B001234567',
  publisher: 'Original Publisher',
  publishedDate: '2024-01-15',
  publishYear: '2024',
  language: 'english',
  runtime: 600,
  edition: 'Original Edition',
  version: 'Original Version',
  genres: ['Fantasy'],
  series: 'Series Name',
  seriesNumber: '1',
  isbn: '9781234567890',
  openLibraryId: 'OL12345M',
  explicit: false,
  abridged: false,
  source: 'Audible',
}

describe('AddLibraryModal editable metadata', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    apiMocks.getAudibleMetadata.mockResolvedValue({})
    apiMocks.getApplicationSettings.mockResolvedValue({ outputPath: 'C:\\root' })
    apiMocks.getQualityProfiles.mockResolvedValue([])
    apiMocks.getRootFolders.mockResolvedValue([])
    apiMocks.previewLibraryPath.mockImplementation((metadata, destinationRoot) => {
      const root = destinationRoot || 'C:\\root'
      const author = metadata.authors?.[0] || 'Unknown Author'
      const title = metadata.title || 'Unknown Title'
      return Promise.resolve({
        fullPath: `${root}\\${author}\\${title}`,
        relativePath: '',
      })
    })
    apiMocks.addToLibrary.mockResolvedValue({
      message: 'Added',
      audiobook: { id: 1, title: 'Edited Title' },
    })
  })

  it('previews and submits edited metadata instead of the original source metadata', async () => {
    const wrapper = mount(AddLibraryModal, {
      props: {
        visible: true,
        book: fakeBook,
      },
      attachTo: document.body,
      global: {
        plugins: [createPinia()],
      },
    })

    await flushPromises()
    apiMocks.previewLibraryPath.mockClear()

    expect(wrapper.find('.metadata-edit-grid').exists()).toBe(false)
    expect(wrapper.text()).toContain('Description')

    await wrapper.get('button.metadata-toggle-btn').trigger('click')
    await flushPromises()

    expect(wrapper.find('.detail-header').exists()).toBe(false)
    expect(wrapper.find('.metadata-edit-grid').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('Content Flags')

    const fields = wrapper.findAll('.metadata-edit-grid .detail-item')
    const findField = (label: string) => {
      const field = fields.find((item) => item.text().includes(label))
      expect(field, `Expected metadata field "${label}" to exist`).toBeTruthy()
      return field!
    }

    await findField('Title').find('input').setValue('Edited Title')
    await findField('Edition').find('input').setValue('Library Edition')
    await findField('Publisher').find('input').setValue('Edited Publisher')
    await findField('Authors').find('input').setValue('Edited Author')
    await flushPromises()

    expect(apiMocks.previewLibraryPath).toHaveBeenCalled()
    const latestPreviewCall =
      apiMocks.previewLibraryPath.mock.calls[apiMocks.previewLibraryPath.mock.calls.length - 1]
    expect(latestPreviewCall[0]).toMatchObject({
      title: 'Edited Title',
      edition: 'Library Edition',
      publisher: 'Edited Publisher',
      authors: ['Edited Author'],
    })

    const relativeInput = wrapper.get('input.relative-input')
    expect((relativeInput.element as HTMLInputElement).value).toBe('Edited Author\\Edited Title')

    await wrapper.get('button.btn-primary').trigger('click')
    await flushPromises()

    expect(apiMocks.addToLibrary).toHaveBeenCalledWith(
      expect.objectContaining({
        title: 'Edited Title',
        edition: 'Library Edition',
        publisher: 'Edited Publisher',
        authors: ['Edited Author'],
      }),
      expect.any(Object),
    )
  })
})
