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
import { setActivePinia, createPinia } from 'pinia'
import { useLibraryStore } from '@/stores/library'
import { describe, test, expect, beforeEach, vi } from 'vitest'
import { signalRService } from '@/services/signalr'
import type { Audiobook } from '@/types'

describe('AudiobookUpdate SignalR merge', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  test('merges server-provided audiobook DTO into store item', async () => {
    const callbacks: Array<(a: Audiobook) => void> = []
    const spy = vi
      .spyOn(signalRService, 'onAudiobookUpdate')
      .mockImplementation((cb?: (...args: unknown[]) => void) => {
        if (cb) callbacks.push(cb as (a: Audiobook) => void)
        return () => {}
      })

    const store = useLibraryStore()
    store.audiobooks = [
      {
        id: 1,
        title: 'Local',
        basePath: '/old/path',
        files: [{ id: 10, path: '/old/path/file.m4b' }],
      },
    ] as Audiobook[]

    // Simulate server pushing a DTO with new basePath and files
    const serverDto: Partial<Audiobook> = {
      id: 1,
      title: 'Local (server)',
      basePath: '/new/path',
      files: [{ id: 11, path: '/new/path/file.m4b' }],
    }

    // Call the registered callback
    expect(callbacks.length).toBeGreaterThan(0)
    callbacks[0](serverDto as Audiobook)

    // Assert store was merged correctly
    const merged = store.audiobooks.find((b) => b.id === 1) as Audiobook
    expect(merged).toBeDefined()
    expect(merged.title).toBe('Local (server)')
    expect(merged.basePath).toBe('/new/path')
    // Files replaced since server provided non-empty array
    expect(merged.files).toHaveLength(1)
    expect(merged.files![0].path).toBe('/new/path/file.m4b')

    spy.mockRestore()
  })
})
