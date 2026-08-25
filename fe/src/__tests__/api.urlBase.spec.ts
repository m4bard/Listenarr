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

vi.unmock('../services/api')

/**
 * The URL base reaches these URLs through API_BASE_PATH. Anything that also prefixes API_ORIGIN
 * would emit the sub-path twice, which is silently wrong rather than visibly broken, so the shape
 * of the built URL is pinned here.
 */
const loadApi = async (urlBase?: string) => {
  vi.resetModules()
  if (urlBase === undefined) {
    delete (window as unknown as Record<string, unknown>).__listenarrUrlBase
  } else {
    ;(window as unknown as Record<string, unknown>).__listenarrUrlBase = urlBase
  }
  const [{ apiService }, apiBase] = await Promise.all([
    import('@/services/api'),
    import('@/services/apiBase'),
  ])
  return { apiService, apiBase }
}

describe('ApiService URLs under a URL sub-path', () => {
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

  it('builds an image URL that carries the sub-path exactly once', async () => {
    const { apiService } = await loadApi('/example')

    const url = apiService.getImageUrl('/config/cache/images/authors/ASIN000001.jpg')

    expect(url).toBe('/example/api/v1/images/ASIN000001')
  })

  it('leaves the image URL root-absolute at the site root', async () => {
    const { apiService } = await loadApi()

    expect(apiService.getImageUrl('/config/cache/images/authors/ASIN000001.jpg')).toBe(
      '/api/v1/images/ASIN000001',
    )
  })

  it('prefixes a cached image path that is not an API path', async () => {
    const { apiService } = await loadApi('/example')

    // Not an /api path, so it does need the base bolted on rather than being left alone.
    expect(apiService.getImageUrl('/config/cache/images/covers/x.jpg')).toBe(
      '/example/config/cache/images/covers/x.jpg',
    )
  })

  it('builds a request URL with a single sub-path segment', async () => {
    const { apiBase } = await loadApi('/example')

    expect(apiBase.API_BASE_URL).toBe('/example/api/v1')
    expect(apiBase.buildApiPath('/system/logs')).toBe('/example/api/v1/system/logs')
  })
})
