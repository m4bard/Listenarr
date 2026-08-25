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

/**
 * apiBase computes its exported bindings when the module is first evaluated, so every case here
 * sets the injected URL base first and then imports a fresh copy of the module.
 */
const loadApiBase = async (urlBase?: string) => {
  vi.resetModules()
  if (urlBase === undefined) {
    delete (window as unknown as Record<string, unknown>).__listenarrUrlBase
  } else {
    ;(window as unknown as Record<string, unknown>).__listenarrUrlBase = urlBase
  }
  return import('@/services/apiBase')
}

describe('apiBase under a URL sub-path', () => {
  beforeEach(() => {
    // The production build is the case that matters: in dev the Vite proxy owns the prefix.
    vi.stubEnv('DEV', false)
    vi.stubEnv('PROD', true)
    vi.stubEnv('VITE_API_BASE_URL', '')
  })

  afterEach(() => {
    vi.unstubAllEnvs()
    vi.unstubAllGlobals()
    vi.resetModules()
    delete (window as unknown as Record<string, unknown>).__listenarrUrlBase
  })

  it('still absolutizes the API base when there is no window at all', async () => {
    // computeEffectiveApiBase has a no-window branch for code paths that run outside a browser.
    // Reading the injected global must not have broken it.
    vi.stubGlobal('window', undefined)
    vi.resetModules()
    const apiBase = await import('@/services/apiBase')

    expect(apiBase.API_BASE_URL).toBe('/api/v1')
    expect(apiBase.EFFECTIVE_API_BASE).toBe('http://localhost/api/v1')
  })

  it('keeps the root-absolute API root when there is no sub-path', async () => {
    const apiBase = await loadApiBase()

    expect(apiBase.API_BASE_URL).toBe('/api/v1')
    expect(apiBase.API_BASE_PATH).toBe('/api/v1')
    expect(apiBase.API_IMAGES_PATH_PREFIX).toBe('/api/v1/images/')
    expect(apiBase.buildApiPath('/library')).toBe('/api/v1/library')
  })

  it('prefixes the API root with the injected sub-path', async () => {
    const apiBase = await loadApiBase('/example')

    expect(apiBase.API_BASE_URL).toBe('/example/api/v1')
    expect(apiBase.API_BASE_PATH).toBe('/example/api/v1')
    expect(apiBase.API_IMAGES_PATH_PREFIX).toBe('/example/api/v1/images/')
    expect(apiBase.buildApiPath('/library')).toBe('/example/api/v1/library')
  })

  it('builds a hub URL that is inside the sub-path rather than at the site root', async () => {
    const atRoot = await loadApiBase()
    expect(atRoot.API_ORIGIN).toBe('')
    expect(atRoot.buildHubUrl('/hubs/logs')).toBe('/hubs/logs')

    const underSubPath = await loadApiBase('/example')
    expect(underSubPath.API_ORIGIN).toBe('/example')
    expect(underSubPath.buildHubUrl('/hubs/logs')).toBe('/example/hubs/logs')
  })

  it('lets an explicitly configured API base win, since it can name another host', async () => {
    vi.stubEnv('VITE_API_BASE_URL', 'https://api.example.test/api')
    const apiBase = await loadApiBase('/example')

    expect(apiBase.API_BASE_URL).toBe('https://api.example.test/api/v1')
    expect(apiBase.API_BASE_PATH).toBe('/api/v1')
  })

  it('keeps the sub-path when the API version is changed at runtime', async () => {
    const apiBase = await loadApiBase('/example')

    expect(apiBase.setApiVersion('2')).toBe(true)

    expect(apiBase.API_BASE_URL).toBe('/example/api/v2')
    expect(apiBase.API_BASE_PATH).toBe('/example/api/v2')
    expect(apiBase.API_IMAGES_PATH_PREFIX).toBe('/example/api/v2/images/')
    expect(apiBase.API_ORIGIN).toBe('/example')
    expect(apiBase.EFFECTIVE_API_BASE).toBe('/example/api/v2')
  })
})
