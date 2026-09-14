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
import { ref } from 'vue'
import type { ServiceHealth } from '@/types'

const baseHealth: ServiceHealth = {
  status: 'healthy',
  version: '1.0.0',
  uptime: '1h',
  downloadClients: {
    status: 'healthy',
    connected: 0,
    total: 0,
    clients: [],
  },
  externalApis: {
    status: 'healthy',
    connected: 0,
    total: 0,
    apis: [],
  },
}

const mockRouter = () => {
  vi.doMock('vue-router', () => ({
    useRouter: () => ({ push: vi.fn() }),
  }))
}

const mockComposables = () => {
  vi.doMock('@/composables/useSignalR', () => ({
    useSignalR: () => ({ isConnected: ref(false) }),
  }))
  vi.doMock('@/composables/useSystemLogs', () => ({
    useSystemLogs: () => ({
      logs: ref([]),
      isConnected: ref(false),
      clearLogs: vi.fn(),
    }),
  }))
}

const mockApi = (health: ServiceHealth) => {
  vi.doMock('@/services/api', () => ({
    getSystemInfo: vi.fn(async () => ({
      version: '1.0.0',
      operatingSystem: 'Linux',
      runtime: '.NET',
      uptime: '1h',
      memory: {
        usedBytes: 0,
        totalBytes: 0,
        freeBytes: 0,
        usedPercentage: 0,
        usedFormatted: '0',
        totalFormatted: '0',
        freeFormatted: '0',
      },
      cpu: { usagePercentage: 0, processorCount: 1 },
      startTime: new Date().toISOString(),
    })),
    getStorageInfo: vi.fn(async () => ({
      usedBytes: 0,
      totalBytes: 0,
      freeBytes: 0,
      usedPercentage: 0,
      usedFormatted: '0',
      totalFormatted: '0',
      freeFormatted: '0',
      driveName: '/',
      status: 'available',
      disks: [],
    })),
    getServiceHealth: vi.fn(async () => health),
    downloadLogs: vi.fn(async () => undefined),
  }))
}

const mountSystemView = async () => {
  const { default: SystemViewComponent } = await import('@/views/system/SystemView.vue')
  const wrapper = mount(SystemViewComponent, {
    global: {
      stubs: {
        RouterLink: { template: '<a><slot /></a>' },
      },
    },
  })

  await flushPromises()
  return wrapper
}

describe('SystemView download client status', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.clearAllMocks()
    mockRouter()
    mockComposables()
  })

  it('renders a disconnected client with its failure reason and not as connected', async () => {
    mockApi({
      ...baseHealth,
      downloadClients: {
        status: 'error',
        connected: 0,
        total: 1,
        clients: [
          { name: 'qBittorrent', status: 'disconnected', type: 'qbittorrent', failureReason: 'timeout' },
        ],
      },
    })

    const wrapper = await mountSystemView()

    const indicator = wrapper.find('.client-indicator.disconnected')
    expect(indicator.exists()).toBe(true)
    expect(indicator.text()).toBe('disconnected')
    expect(wrapper.find('.client-indicator.connected').exists()).toBe(false)

    const row = wrapper.find('.client-status')
    expect(row.attributes('title')).toBe('timeout')
  })

  it('renders a reachable client as connected, distinct from disconnected', async () => {
    mockApi({
      ...baseHealth,
      downloadClients: {
        status: 'healthy',
        connected: 1,
        total: 1,
        clients: [{ name: 'qBittorrent', status: 'connected', type: 'qbittorrent' }],
      },
    })

    const wrapper = await mountSystemView()

    const indicator = wrapper.find('.client-indicator.connected')
    expect(indicator.exists()).toBe(true)
    expect(indicator.text()).toBe('connected')
    expect(wrapper.find('.client-indicator.disconnected').exists()).toBe(false)
  })

  it('renders an unpolled client as unknown, not silently as connected', async () => {
    mockApi({
      ...baseHealth,
      downloadClients: {
        status: 'warning',
        connected: 0,
        total: 1,
        clients: [{ name: 'Sabnzbd', status: 'unknown', type: 'sabnzbd' }],
      },
    })

    const wrapper = await mountSystemView()

    const indicator = wrapper.find('.client-indicator.unknown')
    expect(indicator.exists()).toBe(true)
    expect(indicator.text()).toBe('unknown')
    expect(wrapper.find('.client-indicator.connected').exists()).toBe(false)
  })
})
