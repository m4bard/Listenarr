import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import LibraryImportFooter from '@/components/domain/audiobook/LibraryImportFooter.vue'
import { useLibraryImportStore } from '@/stores/libraryImport'

const success = vi.fn()
const error = vi.fn()

vi.mock('@/services/toastService', () => ({
  useToast: () => ({ success, error }),
}))

describe('LibraryImportFooter', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('shows a stable importing indicator while the batch is running', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useLibraryImportStore()

    let resolveImport: ((value: { imported: number; errors: string[] }) => void) | null = null

    store.items = {
      'C:\\incoming\\Book 1.mp3': {
        id: 'C:\\incoming\\Book 1.mp3',
        fullPath: 'C:\\incoming\\Book 1.mp3',
        sourceFiles: ['C:\\incoming\\Book 1.mp3'],
        folderPath: 'C:\\incoming',
        relativePath: 'Book 1',
        folderName: 'Book 1',
        format: 'MP3',
        fileCount: 1,
        selectedMatch: { title: 'Book 1', authors: [] } as any,
        hasSearched: true,
        isSearching: false,
        selected: true,
      },
      'C:\\incoming\\Book 2.mp3': {
        id: 'C:\\incoming\\Book 2.mp3',
        fullPath: 'C:\\incoming\\Book 2.mp3',
        sourceFiles: ['C:\\incoming\\Book 2.mp3'],
        folderPath: 'C:\\incoming',
        relativePath: 'Book 2',
        folderName: 'Book 2',
        format: 'MP3',
        fileCount: 1,
        selectedMatch: { title: 'Book 2', authors: [] } as any,
        hasSearched: true,
        isSearching: false,
        selected: true,
      },
    } as any

    ;(store as any).importSelected = vi.fn(
      () =>
        new Promise<{ imported: number; errors: string[] }>((resolve) => {
          resolveImport = resolve
        }),
    )

    const wrapper = mount(LibraryImportFooter, {
      props: {
        folders: [{ id: 1, path: 'D:\\library' }] as any,
      },
      global: {
        plugins: [pinia],
      },
    })

    const importButton = wrapper.find('button.btn.btn-primary')
    await importButton.trigger('click')
    await wrapper.vm.$nextTick()

    expect(importButton.text()).toContain('Importing 2 Books...')
    expect((importButton.element as HTMLButtonElement).disabled).toBe(true)

    resolveImport?.({ imported: 2, errors: [] })
    await new Promise((resolve) => setTimeout(resolve, 0))
    await wrapper.vm.$nextTick()

    expect(success).toHaveBeenCalledWith('Import complete', '2 books imported')
  })
})
