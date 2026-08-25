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
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'

const loadHubUrl = async (urlBase?: string) => {
  vi.resetModules()
  if (urlBase === undefined) {
    delete (window as unknown as Record<string, unknown>).__listenarrUrlBase
  } else {
    ;(window as unknown as Record<string, unknown>).__listenarrUrlBase = urlBase
  }
  return import('@/services/hubUrl')
}

describe('SignalR hub URLs under a URL sub-path', () => {
  beforeEach(() => {
    vi.stubEnv('DEV', false)
    vi.stubEnv('PROD', true)
    vi.stubEnv('VITE_API_BASE_URL', '')
  })

  afterEach(() => {
    vi.unstubAllEnvs()
    vi.resetModules()
    delete (window as unknown as Record<string, unknown>).__listenarrUrlBase
  })

  it('puts the websocket at the site root when there is no sub-path', async () => {
    const { buildHubWebSocketUrl } = await loadHubUrl()

    expect(buildHubWebSocketUrl('/hubs/downloads')).toBe(
      `${window.location.origin.replace(/^http/, 'ws')}/hubs/downloads`,
    )
  })

  it('puts the websocket inside the sub-path, hub by hub', async () => {
    const { buildHubWebSocketUrl } = await loadHubUrl('/example')
    const wsOrigin = window.location.origin.replace(/^http/, 'ws')

    expect(buildHubWebSocketUrl('/hubs/downloads')).toBe(`${wsOrigin}/example/hubs/downloads`)
    expect(buildHubWebSocketUrl('/hubs/settings')).toBe(`${wsOrigin}/example/hubs/settings`)
  })
})
