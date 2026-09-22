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
import { flushPromises, mount } from '@vue/test-utils'
import RecycleBinSection from '@/components/settings/RecycleBinSection.vue'
import { apiService } from '@/services/api'
import type { ApplicationSettings } from '@/types'

// The globally mocked apiService (fe/src/__tests__/test-setup.ts) does not stub every
// method on the real service, only the ones existing specs already needed. Add this one
// if it is missing rather than assuming it is there.
if (!(apiService as unknown as Record<string, unknown>).emptyRecycleBin) {
  ;(apiService as unknown as { emptyRecycleBin: () => Promise<unknown> }).emptyRecycleBin =
    vi.fn(async () => ({ message: 'Recycle bin emptied', deletedCount: 0 }))
}

// Deliberately different from the shipped defaults, so a control bound to the wrong key
// shows the wrong value instead of accidentally matching.
const settings: Partial<ApplicationSettings> = {
  recycleBinPath: '/mnt/recycle',
  recycleBinCleanupDays: 21,
}

function mountSection(overrides: Partial<ApplicationSettings> = {}) {
  return mount(RecycleBinSection, {
    props: { settings: { ...settings, ...overrides } },
  })
}

function lastPayload(wrapper: ReturnType<typeof mountSection>) {
  const emitted = wrapper.emitted()['update:settings']!
  return emitted[emitted.length - 1][0] as Partial<ApplicationSettings>
}

describe('RecycleBinSection', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('renders the path and retention controls with the values it was given', () => {
    const wrapper = mountSection()

    const pathInput = wrapper.find('input[type="text"]').element as HTMLInputElement
    expect(pathInput.value).toBe('/mnt/recycle')

    const daysInput = wrapper.find('input[type="number"]').element as HTMLInputElement
    expect(daysInput.value).toBe('21')
  })

  it('emits update:settings with the new path when the path field is edited', async () => {
    const wrapper = mountSection()

    await wrapper.find('input[type="text"]').setValue('/srv/bin')

    const payload = lastPayload(wrapper)
    expect(payload.recycleBinPath).toBe('/srv/bin')
    // The neighbouring field rides along untouched, so a control wired to the wrong key
    // fails here instead of quietly passing.
    expect(payload.recycleBinCleanupDays).toBe(21)
  })

  it('emits update:settings with the new retention when the days field is edited', async () => {
    const wrapper = mountSection()

    await wrapper.find('input[type="number"]').setValue('14')

    const payload = lastPayload(wrapper)
    expect(payload.recycleBinCleanupDays).toBe(14)
    expect(payload.recycleBinPath).toBe('/mnt/recycle')
  })

  it('accepts zero as a legal retention value', async () => {
    const wrapper = mountSection()

    await wrapper.find('input[type="number"]').setValue('0')

    expect(lastPayload(wrapper).recycleBinCleanupDays).toBe(0)
  })

  it('falls back to the shipped default when the retention box is emptied', async () => {
    const wrapper = mountSection()

    await wrapper.find('input[type="number"]').setValue('')

    // Zero is a legal retention value and means something different (keep until emptied
    // by hand), so an emptied box must not quietly become that.
    expect(lastPayload(wrapper).recycleBinCleanupDays).toBe(7)
  })

  it('clamps a negative retention value to zero', async () => {
    const wrapper = mountSection()

    await wrapper.find('input[type="number"]').setValue('-5')

    expect(lastPayload(wrapper).recycleBinCleanupDays).toBe(0)
  })

  it('disables the empty-bin button when the path is empty', () => {
    const wrapper = mountSection({ recycleBinPath: '' })

    const button = wrapper.find('button').element as HTMLButtonElement
    expect(button.disabled).toBe(true)
  })

  it('enables the empty-bin button when the path is set', () => {
    const wrapper = mountSection({ recycleBinPath: '/mnt/recycle' })

    const button = wrapper.find('button').element as HTMLButtonElement
    expect(button.disabled).toBe(false)
  })

  it('leaves the retention field live when the path is empty', () => {
    // So an operator can set the budget before switching the feature on, matching how the
    // rest of the settings screen treats a numeric field that a toggle or an empty path
    // would otherwise govern.
    const wrapper = mountSection({ recycleBinPath: '' })

    const daysInput = wrapper.find('input[type="number"]').element as HTMLInputElement
    expect(daysInput.disabled).toBe(false)
  })

  it('does not call the API when the button is clicked while disabled', async () => {
    const emptyRecycleBin = vi.fn(async () => ({ message: 'ok', deletedCount: 0 }))
    ;(apiService as unknown as { emptyRecycleBin: typeof emptyRecycleBin }).emptyRecycleBin =
      emptyRecycleBin
    const confirmSpy = vi.spyOn(window, 'confirm')

    const wrapper = mountSection({ recycleBinPath: '' })
    await wrapper.find('button').trigger('click')

    expect(confirmSpy).not.toHaveBeenCalled()
    expect(emptyRecycleBin).not.toHaveBeenCalled()
  })

  it('asks for confirmation before emptying the bin, and does nothing if declined', async () => {
    const emptyRecycleBin = vi.fn(async () => ({ message: 'ok', deletedCount: 3 }))
    ;(apiService as unknown as { emptyRecycleBin: typeof emptyRecycleBin }).emptyRecycleBin =
      emptyRecycleBin
    vi.spyOn(window, 'confirm').mockReturnValue(false)

    const wrapper = mountSection()
    await wrapper.find('button').trigger('click')
    await flushPromises()

    expect(window.confirm).toHaveBeenCalled()
    expect(emptyRecycleBin).not.toHaveBeenCalled()
  })

  it('calls DELETE via emptyRecycleBin and shows the removed count on confirmation', async () => {
    const emptyRecycleBin = vi.fn(async () => ({ message: 'ok', deletedCount: 4 }))
    ;(apiService as unknown as { emptyRecycleBin: typeof emptyRecycleBin }).emptyRecycleBin =
      emptyRecycleBin
    vi.spyOn(window, 'confirm').mockReturnValue(true)

    const wrapper = mountSection()
    await wrapper.find('button').trigger('click')
    await flushPromises()

    expect(emptyRecycleBin).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).toContain('Removed 4 files from the recycle bin.')
  })

  it('shows the error message inline when emptying the bin fails', async () => {
    const emptyRecycleBin = vi.fn(async () => {
      throw new Error('recycle bin path is not writable')
    })
    ;(apiService as unknown as { emptyRecycleBin: typeof emptyRecycleBin }).emptyRecycleBin =
      emptyRecycleBin
    vi.spyOn(window, 'confirm').mockReturnValue(true)

    const wrapper = mountSection()
    await wrapper.find('button').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('recycle bin path is not writable')
  })
})
