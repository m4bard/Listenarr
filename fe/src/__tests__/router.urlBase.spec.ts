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
import { createPinia, setActivePinia } from 'pinia'

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  return { ...actual, createWebHistory: vi.fn(actual.createWebHistory) }
})

const loadRouterFactory = async (urlBase?: string) => {
  vi.resetModules()
  if (urlBase === undefined) {
    delete (window as unknown as Record<string, unknown>).__listenarrUrlBase
  } else {
    ;(window as unknown as Record<string, unknown>).__listenarrUrlBase = urlBase
  }

  const { createWebHistory } = await import('vue-router')
  vi.mocked(createWebHistory).mockClear()
  const { createAppRouter } = await import('@/router')
  return { createAppRouter, createWebHistory: vi.mocked(createWebHistory) }
}

describe('router history base', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  afterEach(() => {
    delete (window as unknown as Record<string, unknown>).__listenarrUrlBase
    vi.resetModules()
  })

  it('uses the site root when nothing was injected', async () => {
    const { createAppRouter, createWebHistory } = await loadRouterFactory()

    createAppRouter()

    // Explicitly '/', never undefined: vue-router falls back to <base href> for a falsy base and
    // this build deliberately emits no such tag.
    expect(createWebHistory).toHaveBeenCalledWith('/')
  })

  it('uses the injected sub-path so route URLs keep the proxy prefix', async () => {
    const { createAppRouter, createWebHistory } = await loadRouterFactory('/example')

    const router = createAppRouter()

    expect(createWebHistory).toHaveBeenCalledWith('/example')
    expect(router.resolve({ name: 'login' }).href).toBe('/example/login')
  })
})
