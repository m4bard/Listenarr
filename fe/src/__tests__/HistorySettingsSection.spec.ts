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

describe('HistorySettingsSection', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('loads the currently configured retention value', async () => {
    const { default: HistorySettingsSection } = await import(
      '@/components/settings/HistorySettingsSection.vue'
    )
    const wrapper = mount(HistorySettingsSection, {
      props: { settings: { historyRetentionDays: 45 } },
    })

    const input = wrapper.find('input[type="number"]')
    expect((input.element as HTMLInputElement).value).toBe('45')
  })

  it('defaults to 0 when no retention value has been configured yet', async () => {
    const { default: HistorySettingsSection } = await import(
      '@/components/settings/HistorySettingsSection.vue'
    )
    const wrapper = mount(HistorySettingsSection, { props: { settings: {} } })

    const input = wrapper.find('input[type="number"]')
    expect((input.element as HTMLInputElement).value).toBe('0')
  })

  it('emits update:settings with the changed value when saved', async () => {
    const { default: HistorySettingsSection } = await import(
      '@/components/settings/HistorySettingsSection.vue'
    )
    const wrapper = mount(HistorySettingsSection, {
      props: { settings: { historyRetentionDays: 30 } },
    })

    const input = wrapper.find('input[type="number"]')
    await input.setValue('90')

    const last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.historyRetentionDays).toBe(90)
  })
})
