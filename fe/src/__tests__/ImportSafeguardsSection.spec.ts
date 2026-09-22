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

describe('ImportSafeguardsSection', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('emits update:settings when the minimum free space input changes', async () => {
    const { default: ImportSafeguardsSection } =
      await import('@/components/settings/ImportSafeguardsSection.vue')
    const wrapper = mount(ImportSafeguardsSection, {
      props: {
        settings: {
          minimumFreeSpaceWhenImporting: 100,
          skipFreeSpaceCheckWhenImporting: false,
        },
      },
    })

    const input = wrapper.find('input[type="number"]')
    await input.setValue('250')

    const last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.minimumFreeSpaceWhenImporting).toBe(250)
  })

  it('clamps the minimum free space input to the documented range', async () => {
    const { default: ImportSafeguardsSection } =
      await import('@/components/settings/ImportSafeguardsSection.vue')
    const wrapper = mount(ImportSafeguardsSection, {
      props: { settings: { minimumFreeSpaceWhenImporting: 100 } },
    })

    const input = wrapper.find('input[type="number"]')
    await input.setValue('-5')
    let last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.minimumFreeSpaceWhenImporting).toBe(0)

    await input.setValue('99999999')
    last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.minimumFreeSpaceWhenImporting).toBe(1048576)
  })

  it('round-trips the skip free space check checkbox', async () => {
    const { default: ImportSafeguardsSection } =
      await import('@/components/settings/ImportSafeguardsSection.vue')
    const Checkbox = (await import('@/components/form/Checkbox.vue')).default
    const wrapper = mount(ImportSafeguardsSection, {
      props: { settings: { skipFreeSpaceCheckWhenImporting: false } },
      global: { components: { Checkbox } },
    })

    const checkbox = wrapper.find('input[type="checkbox"]')
    expect((checkbox.element as HTMLInputElement).checked).toBe(false)

    await checkbox.setValue(true)
    const last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.skipFreeSpaceCheckWhenImporting).toBe(true)
  })

  it('disables the minimum free space input when the check is skipped', async () => {
    const { default: ImportSafeguardsSection } =
      await import('@/components/settings/ImportSafeguardsSection.vue')
    const wrapper = mount(ImportSafeguardsSection, {
      props: {
        settings: {
          minimumFreeSpaceWhenImporting: 100,
          skipFreeSpaceCheckWhenImporting: true,
        },
      },
    })

    const input = wrapper.find('input[type="number"]')
    expect((input.element as HTMLInputElement).disabled).toBe(true)
  })
})
