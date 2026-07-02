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
/**
 * Small path utility helpers used across UI components.
 * Keep these functions small and dependency-free so they are easy to reason about
 * and simple to unit test if needed.
 */

export type PathKind = 'windows' | 'unix' | 'unknown'

export interface DestinationPathValidationOptions {
  pathKind?: PathKind
  sourcePath?: string | null
}

const WINDOWS_RESERVED_DEVICE_PATTERN = /^(con|prn|aux|nul|com[1-9]|lpt[1-9])$/i

export function toForward(s: string | null | undefined): string {
  return (s || '').replace(/\\/g, '/')
}

export function trimTrailingSlash(s: string): string {
  let out = s
  while (out.endsWith('/') || out.endsWith('\\')) out = out.slice(0, -1)
  return out
}

export function detectPathKind(s: string | null | undefined): PathKind {
  const value = s || ''
  if (/^[a-zA-Z]:([\\/]|$)/.test(value)) return 'windows'
  if (/^[\\/]{2}[^\\/]+[\\/][^\\/]+/.test(value) && value.includes('\\')) return 'windows'
  if (value.includes('\\')) return 'windows'
  if (value.startsWith('/')) return 'unix'
  return 'unknown'
}

export function isWindowsShapedPath(s: string | null | undefined): boolean {
  return detectPathKind(s) === 'windows'
}

export function splitPathSegments(
  s: string | null | undefined,
  pathKind: PathKind = 'unknown',
): string[] {
  const value = s || ''
  const kind = pathKind === 'unknown' ? detectPathKind(value) : pathKind
  if (kind === 'windows') return value.replace(/\\/g, '/').split('/')
  return value.split('/')
}

export function normalizeForCompare(
  s: string | null | undefined,
  pathKind: PathKind = 'unknown',
): string {
  const value = trimTrailingSlash(s || '')
  const kind = pathKind === 'unknown' ? detectPathKind(value) : pathKind
  const normalized = kind === 'windows' ? value.replace(/\\/g, '/') : value
  return kind === 'windows' ? normalized.toLowerCase() : normalized
}

export function pathsEqual(
  first: string | null | undefined,
  second: string | null | undefined,
  pathKind: PathKind = 'unknown',
): boolean {
  if (!first || !second) return false

  const kind = pathKind === 'unknown' ? detectPathKind(first || second) : pathKind
  return normalizeForCompare(first, kind) === normalizeForCompare(second, kind)
}

export function pathIsInside(
  candidate: string | null | undefined,
  root: string | null | undefined,
  pathKind: PathKind = 'unknown',
): boolean {
  if (!candidate || !root) return false

  const kind = pathKind === 'unknown' ? detectPathKind(candidate || root) : pathKind
  const normalizedCandidate = normalizeForCompare(candidate, kind)
  const normalizedRoot = normalizeForCompare(root, kind)
  if (!normalizedCandidate || !normalizedRoot || normalizedCandidate === normalizedRoot)
    return false

  const rootWithSeparator = normalizedRoot.endsWith('/') ? normalizedRoot : `${normalizedRoot}/`
  return normalizedCandidate.startsWith(rootWithSeparator)
}

export function pathsOverlap(
  first: string | null | undefined,
  second: string | null | undefined,
  pathKind: PathKind = 'unknown',
): boolean {
  return (
    pathsEqual(first, second, pathKind) ||
    pathIsInside(first, second, pathKind) ||
    pathIsInside(second, first, pathKind)
  )
}

export function isAbsolutePath(s: string): boolean {
  return /^([a-zA-Z]:[\\/]|[\\/])/.test(s)
}

export function hasRelativePathSegment(
  s: string | null | undefined,
  pathKind: PathKind = 'unknown',
): boolean {
  return splitPathSegments(s, pathKind).some((segment) => segment === '.' || segment === '..')
}

export function hasParentTraversalSegment(
  s: string | null | undefined,
  pathKind: PathKind = 'unknown',
): boolean {
  return splitPathSegments(s, pathKind).some((segment) => segment === '..')
}

export function hasEmptyMiddlePathSegment(
  s: string | null | undefined,
  pathKind: PathKind = 'unknown',
): boolean {
  const value = s || ''
  const kind = pathKind === 'unknown' ? detectPathKind(value) : pathKind
  const normalized = trimTrailingSlash(kind === 'windows' ? value.replace(/\\/g, '/') : value)
  if (!normalized) return false

  if (kind === 'windows' && normalized.startsWith('//')) {
    return normalized
      .slice(2)
      .split('/')
      .some((segment) => segment === '')
  }

  const segments = normalized.split('/')
  const startIndex = normalized.startsWith('/') ? 1 : 0
  return segments.slice(startIndex).some((segment) => segment === '')
}

export function hasControlCharacter(s: string | null | undefined): boolean {
  return /[\u0000-\u001f\u007f]/.test(s || '')
}

export function hasOuterWhitespace(s: string | null | undefined): boolean {
  const value = s || ''
  return value.length > 0 && value !== value.trim()
}

export function hasPathSegmentOuterWhitespace(
  s: string | null | undefined,
  pathKind: PathKind = 'unknown',
): boolean {
  return splitPathSegments(s, pathKind)
    .filter((segment) => segment.length > 0)
    .some((segment) => segment !== segment.trim())
}

export function hasWindowsTrailingSpaceOrPeriodSegment(
  s: string | null | undefined,
  pathKind: PathKind = 'unknown',
): boolean {
  const value = s || ''
  const kind = pathKind === 'unknown' ? detectPathKind(value) : pathKind
  if (kind !== 'windows') return false

  return splitPathSegments(trimTrailingSlash(value), 'windows')
    .filter((segment) => segment.length > 0)
    .some((segment) => /[ .]$/.test(segment))
}

export function hasWindowsInvalidCharacter(
  s: string | null | undefined,
  pathKind: PathKind = 'unknown',
): boolean {
  const value = s || ''
  const kind = pathKind === 'unknown' ? detectPathKind(value) : pathKind
  if (kind !== 'windows') return false
  if (/[<>"|?*]/.test(value)) return true

  const withoutDriveColon = /^[a-zA-Z]:/.test(value) ? value.slice(2) : value
  return withoutDriveColon.includes(':')
}

export function hasWindowsReservedDeviceSegment(
  s: string | null | undefined,
  pathKind: PathKind = 'unknown',
): boolean {
  const value = s || ''
  const kind = pathKind === 'unknown' ? detectPathKind(value) : pathKind
  if (kind !== 'windows') return false

  const normalized = trimTrailingSlash(value.replace(/\\/g, '/'))
  let segments = normalized.split('/').filter((segment) => segment.length > 0)

  if (/^[a-zA-Z]:$/.test(segments[0] || '')) {
    segments = segments.slice(1)
  } else if (normalized.startsWith('//')) {
    // Skip UNC server and share components; validate the destination folders below the share.
    segments = segments.slice(2)
  }

  return segments.some((segment) => {
    const baseName = segment.trimEnd().split('.')[0]
    return WINDOWS_RESERVED_DEVICE_PATTERN.test(baseName)
  })
}

export function validateLibraryDestinationPath(
  s: string | null | undefined,
  options: DestinationPathValidationOptions = {},
): string | null {
  if (!s) return null

  const pathKind =
    options.pathKind === 'unknown' || !options.pathKind ? detectPathKind(s) : options.pathKind

  if (hasControlCharacter(s)) {
    return 'Destination folder cannot contain control characters.'
  }

  if (hasRelativePathSegment(s, pathKind)) {
    return 'Path traversal is not allowed in the destination folder. Remove relative path segments and choose the actual target folder instead.'
  }

  if (hasEmptyMiddlePathSegment(s, pathKind)) {
    return 'Destination folder cannot contain empty path segments. Remove repeated path separators.'
  }

  if (hasWindowsTrailingSpaceOrPeriodSegment(s, pathKind)) {
    return 'Windows destination folder segments cannot end with a space or period.'
  }

  if (hasWindowsInvalidCharacter(s, pathKind)) {
    return 'Destination folder contains characters that are invalid on Windows.'
  }

  if (hasWindowsReservedDeviceSegment(s, pathKind)) {
    return 'Destination folder contains a reserved Windows device name.'
  }

  if (options.sourcePath && pathsEqual(s, options.sourcePath, pathKind)) {
    return 'Destination folder must be different from the current source folder.'
  }

  return null
}

/**
 * If `value` contains the configured `root` prefix, remove it and return the
 * relative portion (respecting backslash style). Returns null if no match.
 */
export function stripRootPrefix(root: string, value: string): string | null {
  if (!root || !value) return null
  try {
    const rootKind = detectPathKind(root)
    const nroot = normalizeForCompare(root, rootKind)
    const nval = normalizeForCompare(value, rootKind)

    if (nval.includes(nroot)) {
      const idx = nval.indexOf(nroot)
      const normalizedValue = rootKind === 'windows' ? value.replace(/\\/g, '/') : value
      const rel = normalizedValue.slice(idx + nroot.length).replace(/^\/+/, '')
      const useBackslash = rootKind === 'windows' && root.includes('\\')
      return useBackslash ? rel.replace(/\//g, '\\') : rel
    }

    // fallback: try matching two-segment windows from the end toward the start
    const segs = splitPathSegments(nroot, rootKind)
    for (let i = Math.max(0, segs.length - 2); i >= 0; i--) {
      const two = segs.slice(i, i + 2).join('/')
      if (two && nval.includes(two)) {
        const idx = nval.indexOf(two)
        const normalizedValue = rootKind === 'windows' ? value.replace(/\\/g, '/') : value
        const rel = normalizedValue.slice(idx + two.length).replace(/^\/+/, '')
        const useBackslash = rootKind === 'windows' && root.includes('\\')
        return useBackslash ? rel.replace(/\//g, '\\') : rel
      }
    }
  } catch {
    // noop - fall through to null
  }

  return null
}

export function joinPaths(
  root: string | null | undefined,
  relative: string | null | undefined,
): string {
  if (!root) return relative || ''
  const rootKind = detectPathKind(root)
  const useBackslash = rootKind === 'windows' && root.includes('\\')
  const normalizedRoot = rootKind === 'windows' ? root.replace(/\\/g, '/') : root
  const r = trimTrailingSlash(normalizedRoot)
  const rel = (relative || '').toString().replace(/^\/+/, '')
  const combined = rel ? `${r}/${rel}` : r
  return useBackslash ? combined.replace(/\//g, '\\') : combined
}
