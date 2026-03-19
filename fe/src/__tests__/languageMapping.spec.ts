import { describe, expect, it } from 'vitest'
import {
  normalizePreferredSearchLanguage,
  normalizeSearchResultLanguage,
} from '@/utils/languageMapping'

describe('languageMapping', () => {
  it('falls back safely for unsupported legacy preferred language values', () => {
    expect(normalizePreferredSearchLanguage('br')).toBe('english')
    expect(normalizePreferredSearchLanguage('portuguese')).toBe('english')
    expect(normalizePreferredSearchLanguage('jp')).toBe('english')
    expect(normalizePreferredSearchLanguage('japanese')).toBe('english')
  })

  it('does not map unsupported result languages into a supported filter', () => {
    expect(normalizeSearchResultLanguage('br')).toBeUndefined()
    expect(normalizeSearchResultLanguage('portuguese')).toBeUndefined()
    expect(normalizeSearchResultLanguage('jp')).toBeUndefined()
    expect(normalizeSearchResultLanguage('japanese')).toBeUndefined()
  })
})
