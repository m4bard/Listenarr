import { describe, expect, it } from 'vitest'
import {
  buildLibraryImportFallbackTitle,
  buildLibraryImportInitialAuthor,
  buildLibraryImportInitialQuery,
  buildLibraryImportSearchParams,
} from '@/utils/libraryImportSearch'

describe('library import search helpers', () => {
  it('prefers detected title and author before folder fallback', () => {
    const item = {
      fullPath: 'C:\\incoming\\Chapter 01.mp3',
      folderName: 'test-import',
      detectedTitle: 'Jack of Shadows',
      detectedAuthor: 'Roger Zelazny',
    }

    expect(buildLibraryImportInitialQuery(item)).toBe('Jack of Shadows')
    expect(buildLibraryImportInitialAuthor(item)).toBe('Roger Zelazny')
    expect(buildLibraryImportSearchParams(item, 5)).toEqual({
      title: 'Jack of Shadows',
      author: 'Roger Zelazny',
      cap: 5,
    })
  })

  it('falls back to the filename or folder when no detected title exists', () => {
    const item = {
      fullPath: 'C:\\incoming\\The Land (3).m4b',
      folderName: 'The Land',
    }

    expect(buildLibraryImportFallbackTitle(item)).toBe('The Land 3')
    expect(buildLibraryImportInitialQuery(item)).toBe('The Land 3')
    expect(buildLibraryImportSearchParams(item, 5)).toEqual({
      title: 'The Land 3',
      cap: 5,
    })
  })

  it('still prioritizes asin over title and author', () => {
    const item = {
      fullPath: 'C:\\incoming\\Book.m4b',
      folderName: 'Book',
      detectedTitle: 'Ignored Title',
      detectedAuthor: 'Ignored Author',
      detectedAsin: 'B0DQR9D4YG',
    }

    expect(buildLibraryImportInitialQuery(item)).toBe('B0DQR9D4YG')
    expect(buildLibraryImportInitialAuthor(item)).toBe('')
    expect(buildLibraryImportSearchParams(item, 5)).toEqual({
      asin: 'B0DQR9D4YG',
      cap: 5,
    })
  })
})
