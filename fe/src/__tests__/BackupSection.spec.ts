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
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import type { VueWrapper } from '@vue/test-utils'
import type { ApplicationSettings } from '@/types'

async function mountSection(settings: Partial<ApplicationSettings> = {}) {
  const { default: BackupSection } = await import('@/components/settings/BackupSection.vue')
  return mount(BackupSection, { props: { settings } })
}

function lastPayload(wrapper: VueWrapper): Partial<ApplicationSettings> {
  const emitted = wrapper.emitted()['update:settings']!
  return emitted[emitted.length - 1][0] as Partial<ApplicationSettings>
}

describe('BackupSection', () => {
  it('shows the stored retention value', async () => {
    const wrapper = await mountSection({ backupRetentionDays: 14 })
    const input = wrapper.find('input[type="number"]')
    expect((input.element as HTMLInputElement).value).toBe('14')
  })

  it('falls back to the family default when the setting is absent', async () => {
    // The control for the case above: same component, no value supplied, and the box must not
    // render empty or zero, either of which would read as "retention is off".
    const wrapper = await mountSection({})
    const input = wrapper.find('input[type="number"]')
    expect((input.element as HTMLInputElement).value).toBe('28')
  })

  it('round-trips an edited retention value', async () => {
    const wrapper = await mountSection({ backupRetentionDays: 28 })
    await wrapper.find('input[type="number"]').setValue('7')
    expect(lastPayload(wrapper).backupRetentionDays).toBe(7)
  })

  it('keeps zero, because zero is how retention is switched off', async () => {
    const wrapper = await mountSection({ backupRetentionDays: 28 })
    await wrapper.find('input[type="number"]').setValue('0')
    expect(lastPayload(wrapper).backupRetentionDays).toBe(0)
  })

  it('treats a cleared box as the default rather than as zero', async () => {
    // Clearing the field is a keystroke on the way to typing a new number. Emitting 0 there would
    // silently disable the sweep, which is the opposite of what the operator is doing.
    const wrapper = await mountSection({ backupRetentionDays: 28 })
    await wrapper.find('input[type="number"]').setValue('')
    expect(lastPayload(wrapper).backupRetentionDays).toBe(28)
  })

  it('clamps a value beyond the allowed range', async () => {
    const wrapper = await mountSection({ backupRetentionDays: 28 })
    await wrapper.find('input[type="number"]').setValue('99999')
    expect(lastPayload(wrapper).backupRetentionDays).toBe(365)

    await wrapper.find('input[type="number"]').setValue('-5')
    expect(lastPayload(wrapper).backupRetentionDays).toBe(0)
  })

  it('leaves the other settings on the emitted object alone', async () => {
    const wrapper = await mountSection({ backupRetentionDays: 28, enableNotifications: true })
    await wrapper.find('input[type="number"]').setValue('9')
    const payload = lastPayload(wrapper)
    expect(payload.backupRetentionDays).toBe(9)
    expect(payload.enableNotifications).toBe(true)
  })
})
