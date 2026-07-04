import { beforeEach, describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import RootFolderFormModal from '@/components/settings/RootFolderFormModal.vue'
import { useRootFoldersStore } from '@/stores/rootFolders'

const success = vi.fn()
const error = vi.fn()

vi.mock('@/services/toastService', () => ({
  useToast: () => ({ success, error }),
}))

describe('RootFolderFormModal', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it.each([
    [true, 'Root relocation started'],
    [false, 'Root path metadata updated'],
  ])('reports the path change accurately when moveFiles is %s', async (moveFiles, message) => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useRootFoldersStore()
    vi.spyOn(store, 'update').mockResolvedValue({
      id: 7,
      name: 'Library',
      path: '/new-library',
      isDefault: true,
    })
    const wrapper = mount(RootFolderFormModal, {
      props: {
        root: {
          id: 7,
          name: 'Library',
          path: '/old-library',
          isDefault: true,
        },
      },
      global: {
        plugins: [pinia],
        stubs: {
          FolderBrowserModal: true,
        },
      },
    })
    await wrapper.get('#root-path').setValue('/new-library')
    await (
      wrapper.vm as unknown as { confirmChange: (moveFiles: boolean) => Promise<void> }
    ).confirmChange(moveFiles)
    await vi.waitFor(() => expect(success).toHaveBeenCalledWith('Success', message))
  })
})
