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
import { getScoreBreakdownTooltip } from '@/composables/useScore'
import type { QualityScore, SearchResult } from '@/types'

describe('useScore composable - rejection behavior', () => {
  it('returns only rejection reason for rejected scores', () => {
    const fakeResult = {
      id: 'r1',
      title: 'T',
      artist: '',
      album: '',
      category: '',
      source: '',
      publishedDate: '',
      format: '',
      size: 0,
      magnetLink: '',
      torrentUrl: '',
      nzbUrl: '',
      downloadType: '',
      quality: '',
    } as unknown as SearchResult
    const score: QualityScore = {
      searchResult: fakeResult,
      totalScore: -1,
      scoreBreakdown: {},
      rejectionReasons: ['Low seeders'],
      isRejected: true,
    }

    const tooltip = getScoreBreakdownTooltip(score)
    expect(tooltip).toBe('Rejected: Low seeders')
  })
})
