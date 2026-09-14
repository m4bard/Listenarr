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

describe('ApiService testNotification', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('always posts to the diagnostics test-notification endpoint, never the legacy /notifications/test route', async () => {
    vi.resetModules()

    const fetchMock = vi.fn(() =>
      Promise.resolve(
        new Response(JSON.stringify({ success: true, message: 'Test notification sent' }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    const actual = await vi.importActual<typeof import('@/services/api')>('@/services/api')
    const result = await actual.apiService.testNotification(
      'book-available',
      { message: 'Test notification from Listenarr UI' },
      'webhook-1',
      'https://hooks.slack.com/services/xyz',
    )

    expect(result.success).toBe(true)
    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [requestInfo, options] = fetchMock.mock.calls[0] as [RequestInfo, RequestInit]
    expect(String(requestInfo)).toContain('/diagnostics/test-notification')
    expect(String(requestInfo)).not.toContain('/notifications/test')
    const body = JSON.parse(String(options.body))
    expect(body).toEqual({
      trigger: 'book-available',
      data: { message: 'Test notification from Listenarr UI' },
      webhookId: 'webhook-1',
      webhookUrl: 'https://hooks.slack.com/services/xyz',
    })
  })
})
