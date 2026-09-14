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
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import Checkbox from '@/components/form/Checkbox.vue'

describe('SearchSettingsSection', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  describe('indexer search concurrency', () => {
    async function mountWith(settings: Record<string, unknown>) {
      const { default: SearchSettingsSection } =
        await import('@/components/settings/SearchSettingsSection.vue')
      return mount(SearchSettingsSection, {
        props: { settings },
        global: { components: { Checkbox } },
      })
    }

    function lastEmitted(wrapper: Awaited<ReturnType<typeof mountWith>>) {
      const emitted = wrapper.emitted()['update:settings']!
      return emitted[emitted.length - 1][0] as Record<string, unknown>
    }

    it('shows the shipped ceiling of 4 when the setting is absent', async () => {
      const wrapper = await mountWith({})
      const input = wrapper.find('#max-concurrent-indexer-searches')
      expect((input.element as HTMLInputElement).value).toBe('4')
    })

    it('emits the typed ceiling', async () => {
      const wrapper = await mountWith({ maxConcurrentIndexerSearches: 4 })
      await wrapper.find('#max-concurrent-indexer-searches').setValue('2')
      expect(lastEmitted(wrapper).maxConcurrentIndexerSearches).toBe(2)
    })

    it.each([
      ['0', 1],
      ['-5', 1],
      ['999', 32],
      ['', 4],
      ['2.6', 3],
    ])('clamps %j to %i, the same bounds the server applies', async (typed, expected) => {
      const wrapper = await mountWith({ maxConcurrentIndexerSearches: 4 })
      await wrapper.find('#max-concurrent-indexer-searches').setValue(typed)
      expect(lastEmitted(wrapper).maxConcurrentIndexerSearches).toBe(expected)
    })
  })

  it('emits update:settings for checkboxes and numeric inputs', async () => {
    const { default: SearchSettingsSection } =
      await import('@/components/settings/SearchSettingsSection.vue')
    const wrapper = mount(SearchSettingsSection, {
      props: { settings: { enableOpenLibrarySearch: false } },
      global: { components: { Checkbox } },
    })

    const checks = wrapper.findAll('input[type="checkbox"]')
    await checks[0].setValue(true)
    const last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.enableOpenLibrarySearch).toBe(true)
  })

  it('round-trips the Amazon and Audible search checkboxes', async () => {
    const { default: SearchSettingsSection } =
      await import('@/components/settings/SearchSettingsSection.vue')
    const wrapper = mount(SearchSettingsSection, {
      props: { settings: { enableAmazonSearch: true, enableAudibleSearch: true } },
      global: { components: { Checkbox } },
    })

    const checks = wrapper.findAll('input[type="checkbox"]')
    expect(checks).toHaveLength(3)

    await checks[1].setValue(false)
    let last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.enableAmazonSearch).toBe(false)

    await checks[2].setValue(false)
    last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.enableAudibleSearch).toBe(false)
  })
})
