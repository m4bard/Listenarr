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
import { afterEach, describe, expect, it, vi } from 'vitest'

// The shared test setup replaces the SignalR service with a stub for component tests. This spec is
// about the real URL resolution, so it opts back in to the module under test.
vi.unmock('@/services/signalr')

const originalBaseUri = document.baseURI
const originalWebSocket = globalThis.WebSocket

/**
 * The downloads hub is opened with a bare WebSocket, so stubbing the constructor is enough to
 * see the URL the service resolved from the document base.
 */
const connectAndCaptureHubUrl = async (baseUri: string) => {
  const opened: string[] = []

  class CapturingWebSocket {
    static readonly OPEN = 1
    static readonly CLOSED = 3
    readonly readyState = 0
    onopen: (() => void) | null = null
    onmessage: (() => void) | null = null
    onerror: (() => void) | null = null
    onclose: (() => void) | null = null
    constructor(url: string) {
      opened.push(url)
    }
    send() {}
    close() {}
  }

  globalThis.WebSocket = CapturingWebSocket as unknown as typeof WebSocket

  vi.resetModules()
  vi.stubEnv('DEV', false)
  vi.stubEnv('PROD', true)
  vi.stubEnv('VITE_API_BASE_URL', '/api')
  Object.defineProperty(document, 'baseURI', { value: baseUri, configurable: true })

  const { signalRService } = await import('@/services/signalr')
  await signalRService.connect()

  return opened.at(-1)
}

afterEach(() => {
  globalThis.WebSocket = originalWebSocket
  vi.unstubAllEnvs()
  vi.resetModules()
  Object.defineProperty(document, 'baseURI', { value: originalBaseUri, configurable: true })
})

// The hub is opened on the browser's own origin, not on the host in the document base, so the
// expectation is built from wherever the test environment happens to be serving.
const wsOrigin = () => window.location.origin.replace(/^http/, 'ws')

describe('download hub URL under a URL sub-path', () => {
  it('opens the hub at the site root when the app is served there', async () => {
    expect(await connectAndCaptureHubUrl('http://listenarr.example.com/')).toBe(
      `${wsOrigin()}/hubs/downloads`,
    )
  })

  it('opens the hub under the sub-path the document was served with', async () => {
    expect(await connectAndCaptureHubUrl('http://listenarr.example.com/example/')).toBe(
      `${wsOrigin()}/example/hubs/downloads`,
    )
  })
})
