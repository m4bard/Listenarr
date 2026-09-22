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
import { formatBytes } from '@/utils/formatBytes'

describe('formatBytes', () => {
  it('formats zero explicitly', () => {
    expect(formatBytes(0)).toBe('0 B')
  })

  it('formats bytes below 1KB without a decimal', () => {
    expect(formatBytes(512)).toBe('512 B')
  })

  it('formats gigabytes with one decimal place', () => {
    expect(formatBytes(5_000_000_000)).toBe('4.7 GB')
  })

  it('formats terabytes', () => {
    expect(formatBytes(2 * 1024 * 1024 * 1024 * 1024)).toBe('2.0 TB')
  })

  it('returns an empty string for null or undefined, never a false zero', () => {
    // Distinct from formatBytes(0): a missing measurement must not read as "no space left".
    expect(formatBytes(null)).toBe('')
    expect(formatBytes(undefined)).toBe('')
  })

  it('returns an empty string for a negative value', () => {
    expect(formatBytes(-1)).toBe('')
  })
})
