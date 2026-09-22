/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { apiService } from '@/services/api'
import type { NamingPatternPreview } from '@/types'

// FileManagementSection no longer renders its own approximation of the naming pattern
// (the old applyPattern/sampleVariables pair). The three preview rows are fed by
// apiService.previewNamingPatterns, which is GET configuration/naming/examples, so these
// tests verify the component wires that call up correctly rather than re-testing the naming
// renderer itself (that lives in FileNamingService_PreviewNamingPatternsTests.cs).

function mockPreview(overrides: Partial<NamingPatternPreview> = {}): NamingPatternPreview {
  return {
    folderExample: '',
    singleFileExample: '',
    multiFileExamples: [],
    multiFileAmbiguous: false,
    ...overrides,
  }
}

describe('FileManagementSection', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    vi.mocked(apiService.previewNamingPatterns).mockReset()
    vi.mocked(apiService.previewNamingPatterns).mockResolvedValue(mockPreview())
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('emits update:settings on pattern and select changes', async () => {
    const { default: FileManagementSection } =
      await import('@/components/settings/FileManagementSection.vue')
    const wrapper = mount(FileManagementSection, {
      props: {
        settings: {
          folderNamingPattern: '{Author}/{Series}/{Title}',
          fileNamingPattern: '{Title}',
          completedFileAction: 'move',
          importBlacklistExtensions: ['.nfo'],
        },
      },
    })
    await flushPromises()

    const folderInput = wrapper.find('input[placeholder="{Author}/{Series}/{Title}"]')
    await folderInput.setValue('{Author}/{Title}')
    let last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.folderNamingPattern).toBe('{Author}/{Title}')

    const fileInput = wrapper.find('input[placeholder="{Title}"]')
    await fileInput.setValue('{Title}-{DiskNumber}')
    last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.fileNamingPattern).toBe('{Title}-{DiskNumber}')

    const sel = wrapper.find('select')
    await sel.setValue('hardlink/copy')
    last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.completedFileAction).toBe('hardlink/copy')

    const textarea = wrapper.find('textarea')
    await textarea.setValue('.nfo\njpg')
    await textarea.trigger('change')
    last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.importBlacklistExtensions).toEqual(['.nfo', '.jpg'])

    // Unmount so the debounced preview refresh this test's setValue calls scheduled with real
    // timers does not fire during a later test and pollute its call count.
    wrapper.unmount()
  })

  it('fetches the preview on mount and renders the server-rendered examples', async () => {
    vi.mocked(apiService.previewNamingPatterns).mockResolvedValue(
      mockPreview({
        folderExample: 'M. R. Castellane/The Clockmaker',
        singleFileExample: 'The Clockmaker.m4b',
      }),
    )
    const { default: FileManagementSection } =
      await import('@/components/settings/FileManagementSection.vue')
    const wrapper = mount(FileManagementSection, {
      props: {
        settings: {
          folderNamingPattern: '{Author}/{Title}',
          fileNamingPattern: '{Title}',
        },
      },
    })
    await flushPromises()

    expect(apiService.previewNamingPatterns).toHaveBeenCalledWith({
      folderPattern: '{Author}/{Title}',
      filePattern: '{Title}',
      multiFilePattern: '{Title}-{DiskNumber:00}',
    })

    const previews = wrapper.findAll('.pattern-preview code')
    expect(previews[0].text()).toContain('M. R. Castellane/The Clockmaker')
    expect(previews[1].text()).toContain('The Clockmaker.m4b')
  })

  it('shows every rendered multi-file example and flags ambiguity from the server response', async () => {
    vi.mocked(apiService.previewNamingPatterns).mockResolvedValue(
      mockPreview({
        multiFileExamples: ['The Clockmaker-Ch01.m4b', 'The Clockmaker-Ch02.m4b'],
        multiFileAmbiguous: false,
      }),
    )
    const { default: FileManagementSection } =
      await import('@/components/settings/FileManagementSection.vue')
    const wrapper = mount(FileManagementSection, {
      props: {
        settings: {
          multiFileNamingPattern: '{Title}-Ch{ChapterNumber:00}',
        },
      },
    })
    await flushPromises()

    const preview = wrapper.find('.pattern-preview code')
    expect(preview.text()).toContain('The Clockmaker-Ch01.m4b')
    expect(preview.text()).toContain('The Clockmaker-Ch02.m4b')
    expect(preview.text()).not.toContain('every file would get the same name')
  })

  it('warns when the server reports the multi-file pattern is ambiguous', async () => {
    vi.mocked(apiService.previewNamingPatterns).mockResolvedValue(
      mockPreview({
        multiFileExamples: ['The Clockmaker.m4b', 'The Clockmaker.m4b'],
        multiFileAmbiguous: true,
      }),
    )
    const { default: FileManagementSection } =
      await import('@/components/settings/FileManagementSection.vue')
    const wrapper = mount(FileManagementSection, {
      props: {
        settings: {
          multiFileNamingPattern: '{Title}',
        },
      },
    })
    await flushPromises()

    const preview = wrapper.find('.pattern-preview code')
    expect(preview.text()).toContain('every file would get the same name')
  })

  it('shows path length warning when the server-rendered sample path exceeds 259 characters', async () => {
    const longFolder = Array(6).fill('AuthorName/SeriesName/BookTitleGoesHere').join('/')
    vi.mocked(apiService.previewNamingPatterns).mockResolvedValue(
      mockPreview({
        folderExample: longFolder,
        singleFileExample: 'BookTitleGoesHere.m4b',
      }),
    )
    const { default: FileManagementSection } =
      await import('@/components/settings/FileManagementSection.vue')
    const wrapper = mount(FileManagementSection, {
      props: {
        settings: {
          outputPath: 'D:\\VeryLongAudiobookLibraryBasePath\\Collection',
          folderNamingPattern: '{Author}/{Series}/{Title}',
          fileNamingPattern: '{Title}',
        },
      },
    })
    await flushPromises()

    const warning = wrapper.find('.path-length-warning')
    expect(warning.exists()).toBe(true)
    expect(warning.text()).toContain('260 characters')
  })

  it('does not show path length warning when the server-rendered sample path is short', async () => {
    vi.mocked(apiService.previewNamingPatterns).mockResolvedValue(
      mockPreview({
        folderExample: 'Author Name/Book Title',
        singleFileExample: 'Book Title.m4b',
      }),
    )
    const { default: FileManagementSection } =
      await import('@/components/settings/FileManagementSection.vue')
    const wrapper = mount(FileManagementSection, {
      props: {
        settings: {
          outputPath: 'D:\\Books',
          folderNamingPattern: '{Author}/{Title}',
          fileNamingPattern: '{Title}',
        },
      },
    })
    await flushPromises()

    const warning = wrapper.find('.path-length-warning')
    expect(warning.exists()).toBe(false)

    const ok = wrapper.find('.path-length-ok')
    expect(ok.exists()).toBe(true)
    expect(ok.text()).toContain('/ 259 characters')
  })

  it('shows an explicit unavailable message and does not fall back to a local approximation when the preview call fails', async () => {
    vi.mocked(apiService.previewNamingPatterns).mockRejectedValue(new Error('network error'))
    const { default: FileManagementSection } =
      await import('@/components/settings/FileManagementSection.vue')
    const wrapper = mount(FileManagementSection, {
      props: {
        settings: {
          folderNamingPattern: '{Author}/{Title}',
        },
      },
    })
    await flushPromises()

    expect(wrapper.find('.preview-error').exists()).toBe(true)
    expect(wrapper.find('.preview-error').text()).toBe('Preview unavailable')
    // No literal, unresolved token text and no locally-computed sample value should ever
    // appear: a silent fallback to the old frontend-only renderer would recreate the defect
    // this preview replaces.
    expect(wrapper.text()).not.toContain('{Author}')
    expect(wrapper.text()).not.toContain('Stephen King')
  })

  it('debounces the preview refresh while typing and re-fetches with the latest pattern', async () => {
    vi.useFakeTimers()
    const { default: FileManagementSection } =
      await import('@/components/settings/FileManagementSection.vue')
    const wrapper = mount(FileManagementSection, {
      props: {
        settings: {
          folderNamingPattern: '{Author}/{Title}',
        },
      },
    })
    await vi.advanceTimersByTimeAsync(0)
    expect(apiService.previewNamingPatterns).toHaveBeenCalledTimes(1)

    const folderInput = wrapper.find('input[placeholder="{Author}/{Series}/{Title}"]')
    await folderInput.setValue('{Author}/{Series}/{Title}')

    // Not yet: the refresh is debounced.
    await vi.advanceTimersByTimeAsync(100)
    expect(apiService.previewNamingPatterns).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(300)
    expect(apiService.previewNamingPatterns).toHaveBeenCalledTimes(2)
    expect(apiService.previewNamingPatterns).toHaveBeenLastCalledWith(
      expect.objectContaining({ folderPattern: '{Author}/{Series}/{Title}' }),
    )
  })
})
