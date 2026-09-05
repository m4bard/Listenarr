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
import { mount } from '@vue/test-utils'

type ExecCommandHost = { execCommand?: (command: string) => boolean }
type ToastRecord = { level: string; title: string; message: string }

const setExecCommand = (impl: ((command: string) => boolean) | undefined) => {
  const host = document as unknown as ExecCommandHost
  if (impl) {
    host.execCommand = impl
  } else {
    delete host.execCommand
  }
}

/** A non-secure origin: navigator.clipboard is absent, and so is execCommand. */
const useHopelessBrowser = () => {
  vi.stubGlobal('navigator', {})
  setExecCommand(undefined)
}

const loadToasts = async (): Promise<ToastRecord[]> => {
  const { useToast } = await import('@/services/toastService')
  return useToast().toasts as unknown as ToastRecord[]
}

const loadControl = async () => (await import('@/components/form/ApiKeyControl.vue')).default

const mockApi = (overrides: Record<string, unknown>) => {
  vi.doMock('@/services/api', () => ({
    apiService: {
      regenerateApiKey: vi.fn(),
      generateInitialApiKey: vi.fn(),
      ...overrides,
    },
  }))
}

const confirmRegeneration = async () => {
  const confirmModule = await import('@/composables/useConfirm')
  vi.spyOn(confirmModule, 'showConfirm').mockResolvedValue(true as never)
}

const flush = () => new Promise((resolve) => setTimeout(resolve, 0))

describe('ApiKeyControl clipboard failures', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    vi.resetModules()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    setExecCommand(undefined)
  })

  it('tells the user when the copy button cannot reach the clipboard', async () => {
    useHopelessBrowser()
    const toasts = await loadToasts()
    const ApiKeyControl = await loadControl()

    const wrapper = mount(ApiKeyControl, { props: { apiKey: 'MYKEY' }, attachTo: document.body })
    await wrapper.find('button.copy-btn').trigger('click')
    await flush()

    expect(toasts).toHaveLength(1)
    expect(toasts[0].level).toBe('error')
    expect(`${toasts[0].title} ${toasts[0].message}`.toLowerCase()).toContain('copy')

    wrapper.unmount()
  })

  it('does not claim success when the copy silently failed', async () => {
    useHopelessBrowser()
    await loadToasts()
    const ApiKeyControl = await loadControl()

    const wrapper = mount(ApiKeyControl, { props: { apiKey: 'MYKEY' }, attachTo: document.body })
    const copyBtn = wrapper.find('button.copy-btn')
    await copyBtn.trigger('click')
    await flush()

    expect(copyBtn.attributes('aria-label')).toBe('Copy API key')
    expect(copyBtn.attributes('aria-pressed')).toBe('false')

    wrapper.unmount()
  })

  it('reveals and selects the key so it can be copied by hand', async () => {
    useHopelessBrowser()
    await loadToasts()
    const ApiKeyControl = await loadControl()

    const wrapper = mount(ApiKeyControl, { props: { apiKey: 'MYKEY' }, attachTo: document.body })
    const field = wrapper.find('input.api-key-field')
    expect(field.attributes('type')).toBe('password')

    await wrapper.find('button.copy-btn').trigger('click')
    await flush()

    const element = field.element as HTMLInputElement
    expect(field.attributes('type')).toBe('text')
    expect(document.activeElement).toBe(element)
    expect(element.selectionStart).toBe(0)
    expect(element.selectionEnd).toBe('MYKEY'.length)

    wrapper.unmount()
  })

  it('uses the execCommand fallback rather than failing on a non-secure origin', async () => {
    vi.stubGlobal('navigator', {})
    const execCommand = vi.fn(() => true)
    setExecCommand(execCommand)
    const toasts = await loadToasts()
    const ApiKeyControl = await loadControl()

    const wrapper = mount(ApiKeyControl, { props: { apiKey: 'MYKEY' }, attachTo: document.body })
    const copyBtn = wrapper.find('button.copy-btn')
    await copyBtn.trigger('click')
    await flush()

    expect(execCommand).toHaveBeenCalledWith('copy')
    expect(toasts).toHaveLength(0)
    expect(copyBtn.attributes('aria-label')).toBe('Copied!')

    wrapper.unmount()
  })

  // The destructive case. The key really was regenerated and the old one is dead,
  // so reporting a clipboard failure as a regeneration failure invites the user to
  // regenerate again and invalidate the key they were just handed.
  it('keeps the regenerated key and never reports the regeneration as failed', async () => {
    useHopelessBrowser()
    await confirmRegeneration()
    mockApi({ regenerateApiKey: vi.fn().mockResolvedValue({ apiKey: 'NEWKEY' }) })
    const toasts = await loadToasts()
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    const ApiKeyControl = await loadControl()

    const wrapper = mount(ApiKeyControl, { props: { apiKey: 'OLDKEY' }, attachTo: document.body })
    await wrapper.find('button.regen-btn').trigger('click')
    await flush()

    expect(wrapper.emitted()['update:apiKey']).toBeTruthy()
    expect(wrapper.emitted()['update:apiKey']![0]).toEqual(['NEWKEY'])

    const logged = consoleError.mock.calls.map((call) => String(call[0])).join(' ')
    expect(logged).not.toMatch(/generat/i)

    expect(toasts).toHaveLength(1)
    const said = `${toasts[0].title} ${toasts[0].message}`
    expect(said.toLowerCase()).toContain('copy')
    expect(said).not.toMatch(/generat/i)

    wrapper.unmount()
  })

  it('does report a real regeneration failure, and emits nothing', async () => {
    useHopelessBrowser()
    await confirmRegeneration()
    mockApi({ regenerateApiKey: vi.fn().mockRejectedValue(new Error('500')) })
    const toasts = await loadToasts()
    const ApiKeyControl = await loadControl()

    const wrapper = mount(ApiKeyControl, { props: { apiKey: 'OLDKEY' }, attachTo: document.body })
    await wrapper.find('button.regen-btn').trigger('click')
    await flush()

    expect(wrapper.emitted()['update:apiKey']).toBeFalsy()
    expect(toasts).toHaveLength(1)
    expect(toasts[0].level).toBe('error')
    expect(`${toasts[0].title} ${toasts[0].message}`).toMatch(/generat/i)

    wrapper.unmount()
  })

  it('emits nothing when the server answers without a key', async () => {
    useHopelessBrowser()
    await confirmRegeneration()
    mockApi({ regenerateApiKey: vi.fn().mockResolvedValue({}) })
    const toasts = await loadToasts()
    const ApiKeyControl = await loadControl()

    const wrapper = mount(ApiKeyControl, { props: { apiKey: 'OLDKEY' }, attachTo: document.body })
    await wrapper.find('button.regen-btn').trigger('click')
    await flush()

    expect(wrapper.emitted()['update:apiKey']).toBeFalsy()
    expect(toasts).toHaveLength(1)
    expect(toasts[0].level).toBe('error')

    wrapper.unmount()
  })

  it('keeps an initial generated key when the clipboard is unavailable', async () => {
    useHopelessBrowser()
    await confirmRegeneration()
    mockApi({ generateInitialApiKey: vi.fn().mockResolvedValue({ apiKey: 'INITKEY' }) })
    const toasts = await loadToasts()
    const ApiKeyControl = await loadControl()

    const wrapper = mount(ApiKeyControl, { props: { apiKey: '' }, attachTo: document.body })
    await wrapper.find('button.regen-btn').trigger('click')
    await flush()

    expect(wrapper.emitted()['update:apiKey']![0]).toEqual(['INITKEY'])
    expect(toasts).toHaveLength(1)
    expect(`${toasts[0].title} ${toasts[0].message}`).not.toMatch(/generat/i)

    wrapper.unmount()
  })
})
