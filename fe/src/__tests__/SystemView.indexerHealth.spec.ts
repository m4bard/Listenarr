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
import type { IndexerHealth, ServiceHealth } from '@/types'

// The System view's Indexers card: the two indexer health checks (failing now, failing for more
// than six hours) as the backend reports them. Indexer names are invented.

const baseHealth: ServiceHealth = {
  status: 'healthy',
  version: '1.0.0',
  uptime: '1h',
  downloadClients: { status: 'healthy', connected: 0, total: 0, clients: [] },
  externalApis: { status: 'healthy', connected: 0, total: 0, apis: [] },
}

const mockDependencies = (health: ServiceHealth) => {
  vi.doMock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))
  vi.doMock('@/composables/useSignalR', () => ({
    useSignalR: () => ({ isConnected: ref(false) }),
  }))
  vi.doMock('@/composables/useSystemLogs', () => ({
    useSystemLogs: () => ({ logs: ref([]), isConnected: ref(false), clearLogs: vi.fn() }),
  }))
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

const mountWith = async (indexers: IndexerHealth | undefined) => {
  mockDependencies({ ...baseHealth, indexers })
  const { default: SystemView } = await import('@/views/system/SystemView.vue')
  const wrapper = mount(SystemView, {
    global: { stubs: { RouterLink: { template: '<a><slot /></a>' } } },
  })
  await flushPromises()
  return wrapper
}

describe('SystemView indexer health', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.clearAllMocks()
  })

  it('shows the long-term check with its own wording, distinct from a current failure', async () => {
    const wrapper = await mountWith({
      status: 'warning',
      available: 1,
      total: 3,
      checks: [
        {
          kind: 'IndexerStatus',
          status: 'warning',
          message: 'Indexers unavailable due to failures: Alderbrook',
          indexerNames: ['Alderbrook'],
        },
        {
          kind: 'IndexerLongTermStatus',
          status: 'warning',
          message: 'Indexers unavailable due to failures for more than 6 hours: Birchfield',
          indexerNames: ['Birchfield'],
        },
      ],
    })

    const card = wrapper.find('[data-testid="indexer-health"]')
    expect(card.exists()).toBe(true)
    expect(card.find('.status-badge').text()).toBe('1/3')
    expect(card.find('.status-badge').classes()).toContain('warning')

    const rows = card.findAll('[data-testid="indexer-health-check"]')
    expect(rows.map((row) => row.text())).toEqual([
      'Indexers unavailable due to failures: Alderbrook',
      'Indexers unavailable due to failures for more than 6 hours: Birchfield',
    ])
    expect(rows[1]!.attributes('data-kind')).toBe('IndexerLongTermStatus')
  })

  it('marks an all-indexers check as an error', async () => {
    const wrapper = await mountWith({
      status: 'error',
      available: 0,
      total: 2,
      checks: [
        {
          kind: 'IndexerStatus',
          status: 'error',
          message: 'All indexers are unavailable due to failures',
          indexerNames: ['Alderbrook', 'Birchfield'],
        },
      ],
    })

    const card = wrapper.find('[data-testid="indexer-health"]')
    const row = card.find('[data-testid="indexer-health-check"]')
    expect(row.classes()).toContain('error')
    expect(card.find('.status-badge').classes()).toContain('error')
  })

  it('says every indexer is available when no check fired', async () => {
    const wrapper = await mountWith({ status: 'healthy', available: 2, total: 2, checks: [] })

    const card = wrapper.find('[data-testid="indexer-health"]')
    expect(card.find('.status-badge').text()).toBe('2/2')
    expect(card.findAll('[data-testid="indexer-health-check"]')).toHaveLength(0)
    expect(card.text()).toContain('All indexers available')
  })

  it('does not break against a backend that predates indexer health', async () => {
    const wrapper = await mountWith(undefined)

    // Control: the rest of the page rendered
    expect(wrapper.text()).toContain('Download Clients')
    expect(wrapper.find('[data-testid="indexer-health"]').exists()).toBe(false)
  })
})
