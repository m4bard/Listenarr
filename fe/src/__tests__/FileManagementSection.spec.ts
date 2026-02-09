import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'

describe('FileManagementSection', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('emits update:settings on pattern and select changes', async () => {
    const { default: FileManagementSection } = await import('@/components/settings/FileManagementSection.vue')
    const wrapper = mount(FileManagementSection, {
      props: {
        settings: {
          folderNamingPattern: '{Author}/{Series}/{Title}',
          fileNamingPattern: '{Title}',
          completedFileAction: 'Move',
        },
      },
    })

    const folderInput = wrapper.find('input[placeholder="{Author}/{Series}/{Title}"]')
    await folderInput.setValue('{Author}/{Title}')
    let last = wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.folderNamingPattern).toBe('{Author}/{Title}')

    const fileInput = wrapper.find('input[placeholder="{Title}"]')
    await fileInput.setValue('{Title}-{DiskNumber}')
    last = wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.fileNamingPattern).toBe('{Title}-{DiskNumber}')

    const sel = wrapper.find('select')
    await sel.setValue('Copy')
    last = wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.completedFileAction).toBe('Copy')
  })
})
