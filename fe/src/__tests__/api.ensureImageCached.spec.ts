import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { API_BASE_PATH } from '@/services/apiBase'

// Ensure we use the actual implementation (test-setup globally mocks /services/api)
vi.unmock('../services/api')
import { apiService as svc } from '../services/api'

type FetchCall = [RequestInfo | URL, RequestInit?]
type FetchLikeMock = { mock: { calls: FetchCall[] } }
type ApiServiceWithCache = typeof svc & {
  metadataUrlCache: Map<string, { urls: string[]; fetchedAt: number }>
}

describe('ApiService.ensureImageCached', () => {
  const imageBasePath = `${API_BASE_PATH}/images`

  beforeEach(() => {
    vi.restoreAllMocks()
    ;(svc as ApiServiceWithCache).metadataUrlCache.clear()
  })

  afterEach(() => {
    vi.resetAllMocks()
  })

  it('uses cached candidate URLs to trigger backend cache fetches', async () => {
    ;(svc as ApiServiceWithCache).metadataUrlCache.set('ASIN000001', {
      urls: ['https://audimeta.covers/cover1.jpg'],
      fetchedAt: Date.now(),
    })

    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const s = String(input)
      if (s.includes(`${imageBasePath}/ASIN000001?url=`) && s.includes('audimeta.covers')) {
        return { ok: true, status: 200 }
      }
      return { ok: false, status: 404 }
    }))

    const ok = await svc.ensureImageCached(`${imageBasePath}/ASIN000001`)

    expect(ok).toBe(true)
    const fetchCalls = (globalThis.fetch as unknown as FetchLikeMock).mock.calls
    expect(fetchCalls.some((c) => String(c[0]).includes(`${imageBasePath}/ASIN000001?url=`))).toBe(true)
  })

  it('falls back to base image endpoint when no candidate URLs are cached', async () => {
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const s = String(input)
      if (s.endsWith(`${imageBasePath}/ASIN000002`)) {
        return { ok: true, status: 200 }
      }
      return { ok: false, status: 404 }
    }))

    const ok = await svc.ensureImageCached(`${imageBasePath}/ASIN000002`)

    expect(ok).toBe(true)
    const fetchCalls = (globalThis.fetch as unknown as FetchLikeMock).mock.calls
    expect(fetchCalls.some((c) => String(c[0]).endsWith(`${imageBasePath}/ASIN000002`))).toBe(true)
  })

  it('returns false when candidate and base endpoints both fail', async () => {
    ;(svc as ApiServiceWithCache).metadataUrlCache.set('ASIN000003', {
      urls: ['https://cached.example/cover3.jpg'],
      fetchedAt: Date.now(),
    })

    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const s = String(input)
      if (s.includes(`${imageBasePath}/ASIN000003?url=`)) {
        return { ok: false, status: 404 }
      }
      if (s.endsWith(`${imageBasePath}/ASIN000003`)) {
        return { ok: false, status: 404 }
      }
      return { ok: false, status: 404 }
    }))

    const ok = await svc.ensureImageCached(`${imageBasePath}/ASIN000003`)

    expect(ok).toBe(false)
    const fetchCalls = (globalThis.fetch as unknown as FetchLikeMock).mock.calls
    expect(fetchCalls.some((c) => String(c[0]).includes(`${imageBasePath}/ASIN000003?url=`))).toBe(true)
    expect(fetchCalls.some((c) => String(c[0]).endsWith(`${imageBasePath}/ASIN000003`))).toBe(true)
  })
})
