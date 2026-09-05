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
import { ref } from 'vue'
import { useRowSelection } from '@/composables/useRowSelection'

describe('useRowSelection', () => {
  it('reports only ids that are still selectable', () => {
    const selectable = ref([1, 2, 3])
    const selection = useRowSelection<number>(() => selectable.value)

    selection.toggleSelection(1)
    selection.toggleSelection(3)
    expect(selection.selectedIds.value).toEqual([1, 3])
    expect(selection.selectedCount.value).toBe(2)

    // Row 3 goes away underneath the user, the way a Wanted row does when its
    // download starts or a Downloads row does when it leaves the Active tab.
    selectable.value = [1, 2]

    expect(selection.selectedIds.value).toEqual([1])
    expect(selection.selectedCount.value).toBe(1)
    expect(selection.isSelected(3)).toBe(false)
  })

  it('honours the tick again if the row becomes selectable once more', () => {
    const selectable = ref([1, 2])
    const selection = useRowSelection<number>(() => selectable.value)

    selection.toggleSelection(2)
    selectable.value = [1]
    expect(selection.selectedCount.value).toBe(0)

    // A failed download puts a Wanted row back in play. The tick was the user's
    // intent and it stands, rather than being silently discarded while the row
    // was briefly unavailable.
    selectable.value = [1, 2]
    expect(selection.selectedIds.value).toEqual([2])
  })

  it('selectAll covers only what is selectable, and stays true as rows leave', () => {
    const selectable = ref(['a', 'b', 'c'])
    const selection = useRowSelection<string>(() => selectable.value)

    selection.selectAll()
    expect(selection.selectedCount.value).toBe(3)
    expect(selection.allSelected.value).toBe(true)

    selectable.value = ['a', 'b']
    expect(selection.selectedCount.value).toBe(2)
    expect(selection.allSelected.value).toBe(true)
  })

  it('a row that appears after selectAll is not selected, so allSelected goes false', () => {
    const selectable = ref(['a', 'b'])
    const selection = useRowSelection<string>(() => selectable.value)

    selection.selectAll()
    selectable.value = ['a', 'b', 'c']

    expect(selection.allSelected.value).toBe(false)
    expect(selection.selectedIds.value).toEqual(['a', 'b'])
  })

  it('allSelected is false when nothing is selectable, so a select-all cannot claim an empty set', () => {
    const selectable = ref<string[]>([])
    const selection = useRowSelection<string>(() => selectable.value)

    expect(selection.allSelected.value).toBe(false)

    selection.selectAll()
    expect(selection.selectedCount.value).toBe(0)
    expect(selection.allSelected.value).toBe(false)
  })

  it('toggleSelection removes a tick, and clearSelection drops every tick', () => {
    const selectable = ref([1, 2, 3])
    const selection = useRowSelection<number>(() => selectable.value)

    selection.toggleSelection(2)
    selection.toggleSelection(2)
    expect(selection.selectedCount.value).toBe(0)

    selection.selectAll()
    selection.clearSelection()
    expect(selection.selectedCount.value).toBe(0)
    expect(selection.tickedIds.value.size).toBe(0)
  })

  it('selectedIds follows the order the caller offers, not the order of ticking', () => {
    const selectable = ref([10, 20, 30])
    const selection = useRowSelection<number>(() => selectable.value)

    selection.toggleSelection(30)
    selection.toggleSelection(10)

    expect(selection.selectedIds.value).toEqual([10, 30])
  })

  it('a tick for an id that was never selectable never reaches selectedIds', () => {
    const selectable = ref([1, 2])
    const selection = useRowSelection<number>(() => selectable.value)

    selection.toggleSelection(99)

    expect(selection.selectedIds.value).toEqual([])
    expect(selection.selectedCount.value).toBe(0)
    expect(selection.isSelected(99)).toBe(false)
  })
})
