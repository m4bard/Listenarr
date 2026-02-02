import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import Checkbox from '@/components/form/Checkbox.vue'

describe('SearchSettingsSection', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('emits update:settings for checkboxes and numeric inputs', async () => {
    const { default: SearchSettingsSection } = await import('@/components/settings/SearchSettingsSection.vue')
    const wrapper = mount(SearchSettingsSection, {
      props: { settings: { enableAmazonSearch: false, enableAudibleSearch: false, enableOpenLibrarySearch: false, searchCandidateCap: 10, searchResultCap: 10, searchFuzzyThreshold: 0.5 } },
      global: { components: { Checkbox } },
    })

    const checks = wrapper.findAll('input[type="checkbox"]')
    await checks[0].setValue(true)
    let last = wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.enableAmazonSearch).toBe(true)

    await checks[1].setValue(true)
    last = wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.enableAudibleSearch).toBe(true)

    const nums = wrapper.findAll('input[type="number"]')
    await nums[0].setValue('50')
    last = wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.searchCandidateCap).toBe(50)

    await nums[1].setValue('75')
    last = wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.searchResultCap).toBe(75)

    await nums[2].setValue('0.9')
    last = wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.searchFuzzyThreshold).toBeCloseTo(0.9)
  })
})