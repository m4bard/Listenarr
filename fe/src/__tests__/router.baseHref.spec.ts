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
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const withBaseElement = async (href: string | null) => {
  document.querySelectorAll('base').forEach((element) => element.remove())
  if (href !== null) {
    const base = document.createElement('base')
    base.setAttribute('href', href)
    document.head.prepend(base)
  }

  vi.resetModules()
  setActivePinia(createPinia())
  const { createAppRouter } = await import('@/router')
  return createAppRouter()
}

beforeEach(() => {
  setActivePinia(createPinia())
})

afterEach(() => {
  document.querySelectorAll('base').forEach((element) => element.remove())
  vi.resetModules()
})

describe('router history base', () => {
  it('takes the sub-path from the base href the server injected', async () => {
    const router = await withBaseElement('/example/')

    expect(router.options.history.base).toBe('/example')
    expect(router.resolve({ name: 'audiobooks' }).href).toBe('/example/audiobooks')
  })

  it('stays at the site root when the injected base is the site root', async () => {
    const router = await withBaseElement('/')

    expect(router.options.history.base).toBe('')
    expect(router.resolve({ name: 'audiobooks' }).href).toBe('/audiobooks')
  })

  it('strips the origin from an absolute base href', async () => {
    const router = await withBaseElement('http://listenarr.example.com/example/')

    expect(router.options.history.base).toBe('/example')
  })

  it('never produces the relative build base as a history base', async () => {
    // import.meta.env.BASE_URL is the literal './' once the app is built with a relative base.
    // Passing it to createWebHistory would yield '/.' and break every generated link.
    const router = await withBaseElement('/example/')

    expect(router.options.history.base).not.toContain('.')
  })
})
