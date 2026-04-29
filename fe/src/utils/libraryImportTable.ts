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
export type LibraryImportSortKey = 'folder' | 'path' | 'format' | 'match'
export type LibraryImportSortDirection = 'asc' | 'desc'
export type LibraryImportResizableColumnKey = 'folder' | 'path' | 'format' | 'match'

export type LibraryImportColumnWidths = Record<LibraryImportResizableColumnKey, number>

export interface LibraryImportSortableItem {
  folderName: string
  detectedTitle?: string
  fullPath: string
  format: string
  fileCount: number
  selectedMatch: {
    title?: string | null
    authors?: Array<{ name?: string | null }>
  } | null
}

export const DEFAULT_LIBRARY_IMPORT_COLUMN_WIDTHS: LibraryImportColumnWidths = {
  folder: 260,
  path: 380,
  format: 120,
  match: 320,
}

export const LIBRARY_IMPORT_COLUMN_MIN_WIDTHS: LibraryImportColumnWidths = {
  folder: 220,
  path: 280,
  format: 96,
  match: 260,
}

const collator = new Intl.Collator(undefined, {
  numeric: true,
  sensitivity: 'base',
})

function compareText(a: string | null | undefined, b: string | null | undefined): number {
  const left = (a ?? '').trim()
  const right = (b ?? '').trim()
  return collator.compare(left, right)
}

function getBookSortValue(item: LibraryImportSortableItem): string {
  return (item.detectedTitle ?? '').trim() || item.folderName
}

function compareOptionalText(a: string | null | undefined, b: string | null | undefined): number {
  const left = (a ?? '').trim()
  const right = (b ?? '').trim()
  if (!left && !right) return 0
  if (!left) return 1
  if (!right) return -1
  return collator.compare(left, right)
}

function compareNumber(a: number, b: number): number {
  if (a === b) return 0
  return a > b ? 1 : -1
}

function compareMatch(a: LibraryImportSortableItem, b: LibraryImportSortableItem): number {
  const titleCompare = compareOptionalText(a.selectedMatch?.title, b.selectedMatch?.title)
  if (titleCompare !== 0) return titleCompare

  const authorCompare = compareOptionalText(
    a.selectedMatch?.authors?.[0]?.name,
    b.selectedMatch?.authors?.[0]?.name,
  )
  if (authorCompare !== 0) return authorCompare

  return compareText(a.fullPath, b.fullPath)
}

export function sortLibraryImportItems<T extends LibraryImportSortableItem>(
  items: readonly T[],
  sortKey: LibraryImportSortKey,
  sortDirection: LibraryImportSortDirection,
): T[] {
  const factor = sortDirection === 'asc' ? 1 : -1
  const copy = [...items]

  copy.sort((a, b) => {
    let result = 0

    switch (sortKey) {
      case 'folder':
        result = compareText(getBookSortValue(a), getBookSortValue(b))
        break
      case 'path':
        result = compareText(a.fullPath, b.fullPath)
        break
      case 'format':
        result = compareText(a.format, b.format)
        if (result === 0) result = compareNumber(a.fileCount, b.fileCount)
        break
      case 'match':
        result = compareMatch(a, b)
        break
    }

    if (result === 0) {
      return compareText(a.fullPath, b.fullPath)
    }

    return result * factor
  })

  return copy
}
