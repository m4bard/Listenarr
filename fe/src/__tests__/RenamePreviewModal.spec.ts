import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import RenamePreviewModal from '@/components/domain/organize/RenamePreviewModal.vue'
import { apiService } from '@/services/api'
import type { RenamePreview } from '@/types'

describe('RenamePreviewModal', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders a cleaner before-and-after preview for organize changes', async () => {
    vi.mocked(apiService.previewRename).mockResolvedValue([
      {
        audiobookId: 7,
        audiobookTitle: 'Alchemised',
        currentFolderPath: 'D:\\test\\Author\\Alchemised',
        newFolderPath: 'D:\\test\\Author\\Alchemised test',
        folderChanged: true,
        hasChanges: true,
        fileRenames: [
          {
            fileId: 71,
            currentPath: 'D:\\test\\Author\\Alchemised\\Alchemised.m4b',
            newPath: 'D:\\test\\Author\\Alchemised test\\Alchemised test.m4b',
            currentFilename: 'Alchemised.m4b',
            newFilename: 'Alchemised test.m4b',
            changed: true,
          },
        ],
      },
    ] satisfies RenamePreview[])

    const wrapper = mount(RenamePreviewModal, {
      props: {
        visible: true,
        audiobookIds: [7],
      },
    })

    await flushPromises()

    expect(apiService.previewRename).toHaveBeenCalledWith([7])
    expect(wrapper.text()).toContain('1 of 1 audiobook(s) selected')
    expect(wrapper.text()).toContain('Review the proposed folder and filename changes before organizing files on disk.')
    expect(wrapper.text()).toContain('Folder + 1 file')
    expect(wrapper.findAll('.rename-path-value--current')).toHaveLength(2)
    expect(wrapper.findAll('.rename-path-value--new')).toHaveLength(2)
    expect(wrapper.text()).toContain('Current')
    expect(wrapper.text()).toContain('New')
    expect(wrapper.find('.btn.btn-primary').text()).toContain('Organize 1')
  })
})
