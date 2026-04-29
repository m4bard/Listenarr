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
export interface LibraryImportSearchCandidate {
  fullPath: string
  folderName: string
  detectedTitle?: string
  detectedAuthor?: string
  detectedAsin?: string
}

export function buildLibraryImportFallbackTitle(
  item: Pick<LibraryImportSearchCandidate, 'fullPath' | 'folderName'>,
): string {
  const filenameStem =
    item.fullPath
      .replace(/\\/g, '/')
      .split('/')
      .pop()
      ?.replace(/\.[^.]+$/, '') ?? ''
  const numericMatch = /\((\d+)\)\s*$/.exec(filenameStem)
  const stemBase = filenameStem.replace(/\s*\(\d+\)\s*$/, '').trim()
  const base =
    stemBase && stemBase.toLowerCase() !== item.folderName.toLowerCase()
      ? filenameStem
      : item.folderName

  if (numericMatch) {
    return `${base.replace(/\s*\(\d+\)\s*$/, '').trim()} ${numericMatch[1]}`
  }

  return base
}

export function buildLibraryImportInitialQuery(item: LibraryImportSearchCandidate): string {
  const asin = item.detectedAsin?.trim()
  if (asin) return asin

  const detectedTitle = item.detectedTitle?.trim()
  if (detectedTitle) return detectedTitle

  return buildLibraryImportFallbackTitle(item)
}

export function buildLibraryImportInitialAuthor(item: LibraryImportSearchCandidate): string {
  if (item.detectedAsin?.trim()) return ''
  return item.detectedAuthor?.trim() ?? ''
}

export function buildLibraryImportSearchParams(
  item: LibraryImportSearchCandidate,
  cap: number,
): {
  asin?: string
  title?: string
  author?: string
  cap: number
} {
  const asin = item.detectedAsin?.trim()
  if (asin) {
    return { asin, cap }
  }

  const title = buildLibraryImportInitialQuery(item)
  const author = buildLibraryImportInitialAuthor(item) || undefined

  return {
    title,
    ...(author ? { author } : {}),
    cap,
  }
}
