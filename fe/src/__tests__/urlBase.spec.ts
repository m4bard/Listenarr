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
import { describe, it, expect, afterEach } from 'vitest'
import { getUrlBase, withUrlBase } from '@/utils/urlBase'
import { getPlaceholderUrl } from '@/utils/placeholder'

const setInjectedUrlBase = (value: unknown) => {
  ;(window as unknown as Record<string, unknown>).__listenarrUrlBase = value
}

describe('urlBase', () => {
  afterEach(() => {
    delete (window as unknown as Record<string, unknown>).__listenarrUrlBase
  })

  it('is empty when the backend injected nothing', () => {
    expect(getUrlBase()).toBe('')
    expect(withUrlBase('/api')).toBe('/api')
  })

  it.each([
    ['/example', '/example'],
    ['/example/', '/example'],
    ['/example///', '/example'],
    ['example', '/example'],
    ['  /example/audiobooks  ', '/example/audiobooks'],
    ['/', ''],
    ['', ''],
    ['   ', ''],
  ])('normalizes %j to %j', (injected, expected) => {
    setInjectedUrlBase(injected)
    expect(getUrlBase()).toBe(expected)
  })

  it.each([[42], [null], [{}], [['/example']]])(
    'ignores a non-string value (%j) rather than building a broken URL',
    (injected) => {
      setInjectedUrlBase(injected)
      expect(getUrlBase()).toBe('')
    },
  )

  it('prefixes root-absolute paths and tolerates a missing leading slash', () => {
    setInjectedUrlBase('/example')
    expect(withUrlBase('/hubs/logs')).toBe('/example/hubs/logs')
    expect(withUrlBase('hubs/logs')).toBe('/example/hubs/logs')
  })
})

describe('getPlaceholderUrl', () => {
  afterEach(() => {
    delete (window as unknown as Record<string, unknown>).__listenarrUrlBase
  })

  it('stays root-absolute at the site root', () => {
    expect(getPlaceholderUrl()).toBe('/placeholder.svg')
  })

  it('carries the sub-path, so a deep link does not resolve it against the current route', () => {
    setInjectedUrlBase('/example')
    expect(getPlaceholderUrl()).toBe('/example/placeholder.svg')
  })
})
