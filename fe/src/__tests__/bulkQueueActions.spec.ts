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
import { describe, it, expect, vi } from 'vitest'
import { runSequentially, summarizeBulk } from '@/components/domain/download/bulkQueueActions'
import { isImportBlocked } from '@/components/domain/download/queueStatus'

describe('isImportBlocked', () => {
  it('accepts both casings the app uses and rejects everything else', () => {
    expect(isImportBlocked('ImportBlocked')).toBe(true)
    expect(isImportBlocked('importblocked')).toBe(true)
    expect(isImportBlocked('Failed')).toBe(false)
    expect(isImportBlocked(undefined)).toBe(false)
  })
})

describe('runSequentially', () => {
  it('calls the action once per row, in list order', async () => {
    const seen: string[] = []
    const outcome = await runSequentially([{ id: 'a' }, { id: 'b' }, { id: 'c' }], async (row) => {
      seen.push(row.id)
    })
    expect(seen).toEqual(['a', 'b', 'c'])
    expect(outcome).toEqual({ succeeded: ['a', 'b', 'c'], failed: [] })
  })

  it('keeps going after a failure and reports which ids failed', async () => {
    const outcome = await runSequentially([{ id: 'a' }, { id: 'b' }, { id: 'c' }], async (row) => {
      if (row.id === 'b') throw new Error('nope')
    })
    expect(outcome).toEqual({ succeeded: ['a', 'c'], failed: ['b'] })
  })

  it('hands each failure to the caller', async () => {
    const onError = vi.fn()
    await runSequentially(
      [{ id: 'a' }],
      async () => {
        throw new Error('nope')
      },
      onError,
    )
    expect(onError).toHaveBeenCalledTimes(1)
    expect(onError.mock.calls[0][0]).toEqual({ id: 'a' })
    expect((onError.mock.calls[0][1] as Error).message).toBe('nope')
  })
})

describe('summarizeBulk', () => {
  it('summarizes a clean run', () => {
    expect(summarizeBulk('Removed', { succeeded: ['a', 'b'], failed: [] })).toBe('Removed 2 of 2.')
  })

  it('summarizes a partial run', () => {
    expect(summarizeBulk('Removed', { succeeded: ['a'], failed: ['b', 'c'] })).toBe(
      'Removed 1 of 3. 2 failed.',
    )
  })
})
