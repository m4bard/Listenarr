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
import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import { sessionTokenManager } from '@/utils/sessionToken'

describe('sessionTokenManager storage propagation', () => {
  beforeEach(() => {
    // Ensure clean localStorage before each test
    try {
      localStorage.removeItem('listenarr_session_token')
    } catch {}
  })

  afterEach(() => {
    try {
      localStorage.removeItem('listenarr_session_token')
    } catch {}
  })

  it('notifies subscribers when storage is changed (cross-tab)', () => {
    const events: Array<string | null> = []
    const unsub = sessionTokenManager.onTokenChange((token) => {
      events.push(token)
    })

    // Simulate another tab writing to localStorage by dispatching a StorageEvent
    const token = 'abc123'
    // Manually set localStorage (emulating other tab)
    localStorage.setItem('listenarr_session_token', token)
    // Emit storage event
    const ev = new Event('storage')
    Object.defineProperty(ev, 'key', { value: 'listenarr_session_token' })
    Object.defineProperty(ev, 'newValue', { value: token })
    window.dispatchEvent(ev)

    expect(events.length).toBeGreaterThanOrEqual(1)
    expect(events[events.length - 1]).toBe(token)

    // Now simulate removal
    localStorage.removeItem('listenarr_session_token')
    const ev2 = new Event('storage')
    Object.defineProperty(ev2, 'key', { value: 'listenarr_session_token' })
    Object.defineProperty(ev2, 'newValue', { value: null })
    window.dispatchEvent(ev2)

    expect(events[events.length - 1]).toBe(null)

    unsub()
  })
})
