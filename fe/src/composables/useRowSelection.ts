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
import { computed, ref, type ComputedRef, type Ref } from 'vue'

/**
 * Row selection for a list whose contents change underneath the user.
 *
 * The library store already holds this shape for the library grid: a Set of ids
 * plus toggle / selectAll / clear / isSelected. This is the same shape, with one
 * addition that the library grid does not need and these pages do.
 *
 * The addition is `selectedIds`, which is always the intersection of what has
 * been ticked with what the caller currently offers as selectable. Wanted and
 * Downloads both re-render from live data: a Wanted row starts downloading, a
 * Download leaves the Active tab when it finishes. A raw Set of ticked ids goes
 * stale in both cases, and a bulk action reading it would act on rows that are
 * no longer on screen. Deriving the acted-on set rather than pruning it on an
 * event means there is no event to miss.
 *
 * `selectedCount` counts the same intersection, so a button label can never
 * promise more rows than the action will touch.
 */
export interface RowSelection<Id> {
  /** Ids ticked and still selectable, in the caller's current order. */
  selectedIds: ComputedRef<Id[]>
  /** Size of `selectedIds`. */
  selectedCount: ComputedRef<number>
  /** True when the id is ticked and still selectable. */
  isSelected: (id: Id) => boolean
  /** Tick an unticked id, untick a ticked one. */
  toggleSelection: (id: Id) => void
  /** Tick every currently selectable id. */
  selectAll: () => void
  /** Untick everything. */
  clearSelection: () => void
  /** True when every selectable id is ticked and there is at least one. */
  allSelected: ComputedRef<boolean>
  /** Raw ticked ids, including any no longer selectable. Exposed for tests. */
  tickedIds: Ref<Set<Id>>
}

/**
 * @param selectableIds Reactive source of the ids the user may act on right now.
 */
export function useRowSelection<Id>(selectableIds: () => readonly Id[]): RowSelection<Id> {
  const tickedIds = ref(new Set<Id>()) as Ref<Set<Id>>

  const selectedIds = computed(() => selectableIds().filter((id) => tickedIds.value.has(id)))

  const selectedCount = computed(() => selectedIds.value.length)

  // Membership lookups happen once per rendered row, so back them with a Set
  // rather than scanning the selectable list for each one.
  const selectedIdSet = computed(() => new Set(selectedIds.value))

  const allSelected = computed(() => {
    const selectable = selectableIds()
    return selectable.length > 0 && selectedCount.value === selectable.length
  })

  function isSelected(id: Id): boolean {
    return selectedIdSet.value.has(id)
  }

  function toggleSelection(id: Id) {
    const next = new Set(tickedIds.value)
    if (next.has(id)) {
      next.delete(id)
    } else {
      next.add(id)
    }
    tickedIds.value = next
  }

  function selectAll() {
    const next = new Set(tickedIds.value)
    selectableIds().forEach((id) => next.add(id))
    tickedIds.value = next
  }

  function clearSelection() {
    tickedIds.value = new Set<Id>()
  }

  return {
    selectedIds,
    selectedCount,
    isSelected,
    toggleSelection,
    selectAll,
    clearSelection,
    allSelected,
    tickedIds,
  }
}
