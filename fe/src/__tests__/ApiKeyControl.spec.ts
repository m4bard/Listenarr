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
import PasswordInput from '@/components/form/PasswordInput.vue'

describe('ApiKeyControl', () => {
  beforeEach(async () => {
    vi.restoreAllMocks()
    // Reset imported modules so doMock can take effect for each test
    vi.resetModules()
  })

  it('copies to clipboard when copy button clicked', async () => {
    const writeMock = vi.fn().mockResolvedValue(undefined)
    // @ts-expect-error - provide fake clipboard
    global.navigator = { clipboard: { writeText: writeMock } } as unknown

    const { default: ApiKeyControl } = await import('@/components/ui/ApiKeyControl.vue')
    const wrapper = mount(ApiKeyControl, {
      props: { apiKey: 'MYKEY' },
      global: { components: { PasswordInput } },
    })

    const copyBtn = wrapper.find('button.copy-btn')
    expect(copyBtn.exists()).toBe(true)

    await copyBtn.trigger('click')
    expect(writeMock).toHaveBeenCalledWith('MYKEY')
  })

  it('regenerates key and emits update when confirmed', async () => {
    const writeMock = vi.fn().mockResolvedValue(undefined)
    // @ts-expect-error - override navigator clipboard in jsdom test environment
    global.navigator = { clipboard: { writeText: writeMock } } as unknown

    const confirmModule = await import('@/composables/useConfirm')
    vi.spyOn(confirmModule, 'showConfirm').mockResolvedValue(true as unknown)
    // Mock the api module for this test to return a new key on regenerate
    vi.doMock('@/services/api', () => ({
      apiService: {
        regenerateApiKey: vi.fn().mockResolvedValue({ apiKey: 'NEWKEY' }),
        generateInitialApiKey: vi.fn(),
      },
    }))

    const { default: ApiKeyControl } = await import('@/components/ui/ApiKeyControl.vue')
    const wrapper = mount(ApiKeyControl, {
      props: { apiKey: 'OLDKEY' },
      global: { components: { PasswordInput } },
    })

    // Call the internal handler directly to avoid DOM-event quirks in VTU
    const setupState = (wrapper.vm as unknown).$?.setupState || (wrapper.vm as unknown).$setup
    await (setupState.onRegenerate as () => Promise<void>)()
    // wait for async handlers and promise resolution
    await new Promise((r) => setTimeout(r, 0))

    // Ensure underlying API was called
    const apiModule = await import('@/services/api')




    expect((apiModule.apiService.regenerateApiKey as unknown).mock).toBeTruthy()
    expect((apiModule.apiService.regenerateApiKey as unknown).mock.calls.length).toBeGreaterThan(0)

    // Should emit update:apiKey with new key
    expect(wrapper.emitted()['update:apiKey']).toBeTruthy()
    expect(wrapper.emitted()['update:apiKey']![0]).toEqual(['NEWKEY'])

    expect(writeMock).toHaveBeenCalledWith('NEWKEY')
  })

  it('generates initial key when none exists', async () => {
    const writeMock = vi.fn().mockResolvedValue(undefined)
    // @ts-expect-error - override navigator clipboard in jsdom test environment
    global.navigator = { clipboard: { writeText: writeMock } } as unknown

    const confirmModule = await import('@/composables/useConfirm')
    vi.spyOn(confirmModule, 'showConfirm').mockResolvedValue(true as unknown)
    // Mock generateInitialApiKey to return a new key for initial generation
    vi.doMock('@/services/api', () => ({
      apiService: {
        regenerateApiKey: vi.fn(),
        generateInitialApiKey: vi.fn().mockResolvedValue({ apiKey: 'INITKEY' }),
      },
    }))

    const { default: ApiKeyControl } = await import('@/components/ui/ApiKeyControl.vue')
    const wrapper = mount(ApiKeyControl, {
      props: { apiKey: '' },
      global: { components: { PasswordInput } },
    })

    const regenBtn = wrapper.find('button.regen-btn')
    await regenBtn.trigger('click')
    await new Promise((r) => setTimeout(r, 0))

    // Ensure underlying API was called
    const apiModule = await import('@/services/api')
    expect((apiModule.apiService.generateInitialApiKey as unknown).mock).toBeTruthy()
    expect((apiModule.apiService.generateInitialApiKey as unknown).mock.calls.length).toBeGreaterThan(0)

    expect(wrapper.emitted()['update:apiKey']).toBeTruthy()
    expect(wrapper.emitted()['update:apiKey']![0]).toEqual(['INITKEY'])
    expect(writeMock).toHaveBeenCalledWith('INITKEY')
  })
})
