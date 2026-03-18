import { describe, it, expect, vi, afterEach } from 'vitest'

describe('ApiService advancedSearch', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('omits author when asin is provided', async () => {
    vi.resetModules()

    const fetchMock = vi.fn(() =>
      Promise.resolve(
        new Response(JSON.stringify({ results: [] }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    const actual = await vi.importActual<typeof import('@/services/api')>('@/services/api')
    await actual.apiService.advancedSearch({
      asin: 'B0DQR9D4YG',
      author: 'SenLinYu',
      cap: 5,
    })

    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [, options] = fetchMock.mock.calls[0] as [RequestInfo, RequestInit]
    const body = JSON.parse(String(options.body))

    expect(body).toEqual({
      mode: 'Advanced',
      asin: 'B0DQR9D4YG',
      cap: 5,
    })
    expect(body.author).toBeUndefined()
  })

  it('includes author when searching without asin', async () => {
    vi.resetModules()

    const fetchMock = vi.fn(() =>
      Promise.resolve(
        new Response(JSON.stringify({ results: [] }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    const actual = await vi.importActual<typeof import('@/services/api')>('@/services/api')
    await actual.apiService.advancedSearch({
      title: 'Alchemised',
      author: 'SenLinYu',
      cap: 5,
    })

    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [, options] = fetchMock.mock.calls[0] as [RequestInfo, RequestInit]
    const body = JSON.parse(String(options.body))

    expect(body).toEqual({
      mode: 'Advanced',
      title: 'Alchemised',
      author: 'SenLinYu',
      cap: 5,
    })
  })
})
