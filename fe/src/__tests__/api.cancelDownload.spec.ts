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

const stubFetch = () => {
  const fetchMock = vi.fn(() =>
    Promise.resolve(
      new Response(JSON.stringify({ message: 'Download deleted successfully', id: 'd1' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    ),
  )
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const requestedUrl = (fetchMock: ReturnType<typeof stubFetch>): string => {
  const [requestInfo] = fetchMock.mock.calls[0] as [RequestInfo, RequestInit]
  return String(requestInfo)
}

describe('ApiService cancelDownload', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('asks for the record only when the caller says not to touch the client', async () => {
    vi.resetModules()
    const fetchMock = stubFetch()

    const actual = await vi.importActual<typeof import('@/services/api')>('@/services/api')
    await actual.apiService.cancelDownload('d1', false)

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(requestedUrl(fetchMock)).toContain('/downloads/d1?removeFromClient=false')
  })

  it('asks for client removal when the caller says so', async () => {
    vi.resetModules()
    const fetchMock = stubFetch()

    const actual = await vi.importActual<typeof import('@/services/api')>('@/services/api')
    await actual.apiService.cancelDownload('d1', true)

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(requestedUrl(fetchMock)).toContain('/downloads/d1?removeFromClient=true')
  })

  it('defaults to client removal, matching the server default', async () => {
    vi.resetModules()
    const fetchMock = stubFetch()

    const actual = await vi.importActual<typeof import('@/services/api')>('@/services/api')
    await actual.apiService.cancelDownload('d1')

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(requestedUrl(fetchMock)).toContain('/downloads/d1?removeFromClient=true')
    const [, options] = fetchMock.mock.calls[0] as [RequestInfo, RequestInit]
    expect(options.method).toBe('DELETE')
  })
})
