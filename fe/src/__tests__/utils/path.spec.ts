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
import {
  toForward,
  trimTrailingSlash,
  normalizeForCompare,
  isAbsolutePath,
  hasRelativePathSegment,
  hasParentTraversalSegment,
  hasEmptyMiddlePathSegment,
  hasControlCharacter,
  hasOuterWhitespace,
  hasPathSegmentOuterWhitespace,
  hasWindowsTrailingSpaceOrPeriodSegment,
  hasWindowsInvalidCharacter,
  pathsOverlap,
  hasWindowsReservedDeviceSegment,
  validateLibraryDestinationPath,
  stripRootPrefix,
} from '@/utils/path'

describe('path utils', () => {
  it('toForward converts backslashes to forward', () => {
    expect(toForward('C:\\temp\\dir')).toBe('C:/temp/dir')
    expect(toForward(null)).toBe('')
  })

  it('trimTrailingSlash removes trailing slashes', () => {
    expect(trimTrailingSlash('C:/path/')).toBe('C:/path')
    expect(trimTrailingSlash('C:\\path\\')).toBe('C:\\path')
    expect(trimTrailingSlash('no-slash')).toBe('no-slash')
  })

  it('normalizeForCompare lowercases and trims', () => {
    expect(normalizeForCompare('C:\\Temp\\Dir\\')).toBe('c:/temp/dir')
  })

  it('isAbsolutePath detects absolute paths', () => {
    expect(isAbsolutePath('C:\\some\\path')).toBe(true)
    expect(isAbsolutePath('/unix/path')).toBe(true)
    expect(isAbsolutePath('relative/path')).toBe(false)
  })

  it('detects exact relative path segments without blocking periods in names', () => {
    expect(hasRelativePathSegment('D:\\Books\\Title\\.')).toBe(true)
    expect(hasRelativePathSegment('D:\\Books\\Title\\..')).toBe(true)
    expect(hasRelativePathSegment('/books/./title')).toBe(true)
    expect(hasRelativePathSegment('/books/../title')).toBe(true)
    expect(hasRelativePathSegment('/books/Dr. Seuss')).toBe(false)
    expect(hasRelativePathSegment('/books/.metadata')).toBe(false)
    expect(hasRelativePathSegment('/books/title...')).toBe(false)
  })

  it('hasParentTraversalSegment detects parent directory traversal', () => {
    expect(hasParentTraversalSegment('D:\\Books\\Title\\..')).toBe(true)
    expect(hasParentTraversalSegment('/books/title/../other')).toBe(true)
    expect(hasParentTraversalSegment('/books/title..')).toBe(false)
    expect(hasParentTraversalSegment('/books/.../title')).toBe(false)
    expect(hasParentTraversalSegment(null)).toBe(false)
  })

  it('detects empty middle path segments without rejecting roots', () => {
    expect(hasEmptyMiddlePathSegment('D:\\Books\\\\Title')).toBe(true)
    expect(hasEmptyMiddlePathSegment('/books//title')).toBe(true)
    expect(hasEmptyMiddlePathSegment('D:\\Books\\Title')).toBe(false)
    expect(hasEmptyMiddlePathSegment('/books/title')).toBe(false)
    expect(hasEmptyMiddlePathSegment('D:\\')).toBe(false)
    expect(hasEmptyMiddlePathSegment('\\\\server\\share\\Audiobooks')).toBe(false)
    expect(hasEmptyMiddlePathSegment('\\\\server\\share\\\\Audiobooks')).toBe(true)
  })

  it('detects control characters and segment whitespace', () => {
    expect(hasControlCharacter('D:\\Books\\Title\n')).toBe(true)
    expect(hasControlCharacter('D:\\Books\\Title')).toBe(false)
    expect(hasOuterWhitespace(' D:\\Books\\Title')).toBe(true)
    expect(hasOuterWhitespace('D:\\Books\\Title ')).toBe(true)
    expect(hasOuterWhitespace('D:\\Listenarr Test\\Title')).toBe(false)
    expect(hasPathSegmentOuterWhitespace('D:\\Books\\test ')).toBe(true)
    expect(hasPathSegmentOuterWhitespace('D:\\Books\\ test')).toBe(true)
    expect(hasPathSegmentOuterWhitespace('D:\\Listenarr Test\\Title')).toBe(false)
  })

  it('detects Windows-only trailing space or period segments', () => {
    expect(hasWindowsTrailingSpaceOrPeriodSegment('D:\\Books\\test ')).toBe(true)
    expect(hasWindowsTrailingSpaceOrPeriodSegment('D:\\Books\\test.')).toBe(true)
    expect(hasWindowsTrailingSpaceOrPeriodSegment('D:\\Books\\ test')).toBe(false)
    expect(hasWindowsTrailingSpaceOrPeriodSegment('/books/test ')).toBe(false)
    expect(hasWindowsTrailingSpaceOrPeriodSegment('/books/ test ')).toBe(false)
  })

  it('detects Windows invalid characters and reserved device names', () => {
    expect(hasWindowsInvalidCharacter('D:\\Books\\Bad|Folder')).toBe(true)
    expect(hasWindowsInvalidCharacter('D:\\Books\\Bad:Folder')).toBe(true)
    expect(hasWindowsInvalidCharacter('D:\\Books\\Good Folder')).toBe(false)
    expect(hasWindowsReservedDeviceSegment('D:\\Books\\CON')).toBe(true)
    expect(hasWindowsReservedDeviceSegment('D:\\Books\\NUL.txt')).toBe(true)
    expect(hasWindowsReservedDeviceSegment('D:\\Books\\COM1.folder')).toBe(true)
    expect(hasWindowsReservedDeviceSegment('D:\\Books\\Concert')).toBe(false)
  })

  it('detects overlapping source and destination paths', () => {
    expect(pathsOverlap('D:\\Books\\Title\\Child', 'D:\\Books\\Title', 'windows')).toBe(true)
    expect(pathsOverlap('D:\\Books\\Title', 'D:\\Books\\Title\\Child', 'windows')).toBe(true)
    expect(pathsOverlap('D:\\Books\\Title2', 'D:\\Books\\Title', 'windows')).toBe(false)
    expect(pathsOverlap('/books/title/child', '/books/title', 'unix')).toBe(true)
    expect(pathsOverlap('/books/title2', '/books/title', 'unix')).toBe(false)
  })

  it('validates library destination paths while allowing platform-valid whitespace', () => {
    expect(validateLibraryDestinationPath('D:\\Books\\Title\\.')).toContain(
      'Path traversal is not allowed',
    )
    expect(validateLibraryDestinationPath('D:\\Books\\Title\\..')).toContain(
      'Path traversal is not allowed',
    )
    expect(validateLibraryDestinationPath('D:\\Books\\\\Title')).toContain('empty path segments')
    expect(validateLibraryDestinationPath('D:\\Books\\Bad*Folder')).toContain('invalid on Windows')
    expect(validateLibraryDestinationPath('D:\\Books\\CON.txt')).toContain('reserved Windows')
    expect(validateLibraryDestinationPath('D:\\Books\\test ')).toContain(
      'cannot end with a space or period',
    )
    expect(validateLibraryDestinationPath('D:\\Books\\test.')).toContain(
      'cannot end with a space or period',
    )
    expect(validateLibraryDestinationPath('D:\\Books\\ test')).toBe(null)
    expect(validateLibraryDestinationPath('/books/ test /')).toBe(null)
    expect(validateLibraryDestinationPath('D:\\Books\\Dr. Seuss')).toBe(null)
    expect(validateLibraryDestinationPath('D:\\Books\\.metadata')).toBe(null)
    expect(validateLibraryDestinationPath('D:\\Books\\Title...')).toContain(
      'cannot end with a space or period',
    )
    expect(validateLibraryDestinationPath('/books/Title...')).toBe(null)
    expect(
      validateLibraryDestinationPath('D:\\Books\\Title\\Child', {
        pathKind: 'windows',
        sourcePath: 'D:\\Books\\Title',
      }),
    ).toBe(null)
    expect(
      validateLibraryDestinationPath('/books/title/child', {
        pathKind: 'unix',
        sourcePath: '/books/title',
      }),
    ).toBe(null)
    expect(
      validateLibraryDestinationPath('D:\\Books', {
        pathKind: 'windows',
        sourcePath: 'D:\\Books\\Title',
      }),
    ).toBe(null)
  })

  it('stripRootPrefix removes root prefix when present', () => {
    const root = 'C:\\temp\\Isaac Asimov\\Foundation'
    const full = 'C:\\temp\\Isaac Asimov\\Foundation\\Prelude to Foundation'
    const rel = stripRootPrefix(root, full)
    expect(rel).toBe('Prelude to Foundation')

    // preserves backslash style when root uses backslashes
    const root2 = 'C:/temp/Isaac Asimov/Foundation'
    const full2 = 'C:/temp/Isaac Asimov/Foundation/Prelude to Foundation'
    const rel2 = stripRootPrefix(root2, full2)
    expect(rel2).toBe('Prelude to Foundation')

    // returns null when no match
    expect(stripRootPrefix('C:/root/other', full)).toBe(null)

    // matches using last segments
    const root3 = 'C:/temp/Isaac Asimov/Foundation/Extra'
    const full3 = 'C:/some/prefix/isaac asimov/foundation/Prelude'
    const rel3 = stripRootPrefix(root3, full3)
    expect(rel3).toBe('Prelude')
  })
})
