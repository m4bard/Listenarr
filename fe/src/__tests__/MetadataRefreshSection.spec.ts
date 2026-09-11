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
import type { ApplicationSettings } from '@/types'

// Deliberately all different from each other and from the shipped defaults, so a control
// bound to the wrong key shows the wrong number instead of accidentally matching.
const settings: Partial<ApplicationSettings> = {
  metadataRefreshEnabled: true,
  metadataRefreshIntervalHours: 6,
  metadataRefreshStaleAfterDays: 14,
  metadataRefreshRequestsPerHour: 120,
  metadataRefreshMinimumSpacingMs: 2500,
}

async function mountSection() {
  const { default: MetadataRefreshSection } =
    await import('@/components/settings/MetadataRefreshSection.vue')
  return mount(MetadataRefreshSection, {
    props: { settings: { ...settings } },
    global: { components: { Checkbox } },
  })
}

function lastPayload(wrapper: Awaited<ReturnType<typeof mountSection>>) {
  const emitted = wrapper.emitted()['update:settings']!
  return emitted[emitted.length - 1][0] as Partial<ApplicationSettings>
}

describe('MetadataRefreshSection', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('renders the five controls with the values it was given', async () => {
    const wrapper = await mountSection()

    const checks = wrapper.findAll('input[type="checkbox"]')
    expect(checks).toHaveLength(1)
    expect((checks[0].element as HTMLInputElement).checked).toBe(true)

    const inputs = wrapper.findAll('input[type="number"]')
    expect(inputs).toHaveLength(4)
    expect(inputs.map((i) => (i.element as HTMLInputElement).value)).toEqual([
      '6',
      '14',
      '120',
      '2500',
    ])
  })

  it('reflects a disabled refresh rather than forcing the box on', async () => {
    const { default: MetadataRefreshSection } =
      await import('@/components/settings/MetadataRefreshSection.vue')
    const wrapper = mount(MetadataRefreshSection, {
      props: { settings: { ...settings, metadataRefreshEnabled: false } },
      global: { components: { Checkbox } },
    })

    const checkbox = wrapper.find('input[type="checkbox"]').element as HTMLInputElement
    expect(checkbox.checked).toBe(false)
  })

  it('emits update:settings when the enable toggle changes', async () => {
    const wrapper = await mountSection()

    await wrapper.find('input[type="checkbox"]').setValue(false)

    const payload = lastPayload(wrapper)
    expect(payload.metadataRefreshEnabled).toBe(false)
    // The rest of the section rides along untouched.
    expect(payload.metadataRefreshIntervalHours).toBe(6)
    expect(payload.metadataRefreshStaleAfterDays).toBe(14)
    expect(payload.metadataRefreshRequestsPerHour).toBe(120)
    expect(payload.metadataRefreshMinimumSpacingMs).toBe(2500)
  })

  it.each([
    [0, 'metadataRefreshIntervalHours', '12', 12],
    [1, 'metadataRefreshStaleAfterDays', '45', 45],
    [2, 'metadataRefreshRequestsPerHour', '300', 300],
    [3, 'metadataRefreshMinimumSpacingMs', '5000', 5000],
  ] as const)('writes numeric input %i to %s', async (index, key, typed, expected) => {
    const wrapper = await mountSection()

    const inputs = wrapper.findAll('input[type="number"]')
    await inputs[index].setValue(typed)

    const payload = lastPayload(wrapper)
    expect(payload[key]).toBe(expected)

    // Every other key in the section keeps the value it was mounted with, so a control
    // wired to a neighbour's key fails here instead of quietly passing.
    const untouched = (
      [
        'metadataRefreshIntervalHours',
        'metadataRefreshStaleAfterDays',
        'metadataRefreshRequestsPerHour',
        'metadataRefreshMinimumSpacingMs',
      ] as const
    ).filter((k) => k !== key)
    for (const other of untouched) {
      expect(payload[other]).toBe(settings[other])
    }
    expect(payload.metadataRefreshEnabled).toBe(true)
  })

  it('leaves the numeric inputs live while the refresh is on', async () => {
    const wrapper = await mountSection()

    const inputs = wrapper.findAll('input[type="number"]')
    expect(inputs.map((i) => (i.element as HTMLInputElement).disabled)).toEqual([
      false,
      false,
      false,
      false,
    ])
  })

  it('disables the numeric inputs when the refresh is turned off', async () => {
    const { default: MetadataRefreshSection } =
      await import('@/components/settings/MetadataRefreshSection.vue')
    const wrapper = mount(MetadataRefreshSection, {
      props: { settings: { ...settings, metadataRefreshEnabled: false } },
      global: { components: { Checkbox } },
    })

    // The neighbouring sections grey out what a disabled toggle governs. Leaving these live
    // invites an operator to tune an interval that nothing is going to read.
    const inputs = wrapper.findAll('input[type="number"]')
    expect(inputs.map((i) => (i.element as HTMLInputElement).disabled)).toEqual([
      true,
      true,
      true,
      true,
    ])
  })

  it.each([
    [0, 'metadataRefreshIntervalHours', '9999', 168],
    [0, 'metadataRefreshIntervalHours', '0', 1],
    [1, 'metadataRefreshStaleAfterDays', '99999', 3650],
    [1, 'metadataRefreshStaleAfterDays', '-5', 0],
    [2, 'metadataRefreshRequestsPerHour', '100000', 3600],
    [2, 'metadataRefreshRequestsPerHour', '0', 1],
    [3, 'metadataRefreshMinimumSpacingMs', '86400000', 60000],
    [3, 'metadataRefreshMinimumSpacingMs', '-1', 0],
  ] as const)('clamps input %i (%s) typed as %s to %i', async (index, key, typed, expected) => {
    const wrapper = await mountSection()

    const inputs = wrapper.findAll('input[type="number"]')
    await inputs[index].setValue(typed)

    // min and max on a number input are advisory: typing or pasting past them is allowed, and
    // nothing downstream was rejecting the value either. A spacing of a day does not slow the
    // walk down, it stops it, and it does so without saying anything.
    expect(lastPayload(wrapper)[key]).toBe(expected)
  })

  it('falls back to the shipped defaults when the box is emptied', async () => {
    const wrapper = await mountSection()

    await wrapper.findAll('input[type="number"]')[1].setValue('')

    // Zero is a legal staleness age and means "refresh everything on every pass", so an emptied
    // box must not quietly become that.
    expect(lastPayload(wrapper).metadataRefreshStaleAfterDays).toBe(30)
  })

  it('falls back to the shipped defaults when the settings payload is empty', async () => {
    const { default: MetadataRefreshSection } =
      await import('@/components/settings/MetadataRefreshSection.vue')
    const wrapper = mount(MetadataRefreshSection, {
      props: { settings: {} },
      global: { components: { Checkbox } },
    })

    const inputs = wrapper.findAll('input[type="number"]')
    expect(inputs.map((i) => (i.element as HTMLInputElement).value)).toEqual([
      '24',
      '30',
      '60',
      '1000',
    ])
  })
})
