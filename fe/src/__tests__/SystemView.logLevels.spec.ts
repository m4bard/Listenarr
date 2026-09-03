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
import type { LogEntry } from '@/types'

// The severities the backend actually broadcasts over the log hub. SignalRLogSink
// maps every Serilog level onto one of these four PascalCase strings, and
// SystemLogParser maps the level tags it reads out of the log files onto the same
// four. Fixtures here must keep that casing: a lowercase fixture would test a
// shape the server never sends.
const BACKEND_LOG_LEVELS = ['Debug', 'Info', 'Warning', 'Error'] as const

// The severity classes the component's own stylesheet actually selects on.
const readStyledSeverities = async () => {
  const fs = await import('fs')
  const path = await import('path')
  const source = fs.readFileSync(path.resolve(__dirname, '../views/system/SystemView.vue'), 'utf-8')
  return [...source.matchAll(/\.log-entry\.([a-z]+) \.log-level\b/g)].map((match) => match[1])
}

const makeLog = (level: string, index: number): LogEntry => ({
  id: `log-${index}`,
  timestamp: new Date('2026-01-01T12:00:00Z').toISOString(),
  level,
  message: `message for ${level}`,
  source: 'Application',
})

const mountSystemView = async (logs: LogEntry[]) => {
  vi.doMock('@/composables/useSystemLogs', () => ({
    useSystemLogs: () => ({
      logs: ref(logs),
      isConnected: ref(true),
      clearLogs: vi.fn(),
    }),
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
      status: 'healthy',
      version: '1.0.0',
      uptime: '1h',
      downloadClients: { status: 'healthy', connected: 0, total: 0, clients: [] },
      externalApis: { status: 'healthy', connected: 0, total: 0, apis: [] },
    })),
    downloadLogs: vi.fn(),
  }))

  const { default: SystemView } = await import('@/views/system/SystemView.vue')
  const wrapper = mount(SystemView, {
    global: {
      stubs: {
        StatusCard: true,
        InfoCard: true,
        StorageDisksList: true,
        LoadingState: true,
      },
    },
  })

  await new Promise((resolve) => setTimeout(resolve, 10))
  return wrapper
}

describe('SystemView recent logs severity styling', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.clearAllMocks()
  })

  it('renders a lowercase severity class for each PascalCase level the hub broadcasts', async () => {
    const logs = BACKEND_LOG_LEVELS.map((level, index) => makeLog(level, index))
    const wrapper = await mountSystemView(logs)

    const entries = wrapper.findAll('.log-entry')
    expect(entries).toHaveLength(BACKEND_LOG_LEVELS.length)

    BACKEND_LOG_LEVELS.forEach((level, index) => {
      const classes = entries[index].classes()
      expect(classes).toContain(level.toLowerCase())
      // The stylesheet only ever selects the lowercase form, so the raw
      // PascalCase level must not be what lands on the element.
      expect(classes).not.toContain(level)
    })

    wrapper.unmount()
  })

  it('styles every severity class it renders', async () => {
    const logs = BACKEND_LOG_LEVELS.map((level, index) => makeLog(level, index))
    const wrapper = await mountSystemView(logs)
    const styledSeverities = await readStyledSeverities()

    wrapper.findAll('.log-entry').forEach((entry) => {
      const severity = entry.classes().find((name) => name !== 'log-entry')
      expect(severity).toBeDefined()
      expect(styledSeverities).toContain(severity)
    })

    wrapper.unmount()
  })
})
