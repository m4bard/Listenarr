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

const jsonResponse = (body: unknown) =>
  Promise.resolve(
    new Response(JSON.stringify(body), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }),
  )

describe('ApiService queue management', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('clears completed downloads with a DELETE and reports the count', async () => {
    vi.resetModules()
    const fetchMock = vi.fn(() =>
      jsonResponse({ message: 'Completed downloads cleared', count: 3 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const actual = await vi.importActual<typeof import('@/services/api')>('@/services/api')
    const result = await actual.apiService.clearCompletedDownloads()

    const [requestInfo, options] = fetchMock.mock.calls[0] as [RequestInfo, RequestInit]
    expect(String(requestInfo)).toContain('/downloads/completed')
    expect(options.method).toBe('DELETE')
    expect(result.count).toBe(3)
  })

  it('clears failed downloads with a DELETE', async () => {
    vi.resetModules()
    const fetchMock = vi.fn(() => jsonResponse({ message: 'Failed downloads cleared', count: 1 }))
    vi.stubGlobal('fetch', fetchMock)

    const actual = await vi.importActual<typeof import('@/services/api')>('@/services/api')
    await actual.apiService.clearFailedDownloads()

    const [requestInfo, options] = fetchMock.mock.calls[0] as [RequestInfo, RequestInit]
    expect(String(requestInfo)).toContain('/downloads/failed')
    expect(options.method).toBe('DELETE')
  })

  it('posts a retry for one blocked import', async () => {
    vi.resetModules()
    const fetchMock = vi.fn(() =>
      jsonResponse({ message: 'Import retry queued', id: 'd1', status: 'ImportPending' }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const actual = await vi.importActual<typeof import('@/services/api')>('@/services/api')
    const result = await actual.apiService.retryBlockedImport('d1')

    const [requestInfo, options] = fetchMock.mock.calls[0] as [RequestInfo, RequestInit]
    expect(String(requestInfo)).toContain('/downloads/d1/retry-import')
    expect(options.method).toBe('POST')
    expect(result.status).toBe('ImportPending')
  })
})
