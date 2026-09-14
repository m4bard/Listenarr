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
import { describe, it, expect, vi, afterEach } from 'vitest'

function stubFetch() {
  const fetchMock = vi.fn(() =>
    Promise.resolve(
      new Response(JSON.stringify({ message: 'ok', deletedCount: 0 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    ),
  )
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

describe('ApiService cleanupOldHistory', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('omits the days query param when called without an override, so the server uses the configured setting', async () => {
    vi.resetModules()
    const fetchMock = stubFetch()

    const actual = await vi.importActual<typeof import('@/services/api')>('@/services/api')
    await actual.apiService.cleanupOldHistory()

    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [requestInfo, options] = fetchMock.mock.calls[0] as [RequestInfo, RequestInit]
    expect(String(requestInfo)).toContain('/history/cleanup')
    expect(String(requestInfo)).not.toContain('days=')
    expect(options.method).toBe('DELETE')
  })

  it('still sends an explicit days override when the caller passes one', async () => {
    vi.resetModules()
    const fetchMock = stubFetch()

    const actual = await vi.importActual<typeof import('@/services/api')>('@/services/api')
    await actual.apiService.cleanupOldHistory(30)

    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [requestInfo, options] = fetchMock.mock.calls[0] as [RequestInfo, RequestInit]
    expect(String(requestInfo)).toContain('/history/cleanup?days=30')
    expect(options.method).toBe('DELETE')
  })
})
