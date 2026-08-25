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

const originalBaseUri = document.baseURI

/**
 * Reloads the module the way a browser would, with the document base the server injected and
 * with the production flags the shipped bundle is built under.
 */
const loadApiBaseWith = async (baseUri: string, viteApiBaseUrl = '/api') => {
  vi.resetModules()
  vi.stubEnv('DEV', false)
  vi.stubEnv('PROD', true)
  vi.stubEnv('VITE_API_BASE_URL', viteApiBaseUrl)
  Object.defineProperty(document, 'baseURI', { value: baseUri, configurable: true })
  return await import('@/services/apiBase')
}

afterEach(() => {
  vi.unstubAllEnvs()
  vi.resetModules()
  Object.defineProperty(document, 'baseURI', { value: originalBaseUri, configurable: true })
})

describe('apiBase under a URL sub-path', () => {
  it('keeps the site-root paths when the document base is the site root', async () => {
    const apiBase = await loadApiBaseWith('http://listenarr.example.com/')

    expect(apiBase.API_BASE_URL).toBe('/api/v1')
    expect(apiBase.API_BASE_PATH).toBe('/api/v1')
    expect(apiBase.API_PATH_PREFIX).toBe('')
    expect(apiBase.API_ORIGIN).toBe('')
    expect(apiBase.API_IMAGES_PATH_PREFIX).toBe('/api/v1/images/')
    expect(apiBase.buildApiPath('/system/info')).toBe('/api/v1/system/info')
  })

  it('puts the API under the base the document was served with', async () => {
    const apiBase = await loadApiBaseWith('http://listenarr.example.com/example/')

    expect(apiBase.API_BASE_URL).toBe('/example/api/v1')
    expect(apiBase.API_BASE_PATH).toBe('/example/api/v1')
    expect(apiBase.API_PATH_PREFIX).toBe('/example')
    expect(apiBase.API_IMAGES_PATH_PREFIX).toBe('/example/api/v1/images/')
    expect(apiBase.buildApiPath('/system/info')).toBe('/example/api/v1/system/info')
  })

  it('keeps the origin empty for a same-origin sub-path so callers do not repeat the prefix', async () => {
    const apiBase = await loadApiBaseWith('http://listenarr.example.com/example/')

    expect(apiBase.API_ORIGIN).toBe('')
    expect(`${apiBase.API_ORIGIN}${apiBase.API_BASE_PATH}/system/logs`).toBe(
      '/example/api/v1/system/logs',
    )
    expect(`${apiBase.API_ORIGIN}${apiBase.API_PATH_PREFIX}/hubs/logs`).toBe('/example/hubs/logs')
  })

  it('resolves a nested base', async () => {
    const apiBase = await loadApiBaseWith('http://listenarr.example.com/media/listenarr/')

    expect(apiBase.API_BASE_PATH).toBe('/media/listenarr/api/v1')
    expect(apiBase.API_PATH_PREFIX).toBe('/media/listenarr')
  })

  it('drops a filename from the base URI rather than treating it as a directory', async () => {
    const apiBase = await loadApiBaseWith('http://listenarr.example.com/example/index.html')
    expect(apiBase.API_BASE_PATH).toBe('/example/api/v1')
  })

  it('uses a cross-host VITE_API_BASE_URL verbatim and reports only its origin', async () => {
    const apiBase = await loadApiBaseWith(
      'http://listenarr.example.com/example/',
      'https://api.example.net/api',
    )

    expect(apiBase.API_BASE_URL).toBe('https://api.example.net/api/v1')
    expect(apiBase.API_ORIGIN).toBe('https://api.example.net')
    expect(apiBase.API_BASE_PATH).toBe('/api/v1')
    expect(apiBase.API_PATH_PREFIX).toBe('')
  })

  it('falls back to the site root when no VITE_API_BASE_URL is configured', async () => {
    const apiBase = await loadApiBaseWith('http://listenarr.example.com/example/', '')

    expect(apiBase.API_BASE_URL).toBe('/example/api/v1')
  })

  it('carries the sub-path through a runtime API version change', async () => {
    const apiBase = await loadApiBaseWith('http://listenarr.example.com/example/')

    expect(apiBase.setApiVersion(2)).toBe(true)
    expect(apiBase.API_BASE_URL).toBe('/example/api/v2')
    expect(apiBase.API_BASE_PATH).toBe('/example/api/v2')
    expect(apiBase.API_PATH_PREFIX).toBe('/example')
    expect(apiBase.API_IMAGES_PATH_PREFIX).toBe('/example/api/v2/images/')
  })

  it('still builds an absolute effective base when there is no window', async () => {
    vi.resetModules()
    vi.stubEnv('DEV', false)
    vi.stubEnv('PROD', true)
    vi.stubEnv('VITE_API_BASE_URL', '/api')
    const originalWindow = globalThis.window
    // @ts-expect-error deliberately reproducing a non-browser evaluation of the module
    delete globalThis.window
    try {
      const apiBase = await import('@/services/apiBase')
      expect(apiBase.EFFECTIVE_API_BASE.startsWith('http://localhost/')).toBe(true)
      expect(apiBase.EFFECTIVE_API_BASE.endsWith('/api/v1')).toBe(true)
    } finally {
      globalThis.window = originalWindow
    }
  })
})
