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

describe('HousekeepingSection', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('emits update:settings when the dry run checkbox is toggled', async () => {
    const { default: HousekeepingSection } =
      await import('@/components/settings/HousekeepingSection.vue')
    const wrapper = mount(HousekeepingSection, {
      props: { settings: { housekeepingDryRun: true, housekeepingRetentionDays: 30 } },
      global: { components: { Checkbox } },
    })

    const checkbox = wrapper.find('input[type="checkbox"]')
    await checkbox.setValue(false)
    const last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.housekeepingDryRun).toBe(false)
  })

  it('emits update:settings when the retention days field changes', async () => {
    const { default: HousekeepingSection } =
      await import('@/components/settings/HousekeepingSection.vue')
    const wrapper = mount(HousekeepingSection, {
      props: { settings: { housekeepingDryRun: true, housekeepingRetentionDays: 30 } },
      global: { components: { Checkbox } },
    })

    const input = wrapper.find('input[type="number"]')
    await input.setValue('14')
    const last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.housekeepingRetentionDays).toBe(14)
  })

  it('defaults an unset dry run to true, matching the backend default', async () => {
    const { default: HousekeepingSection } =
      await import('@/components/settings/HousekeepingSection.vue')
    const wrapper = mount(HousekeepingSection, {
      props: { settings: {} },
      global: { components: { Checkbox } },
    })

    const checkbox = wrapper.find('input[type="checkbox"]')
    expect((checkbox.element as HTMLInputElement).checked).toBe(true)
  })

  it('states in the retention help text that 0 keeps everything, not that it is unset', async () => {
    const { default: HousekeepingSection } =
      await import('@/components/settings/HousekeepingSection.vue')
    const wrapper = mount(HousekeepingSection, {
      props: { settings: { housekeepingDryRun: true, housekeepingRetentionDays: 30 } },
      global: { components: { Checkbox } },
    })

    const help = wrapper.text()
    expect(help).toContain('0 keeps every record')
    expect(help).toContain('does not mean unset')
    expect(help).toContain('does not mean delete everything now')
  })

  it('shows a live-delete status when dry run is off and retention is above zero', async () => {
    const { default: HousekeepingSection } =
      await import('@/components/settings/HousekeepingSection.vue')
    const wrapper = mount(HousekeepingSection, {
      props: { settings: { housekeepingDryRun: false, housekeepingRetentionDays: 30 } },
      global: { components: { Checkbox } },
    })

    expect(wrapper.text()).toContain('Deleting for real')
  })

  it('shows a disabled status when retention is 0, even with dry run off', async () => {
    const { default: HousekeepingSection } =
      await import('@/components/settings/HousekeepingSection.vue')
    const wrapper = mount(HousekeepingSection, {
      props: { settings: { housekeepingDryRun: false, housekeepingRetentionDays: 0 } },
      global: { components: { Checkbox } },
    })

    expect(wrapper.text()).toContain('sweep is disabled')
  })

  it('shows a reporting-only status when dry run is on and retention is above zero', async () => {
    const { default: HousekeepingSection } =
      await import('@/components/settings/HousekeepingSection.vue')
    const wrapper = mount(HousekeepingSection, {
      props: { settings: { housekeepingDryRun: true, housekeepingRetentionDays: 30 } },
      global: { components: { Checkbox } },
    })

    expect(wrapper.text()).toContain('Reporting only')
  })
})
