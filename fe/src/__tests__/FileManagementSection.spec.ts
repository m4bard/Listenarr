import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'

describe('FileManagementSection', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('emits update:settings on pattern and select changes', async () => {
    const { default: FileManagementSection } = await import('@/components/settings/FileManagementSection.vue')
    const wrapper = mount(FileManagementSection, {
      props: { settings: { fileNamingPattern: '{Author}/{Title}', completedFileAction: 'Move' } },
    })

    const input = wrapper.find('input[type="text"]')
    await input.setValue('{Author}/{Series}/{Title}')
    let last = wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.fileNamingPattern).toBe('{Author}/{Series}/{Title}')

    const sel = wrapper.find('select')
    await sel.setValue('Copy')
    last = wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.completedFileAction).toBe('Copy')
  })
})
