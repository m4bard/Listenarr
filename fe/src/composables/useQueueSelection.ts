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
import { computed, ref } from 'vue'

/** What the selection needs from a row. The Activity page's QueueItem satisfies it. */
export interface SelectableRow {
  id: string
  canRemove?: boolean
}

const isSelectable = (row: SelectableRow): boolean => row.canRemove === true

/**
 * Selection state for the activity queue, keyed on the row id.
 *
 * The queue array is replaced wholesale every 30 seconds and on every SignalR update, so the
 * selection cannot live on the rows. Callers hand each fresh list to pruneSelection, which drops
 * the ids that have gone and leaves the rest selected. Readarr does the same thing under the name
 * removeOldSelectedState.
 */
export function useQueueSelection() {
  const selectedIds = ref<Set<string>>(new Set())

  const selectedCount = computed(() => selectedIds.value.size)

  const isSelected = (id: string): boolean => selectedIds.value.has(id)

  const toggleSelection = (id: string): void => {
    if (selectedIds.value.has(id)) {
      selectedIds.value.delete(id)
    } else {
      selectedIds.value.add(id)
    }
  }

  const selectAll = (rows: SelectableRow[]): void => {
    rows.filter(isSelectable).forEach((row) => selectedIds.value.add(row.id))
  }

  const clearSelection = (): void => {
    selectedIds.value.clear()
  }

  const setSelection = (ids: Iterable<string>): void => {
    selectedIds.value = new Set(ids)
  }

  const pruneSelection = (rows: SelectableRow[]): void => {
    const keep = new Set(rows.filter(isSelectable).map((row) => row.id))
    for (const id of Array.from(selectedIds.value)) {
      if (!keep.has(id)) selectedIds.value.delete(id)
    }
  }

  const selectedFrom = <T extends SelectableRow>(rows: T[]): T[] =>
    rows.filter((row) => selectedIds.value.has(row.id))

  return {
    selectedIds,
    selectedCount,
    isSelected,
    toggleSelection,
    selectAll,
    clearSelection,
    setSelection,
    pruneSelection,
    selectedFrom,
  }
}
