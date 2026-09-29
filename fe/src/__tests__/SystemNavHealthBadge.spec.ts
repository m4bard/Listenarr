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
import { mount, flushPromises } from '@vue/test-utils'
import type { IndexerHealth, ServiceHealth } from '@/types'

// The badge on the sidebar's System entry: indexer health checks, visible from every page. Until
// now indexer failure showed only on the Indexers settings page, which nobody is looking at during
// an outage. Indexer names are invented.

const healthWith = (indexers?: IndexerHealth): ServiceHealth => ({
  status: indexers?.status ?? 'healthy',
  version: '1.0.0',
  uptime: '1h',
  downloadClients: { status: 'healthy', connected: 0, total: 0, clients: [] },
  externalApis: { status: 'healthy', connected: 0, total: 0, apis: [] },
  indexers,
})

const warningCheck = (kind: string, name: string) => ({
  kind,
  status: 'warning',
  message: `Indexers unavailable due to failures: ${name}`,
  indexerNames: [name],
})

const healthy: IndexerHealth = { status: 'healthy', available: 2, total: 2, checks: [] }
const oneFailing: IndexerHealth = {
  status: 'warning',
  available: 1,
  total: 2,
  checks: [warningCheck('IndexerStatus', 'Alderbrook')],
}

let indexersUpdated: (() => void) | null = null
let reconnected: (() => void) | null = null
const getServiceHealth = vi.fn<() => Promise<ServiceHealth>>()

const mountBadge = async (enabled = true) => {
  vi.doMock('@/services/api', () => ({ apiService: { getServiceHealth } }))
  vi.doMock('@/services/signalr', () => ({
    signalRService: {
      onIndexersUpdated: (cb: () => void) => {
        indexersUpdated = cb
        return () => {
          indexersUpdated = null
        }
      },
      onConnected: (cb: () => void) => {
        reconnected = cb
        return () => {
          reconnected = null
        }
      },
    },
  }))
  const { default: SystemNavHealthBadge } =
    await import('@/components/system/SystemNavHealthBadge.vue')
  const wrapper = mount(SystemNavHealthBadge, { props: { enabled } })
  await flushPromises()
  return wrapper
}

describe('SystemNavHealthBadge', () => {
  beforeEach(() => {
    vi.resetModules()
    getServiceHealth.mockReset()
    indexersUpdated = null
    reconnected = null
  })

  it('shows nothing while every indexer is healthy', async () => {
    getServiceHealth.mockResolvedValue(healthWith(healthy))

    const wrapper = await mountBadge()

    expect(getServiceHealth).toHaveBeenCalledTimes(1)
    expect(wrapper.find('.pill').exists()).toBe(false)
  })

  it('counts the indexer checks that fired, as a warning', async () => {
    getServiceHealth.mockResolvedValue(
      healthWith({
        status: 'warning',
        available: 1,
        total: 3,
        checks: [
          warningCheck('IndexerStatus', 'Alderbrook'),
          warningCheck('IndexerLongTermStatus', 'Birchfield'),
        ],
      }),
    )

    const wrapper = await mountBadge()

    const pill = wrapper.find('.pill')
    expect(pill.text()).toBe('2')
    expect(pill.classes()).toContain('pill-warning')
  })

  it('turns red when nothing is left to ask, even if each check alone is only a warning', async () => {
    // One run each side of the six-hour boundary: two warnings, but every indexer blocked
    getServiceHealth.mockResolvedValue(
      healthWith({
        status: 'error',
        available: 0,
        total: 2,
        checks: [
          warningCheck('IndexerStatus', 'Alderbrook'),
          warningCheck('IndexerLongTermStatus', 'Birchfield'),
        ],
      }),
    )

    const wrapper = await mountBadge()

    expect(wrapper.find('.pill').classes()).toContain('pill-error')
  })

  it('refreshes when an indexer enters or leaves backoff, without a page load', async () => {
    getServiceHealth.mockResolvedValueOnce(healthWith(healthy))
    const wrapper = await mountBadge()
    expect(wrapper.find('.pill').exists()).toBe(false)

    getServiceHealth.mockResolvedValueOnce(healthWith(oneFailing))
    indexersUpdated?.()
    await flushPromises()
    expect(wrapper.find('.pill').text()).toBe('1')

    getServiceHealth.mockResolvedValueOnce(healthWith(healthy))
    indexersUpdated?.()
    await flushPromises()
    expect(wrapper.find('.pill').exists()).toBe(false)
  })

  it('refreshes after the realtime connection comes back', async () => {
    getServiceHealth.mockResolvedValueOnce(healthWith(healthy))
    const wrapper = await mountBadge()

    getServiceHealth.mockResolvedValueOnce(healthWith(oneFailing))
    reconnected?.()
    await flushPromises()

    expect(wrapper.find('.pill').text()).toBe('1')
  })

  it('does not ask for health until enabled, and stops listening when unmounted', async () => {
    getServiceHealth.mockResolvedValue(healthWith(oneFailing))

    const wrapper = await mountBadge(false)
    expect(getServiceHealth).not.toHaveBeenCalled()
    expect(indexersUpdated).toBeNull()

    await wrapper.setProps({ enabled: true })
    await flushPromises()
    expect(getServiceHealth).toHaveBeenCalledTimes(1)
    expect(wrapper.find('.pill').text()).toBe('1')

    wrapper.unmount()
    expect(indexersUpdated).toBeNull()
    expect(reconnected).toBeNull()
  })

  it('stays quiet against a backend that predates indexer health, or when the request fails', async () => {
    getServiceHealth.mockResolvedValueOnce(healthWith(undefined))
    const wrapper = await mountBadge()
    expect(wrapper.find('.pill').exists()).toBe(false)

    getServiceHealth.mockRejectedValueOnce(new Error('Network error'))
    indexersUpdated?.()
    await flushPromises()
    expect(wrapper.find('.pill').exists()).toBe(false)
  })
})
