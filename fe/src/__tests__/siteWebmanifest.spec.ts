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
import { describe, it, expect } from 'vitest'
import manifestSource from '../../public/site.webmanifest?raw'

/**
 * Vite copies public/ verbatim, so nothing rewrites the manifest at build time. Its URLs resolve
 * against the manifest's own URL, which the backend has already prefixed, so they must stay
 * relative. A root-absolute entry here would point outside the sub-path.
 */
describe('site.webmanifest', () => {
  const manifest = JSON.parse(manifestSource) as {
    icons: { src: string }[]
    start_url: string
    scope: string
  }

  it('declares icons relative to the manifest', () => {
    expect(manifest.icons.length).toBeGreaterThan(0)
    for (const icon of manifest.icons) {
      expect(icon.src.startsWith('./')).toBe(true)
    }
  })

  it('declares a relative scope and start URL', () => {
    expect(manifest.start_url).toBe('./')
    expect(manifest.scope).toBe('./')
  })
})
