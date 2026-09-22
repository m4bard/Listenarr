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
import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import QualityProfileFormModal from '@/components/settings/QualityProfileFormModal.vue'
import { getScoreBreakdownTooltip } from '@/composables/useScore'
import type { QualityScore, SearchResult } from '@/types'

describe('minimum score threshold', () => {
  it('accepts a threshold above 100, because an accepted release scores above 100', () => {
    const wrapper = mount(QualityProfileFormModal, {
      global: { plugins: [createPinia()] },
      props: { visible: true, profile: null },
    })

    const input = wrapper.find('#minimumScore')
    expect(input.exists()).toBe(true)
    expect(input.attributes('max')).toBeUndefined()
    expect(input.attributes('min')).toBe('0')
  })
})

describe('score breakdown popover', () => {
  it('reconciles with a total above 100 instead of reporting two different figures', () => {
    // The backend adds preferred words and seeders to a base of 100, so this is an ordinary
    // accepted release. The popover reconstructs base + contributions and only prints a second
    // "Backend Total" line when its own arithmetic disagrees with what the backend sent.
    const result = { id: 'r1', title: 'Some Book' } as unknown as SearchResult
    const score: QualityScore = {
      searchResult: result,
      totalScore: 135,
      scoreBreakdown: { Quality: 100, PreferredWords: 25, Seeders: 10 },
      rejectionReasons: [],
      isRejected: false,
    } as unknown as QualityScore

    const tooltip = getScoreBreakdownTooltip(score)

    expect(tooltip).toContain('PreferredWords: +25')
    expect(tooltip).toContain('Seeders: +10')
    expect(tooltip).toContain('Computed Total: 135')
    expect(tooltip).not.toContain('Backend Total')
  })

  it('still reports the disagreement when a total and its breakdown do not add up', () => {
    // CONTROL. The same breakdown with the capped total the backend used to send. If this ever
    // stops printing both figures, the test above is passing because the popover stopped
    // checking rather than because the totals now agree.
    const result = { id: 'r2', title: 'Some Book' } as unknown as SearchResult
    const score: QualityScore = {
      searchResult: result,
      totalScore: 100,
      scoreBreakdown: { Quality: 100, PreferredWords: 25, Seeders: 10 },
      rejectionReasons: [],
      isRejected: false,
    } as unknown as QualityScore

    const tooltip = getScoreBreakdownTooltip(score)

    expect(tooltip).toContain('Computed Total: 135')
    expect(tooltip).toContain('Backend Total: 100')
  })
})
