import { describe, it, expect } from 'vitest'
import { formatSeriesMemberships, isBundleSeriesNumber } from '@/utils/seriesUtils'

describe('formatSeriesMemberships', () => {
  it('lists every series a book belongs to with its number', () => {
    const result = formatSeriesMemberships({
      series: 'Publication Order',
      seriesNumber: '1',
      seriesMemberships: [
        { seriesName: 'Publication Order', seriesNumber: '1', isPrimary: true, sortOrder: 0 },
        { seriesName: 'Chronological Order', seriesNumber: '3', isPrimary: false, sortOrder: 1 },
      ],
    })
    expect(result).toBe('Publication Order #1, Chronological Order #3')
  })

  it('omits the number when a membership has none', () => {
    const result = formatSeriesMemberships({
      seriesMemberships: [{ seriesName: 'Standalone Saga', isPrimary: true, sortOrder: 0 }],
    })
    expect(result).toBe('Standalone Saga')
  })

  it('falls back to the legacy single series when there are no memberships', () => {
    expect(formatSeriesMemberships({ series: 'Solo Series', seriesNumber: '2' })).toBe(
      'Solo Series #2',
    )
    expect(formatSeriesMemberships({ series: 'No Number' })).toBe('No Number')
  })

  it('returns an empty string when there is no series information', () => {
    expect(formatSeriesMemberships({})).toBe('')
    expect(formatSeriesMemberships({ seriesMemberships: [] })).toBe('')
  })
})

describe('isBundleSeriesNumber', () => {
  it.each(['1-4', '1-2', '1-6', '1-8', '1 - 4', '1-3, 5', '2, 3'])(
    'treats %s as covering more than one book',
    (position) => {
      expect(isBundleSeriesNumber(position)).toBe(true)
    },
  )

  it.each([
    '1',
    '0',
    '12',
    // A novella between two books, not a bundle.
    '1.5',
    // A real Audible position: it has a comma and it is one book.
    '2, Dramatized',
    // Grouped digits are one number, not a list of two.
    '20,000',
    '',
    '   ',
  ])('treats %s as a single book', (position) => {
    expect(isBundleSeriesNumber(position)).toBe(false)
  })

  it('handles a missing position', () => {
    expect(isBundleSeriesNumber(undefined)).toBe(false)
    expect(isBundleSeriesNumber(null)).toBe(false)
  })
})
