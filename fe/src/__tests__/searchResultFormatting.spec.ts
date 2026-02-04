import { describe, it, expect } from 'vitest'
import {
  formatDate,
  formatRuntime,
  capitalizeLanguage,
  capitalizeFirst,
  getYearFromDate,
} from '@/utils/searchResultFormatting'

describe('searchResultFormatting', () => {
  describe('formatDate', () => {
    it('formats ISO 8601 date correctly', () => {
      expect(formatDate('2015-10-05')).toBe('Oct 05, 2015')
    })

    it('handles different dates', () => {
      expect(formatDate('2023-01-15')).toBe('Jan 15, 2023')
      expect(formatDate('2020-12-31')).toBe('Dec 31, 2020')
    })

    it('returns original string on parse error', () => {
      expect(formatDate('invalid')).toBe('invalid')
      // '12345' technically parses to a date (year 12345), so return formatted
      // A better test would be something that truly fails
      expect(formatDate('not-a-date')).toBe('not-a-date')
    })

    it('handles empty string', () => {
      expect(formatDate('')).toBe('')
    })

    it('handles malformed dates gracefully', () => {
      expect(formatDate('2015-13-01')).toBe('2015-13-01')
    })
  })

  describe('formatRuntime', () => {
    it('formats hours and minutes', () => {
      expect(formatRuntime(45900)).toBe('12h 45m') // 12h 45m in seconds
    })

    it('formats only hours', () => {
      expect(formatRuntime(3600)).toBe('1h')
      expect(formatRuntime(7200)).toBe('2h')
    })

    it('formats only minutes', () => {
      expect(formatRuntime(2700)).toBe('45m')
      expect(formatRuntime(600)).toBe('10m')
    })

    it('returns Unknown for zero or negative', () => {
      expect(formatRuntime(0)).toBe('Unknown')
      expect(formatRuntime(-100)).toBe('Unknown')
    })

    it('rounds minutes correctly', () => {
      // 2939 / 60 = 48.98... -> floor = 48m
      expect(formatRuntime(2939)).toBe('48m')
      // 3659 / 60 = 60.98... -> floor = 60m = 1h
      expect(formatRuntime(3659)).toBe('1h')
    })

    it('handles large durations', () => {
      expect(formatRuntime(129600)).toBe('36h') // 36 hours
      expect(formatRuntime(133920)).toBe('37h 12m') // 37h 12m
    })
  })

  describe('capitalizeLanguage', () => {
    it('capitalizes simple language codes', () => {
      expect(capitalizeLanguage('english')).toBe('English')
      expect(capitalizeLanguage('spanish')).toBe('Spanish')
      expect(capitalizeLanguage('french')).toBe('French')
    })

    it('handles language-region codes', () => {
      expect(capitalizeLanguage('english-uk')).toBe('English (UK)')
      expect(capitalizeLanguage('english-ca')).toBe('English (CA)')
      expect(capitalizeLanguage('portuguese-br')).toBe('Portuguese (BR)')
    })

    it('handles mixed case input', () => {
      expect(capitalizeLanguage('ENGLISH')).toBe('English')
      expect(capitalizeLanguage('eNgLiSh')).toBe('English')
    })

    it('returns empty string for falsy input', () => {
      expect(capitalizeLanguage('')).toBe('')
      expect(capitalizeLanguage(undefined)).toBe('')
      expect(capitalizeLanguage(null as any)).toBe('')
    })
  })

  describe('capitalizeFirst', () => {
    it('capitalizes first letter', () => {
      expect(capitalizeFirst('hello')).toBe('Hello')
      expect(capitalizeFirst('world')).toBe('World')
    })

    it('preserves rest of string', () => {
      expect(capitalizeFirst('hello world')).toBe('Hello world')
      expect(capitalizeFirst('HELLO')).toBe('HELLO')
    })

    it('handles single character', () => {
      expect(capitalizeFirst('a')).toBe('A')
    })

    it('returns empty string for empty input', () => {
      expect(capitalizeFirst('')).toBe('')
    })
  })

  describe('getYearFromDate', () => {
    it('extracts year from ISO date', () => {
      expect(getYearFromDate('2015-10-05')).toBe(2015)
      expect(getYearFromDate('2023-01-01')).toBe(2023)
    })

    it('extracts year from year-only string', () => {
      expect(getYearFromDate('2015')).toBe(2015)
      expect(getYearFromDate('1965')).toBe(1965)
    })

    it('returns undefined for invalid year', () => {
      expect(getYearFromDate('invalid')).toBeUndefined()
      expect(getYearFromDate('abcd')).toBeUndefined()
    })

    it('returns undefined for empty/falsy input', () => {
      expect(getYearFromDate('')).toBeUndefined()
      expect(getYearFromDate(undefined)).toBeUndefined()
      expect(getYearFromDate(null as any)).toBeUndefined()
    })

    it('handles edge cases', () => {
      expect(getYearFromDate('999')).toBe(999)
      expect(getYearFromDate('99999')).toBe(9999) // Takes first 4 chars
    })
  })
})
