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
import { useQueueSelection } from '@/composables/useQueueSelection'

const row = (id: string, canRemove = true) => ({ id, canRemove })

describe('useQueueSelection', () => {
  it('toggles a row on and then off', () => {
    const selection = useQueueSelection()
    selection.toggleSelection('a')
    expect(selection.isSelected('a')).toBe(true)
    expect(selection.selectedCount.value).toBe(1)
    selection.toggleSelection('a')
    expect(selection.isSelected('a')).toBe(false)
    expect(selection.selectedCount.value).toBe(0)
  })

  it('selects every row it is given and skips the ones that cannot be removed', () => {
    const selection = useQueueSelection()
    selection.selectAll([row('a'), row('b', false), row('c')])
    expect(Array.from(selection.selectedIds.value).sort()).toEqual(['a', 'c'])
  })

  it('clears the whole selection', () => {
    const selection = useQueueSelection()
    selection.selectAll([row('a'), row('b')])
    selection.clearSelection()
    expect(selection.selectedCount.value).toBe(0)
  })

  it('deselects only the rows it is given and keeps the rest', () => {
    const selection = useQueueSelection()
    selection.selectAll([row('a'), row('b'), row('c')])
    selection.deselectAll([row('a'), row('c')])
    expect(Array.from(selection.selectedIds.value)).toEqual(['b'])
  })

  it('keeps a selection across a refresh and drops the ids that went away', () => {
    const selection = useQueueSelection()
    selection.selectAll([row('a'), row('b'), row('c')])
    selection.pruneSelection([row('a'), row('c')])
    expect(Array.from(selection.selectedIds.value).sort()).toEqual(['a', 'c'])
  })

  it('prunes a row that is still listed but can no longer be removed', () => {
    const selection = useQueueSelection()
    selection.selectAll([row('a'), row('b')])
    selection.pruneSelection([row('a'), row('b', false)])
    expect(Array.from(selection.selectedIds.value)).toEqual(['a'])
  })

  it('reads the selected rows back in list order', () => {
    const selection = useQueueSelection()
    selection.setSelection(['c', 'a'])
    expect(selection.selectedFrom([row('a'), row('b'), row('c')]).map((r) => r.id)).toEqual([
      'a',
      'c',
    ])
  })
})
