import { describe, it, expect, vi, afterEach } from 'vitest'

describe('ApiService removeFromLibrary', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('sends deleteFiles and deleteFolder query params when requested', async () => {
    vi.resetModules()

    const fetchMock = vi.fn(() =>
      Promise.resolve(
        new Response(JSON.stringify({ message: 'deleted', id: 42 }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      ),
    )
    vi.stubGlobal('fetch', fetchMock)

    const actual = await vi.importActual<typeof import('@/services/api')>('@/services/api')
    await actual.apiService.removeFromLibrary(42, { deleteFiles: true, deleteFolder: true })

    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [requestInfo, options] = fetchMock.mock.calls[0] as [RequestInfo, RequestInit]
    expect(String(requestInfo)).toContain('/library/42?deleteFiles=true&deleteFolder=true')
    expect(options.method).toBe('DELETE')
  })
})
