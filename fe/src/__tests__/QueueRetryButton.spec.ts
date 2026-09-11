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
import { mount } from '@vue/test-utils'
import { describe, it, expect, vi, beforeEach } from 'vitest'

const mountButton = async (
  props: { downloadId: string; status: string; iconOnly?: boolean },
  retryImpl?: (id: string) => Promise<unknown>,
) => {
  const retryBlockedImport = vi.fn(retryImpl ?? (async () => ({ message: 'Import retry queued' })))
  vi.doMock('@/services/api', () => ({ apiService: { retryBlockedImport } }))

  const success = vi.fn()
  const error = vi.fn()
  vi.doMock('@/services/toastService', () => ({ useToast: () => ({ success, error }) }))
  vi.doMock('@/services/errorTracking', () => ({
    errorTracking: { captureException: vi.fn() },
  }))

  const { default: QueueRetryButton } =
    await import('@/components/domain/download/QueueRetryButton.vue')
  const wrapper = mount(QueueRetryButton, { props })
  return { wrapper, retryBlockedImport, success, error }
}

describe('QueueRetryButton', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.clearAllMocks()
  })

  it('is not rendered for a status the endpoint would reject', async () => {
    const { wrapper } = await mountButton({ downloadId: 'd1', status: 'Failed' })
    expect(wrapper.find('[data-test="queue-retry"]').exists()).toBe(false)
  })

  it('is rendered for both casings of the blocked status', async () => {
    const upper = await mountButton({ downloadId: 'd1', status: 'ImportBlocked' })
    expect(upper.wrapper.find('[data-test="queue-retry"]').exists()).toBe(true)

    vi.resetModules()
    const lower = await mountButton({ downloadId: 'd1', status: 'importblocked' })
    expect(lower.wrapper.find('[data-test="queue-retry"]').exists()).toBe(true)
  })

  it('calls the endpoint and tells the parent to refresh', async () => {
    const { wrapper, retryBlockedImport, success } = await mountButton({
      downloadId: 'd1',
      status: 'ImportBlocked',
    })

    await wrapper.get('[data-test="queue-retry"]').trigger('click')
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(retryBlockedImport).toHaveBeenCalledWith('d1')
    expect(success).toHaveBeenCalledTimes(1)
    expect(wrapper.emitted('retried')).toEqual([['d1']])
  })

  it('toasts and stays quiet to the parent when the call fails', async () => {
    const { wrapper, error } = await mountButton(
      { downloadId: 'd1', status: 'ImportBlocked' },
      async () => {
        throw new Error('nope')
      },
    )

    await wrapper.get('[data-test="queue-retry"]').trigger('click')
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(error).toHaveBeenCalledTimes(1)
    expect(wrapper.emitted('retried')).toBeUndefined()
  })

  it('keeps the icon and drops the label when iconOnly is set', async () => {
    const labelled = await mountButton({ downloadId: 'd1', status: 'ImportBlocked' })
    expect(labelled.wrapper.find('.queue-retry-label').exists()).toBe(true)
    expect(labelled.wrapper.get('[data-test="queue-retry"]').classes()).not.toContain('icon-only')

    vi.resetModules()
    const { wrapper } = await mountButton({
      downloadId: 'd1',
      status: 'ImportBlocked',
      iconOnly: true,
    })

    const button = wrapper.get('[data-test="queue-retry"]')
    expect(button.find('.queue-retry-label').exists()).toBe(false)
    expect(button.classes()).toContain('icon-only')
    expect(button.attributes('title')).toBe('Retry the blocked import')
  })
})
