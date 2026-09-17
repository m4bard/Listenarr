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
import { describe, it, beforeEach, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref } from 'vue'
import { PhCheckCircle, PhXCircle, PhQuestion } from '@phosphor-icons/vue'
import type { ClientStatus } from '@/types'

// The three states ClientStatus documents. The backend used to only ever send the first,
// and this view used to render everything that was not the first as a red failure.
const CONNECTED = 'connected'
const DISCONNECTED = 'disconnected'
const UNKNOWN = 'unknown'

const readSystemViewSource = async () => {
  const fs = await import('fs')
  const path = await import('path')
  return fs.readFileSync(path.resolve(__dirname, '../views/system/SystemView.vue'), 'utf-8')
}

// Pulls the declared colour out of one `.client-indicator.<state>` rule.
const readIndicatorColour = (source: string, state: string) => {
  const rule = new RegExp(`\\.client-indicator\\.${state}\\s*\\{([^}]*)\\}`)
  const body = source.match(rule)?.[1]
  return body?.match(/(?:^|\n)\s*color:\s*([^;]+);/)?.[1]?.trim()
}

const mountSystemView = async (clients: ClientStatus[]) => {
  vi.doMock('@/composables/useSystemLogs', () => ({
    useSystemLogs: () => ({ logs: ref([]), isConnected: ref(true), clearLogs: vi.fn() }),
  }))

  vi.doMock('@/composables/useSignalR', () => ({
    useSignalR: () => ({ isConnected: ref(true) }),
  }))

  vi.doMock('vue-router', () => ({
    useRouter: () => ({ push: vi.fn() }),
  }))

  vi.doMock('@/services/api', () => ({
    getSystemInfo: vi.fn(async () => null),
    getStorageInfo: vi.fn(async () => null),
    getServiceHealth: vi.fn(async () => ({
      status: 'warning',
      version: '1.0.0',
      uptime: '1h',
      downloadClients: {
        status: 'warning',
        connected: clients.filter((c) => c.status === CONNECTED).length,
        total: clients.length,
        clients,
      },
      externalApis: { status: 'healthy', connected: 0, total: 0, apis: [] },
    })),
    downloadLogs: vi.fn(),
  }))

  const { default: SystemView } = await import('@/views/system/SystemView.vue')
  const wrapper = mount(SystemView, {
    global: {
      stubs: {
        // Render the slots: the client rows live inside this card.
        StatusCard: { template: '<div><slot name="header-badge" /><slot /></div>' },
        InfoCard: true,
        StorageDisksList: true,
        LoadingState: true,
      },
    },
  })

  await new Promise((resolve) => setTimeout(resolve, 10))
  return wrapper
}

const client = (name: string, status: string): ClientStatus => ({ name, status, type: 'qbittorrent' })

describe('SystemView download client status', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.clearAllMocks()
  })

  it('does not render an unknown client as a failure', async () => {
    const wrapper = await mountSystemView([client('cannot-tell', UNKNOWN)])

    const row = wrapper.get('.client-status')
    // The icon carries the tone. Amber, not the red a real failure gets.
    expect(row.find('.warning').exists()).toBe(true)
    expect(row.find('.error').exists()).toBe(false)
    expect(row.find('.success').exists()).toBe(false)

    // The tone alone is not enough. A question mark says "could not find out";
    // a cross says "down", whatever colour it is painted.
    expect(row.findComponent(PhQuestion).exists()).toBe(true)
    expect(row.findComponent(PhXCircle).exists()).toBe(false)
    expect(row.findComponent(PhCheckCircle).exists()).toBe(false)

    const indicator = row.get('.client-indicator')
    expect(indicator.classes()).toContain(UNKNOWN)
    expect(indicator.classes()).not.toContain(DISCONNECTED)

    wrapper.unmount()
  })

  it('renders each of the three states distinctly', async () => {
    const wrapper = await mountSystemView([
      client('up', CONNECTED),
      client('down', DISCONNECTED),
      client('cannot-tell', UNKNOWN),
    ])

    const rows = wrapper.findAll('.client-status')
    expect(rows).toHaveLength(3)

    expect(rows[0].find('.success').exists()).toBe(true)
    expect(rows[1].find('.error').exists()).toBe(true)
    expect(rows[2].find('.warning').exists()).toBe(true)

    expect(rows[0].findComponent(PhCheckCircle).exists()).toBe(true)
    expect(rows[1].findComponent(PhXCircle).exists()).toBe(true)
    expect(rows[2].findComponent(PhQuestion).exists()).toBe(true)

    const tones = rows.map((row) => row.get('.client-indicator').classes().slice(-1)[0])
    expect(new Set(tones).size).toBe(3)

    wrapper.unmount()
  })

  it('still renders a disconnected client as a failure', async () => {
    const wrapper = await mountSystemView([client('down', DISCONNECTED)])

    const row = wrapper.get('.client-status')
    expect(row.find('.error').exists()).toBe(true)
    expect(row.find('.warning').exists()).toBe(false)
    expect(row.findComponent(PhXCircle).exists()).toBe(true)
    expect(row.findComponent(PhQuestion).exists()).toBe(false)

    wrapper.unmount()
  })

  it('gives unknown its own colour in the stylesheet, not the failure colour', async () => {
    const source = await readSystemViewSource()

    const unknown = readIndicatorColour(source, UNKNOWN)
    const disconnected = readIndicatorColour(source, DISCONNECTED)
    const connected = readIndicatorColour(source, CONNECTED)

    expect(unknown).toBeDefined()
    expect(disconnected).toBeDefined()
    expect(connected).toBeDefined()
    // A regression that folds unknown back in with disconnected would pass every
    // render assertion above, because both would still be styled. This is what
    // catches it.
    expect(unknown).not.toBe(disconnected)
    expect(unknown).not.toBe(connected)
  })
})
