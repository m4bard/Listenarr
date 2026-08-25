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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const { mockHubUrls } = vi.hoisted(() => ({ mockHubUrls: [] as string[] }))

vi.mock('@microsoft/signalr', () => {
  class HubConnectionBuilder {
    withUrl(url: string) {
      mockHubUrls.push(url)
      return this
    }
    withAutomaticReconnect() {
      return this
    }
    configureLogging() {
      return this
    }
    build() {
      return {
        on: () => {},
        onclose: () => {},
        onreconnecting: () => {},
        onreconnected: () => {},
        start: () => Promise.resolve(),
        stop: () => Promise.resolve(),
      }
    }
  }

  return {
    HubConnectionBuilder,
    HttpTransportType: { WebSockets: 1, ServerSentEvents: 2 },
    LogLevel: { Information: 2 },
  }
})

const originalBaseUri = document.baseURI

const connectWithDocumentBase = async (baseUri: string) => {
  vi.resetModules()
  vi.stubEnv('DEV', false)
  vi.stubEnv('PROD', true)
  vi.stubEnv('VITE_API_BASE_URL', '/api')
  Object.defineProperty(document, 'baseURI', { value: baseUri, configurable: true })

  const { useSystemLogs } = await import('@/composables/useSystemLogs')
  await useSystemLogs(100, false).connect()

  return mockHubUrls.at(-1)
}

beforeEach(() => {
  mockHubUrls.length = 0
})

afterEach(() => {
  vi.unstubAllEnvs()
  vi.resetModules()
  Object.defineProperty(document, 'baseURI', { value: originalBaseUri, configurable: true })
})

describe('useSystemLogs hub URL', () => {
  it('connects to the site-root hub when the app is served at the site root', async () => {
    expect(await connectWithDocumentBase('http://listenarr.example.com/')).toBe('/hubs/logs')
  })

  it('connects to the hub under the sub-path the document was served with', async () => {
    expect(await connectWithDocumentBase('http://listenarr.example.com/example/')).toBe(
      '/example/hubs/logs',
    )
  })

  it('does not append the versioned API base to the hub path', async () => {
    const hubUrl = await connectWithDocumentBase('http://listenarr.example.com/example/')

    expect(hubUrl).not.toContain('/api')
  })
})
