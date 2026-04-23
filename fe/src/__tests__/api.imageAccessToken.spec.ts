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

vi.unmock('../services/api')

import { apiService as svc } from '../services/api'
import { API_BASE_PATH } from '@/services/apiBase'
import { resetCache as resetStartupConfigCache } from '../services/startupConfigCache'
import { sessionTokenManager } from '../utils/sessionToken'

type ApiServiceInternals = typeof svc & {
  clearImageAccessToken?: () => void
}

describe('ApiService image access token flow', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    sessionTokenManager.clearToken()
    resetStartupConfigCache()
    ;(svc as ApiServiceInternals).clearImageAccessToken?.()
  })

  afterEach(() => {
    vi.resetAllMocks()
    sessionTokenManager.clearToken()
    resetStartupConfigCache()
    ;(svc as ApiServiceInternals).clearImageAccessToken?.()
  })

  it('appends the cached image token to backend image URLs', async () => {
    sessionTokenManager.setToken('session-token-123')

    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = String(input)
        if (url.endsWith(`${API_BASE_PATH}/account/image-token`)) {
          return {
            ok: true,
            status: 200,
            json: async () => ({
              token: 'image-token-123',
              expiresAt: '2099-01-01T00:00:00Z',
            }),
          }
        }

        throw new Error(`Unexpected fetch: ${url}`)
      }),
    )

    const token = await svc.ensureImageAccessTokenForCurrentAuth()

    expect(token).toBe('image-token-123')
    expect(svc.getImageUrl(`${API_BASE_PATH}/images/ASIN000001`)).toBe(
      `${API_BASE_PATH}/images/ASIN000001?t=image-token-123`,
    )
  })

  it('returns a signed backend image URL without fetching the image as a blob', async () => {
    sessionTokenManager.setToken('session-token-456')

    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input)
      if (url.endsWith(`${API_BASE_PATH}/account/image-token`)) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            token: 'image-token-456',
            expiresAt: '2099-01-01T00:00:00Z',
          }),
        }
      }

      throw new Error(`Unexpected fetch: ${url}`)
    })

    vi.stubGlobal('fetch', fetchMock)

    const url = await svc.fetchImageObjectUrl(`${API_BASE_PATH}/images/ASIN000002`)

    expect(url).toBe(`${API_BASE_PATH}/images/ASIN000002?t=image-token-456`)
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(String(fetchMock.mock.calls[0][0])).toContain('/account/image-token')
  })
})
