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
import { computeNormalizedSmart } from '@/composables/useScore'

// Regression tests for Listenarr#178, Bug 2's frontend/backend scale mismatch: this file
// divided the "Indexer" breakdown component by 500, while CompositeScorer.cs multiplied by
// 1000 - the only component in the six-tier breakdown where the frontend divisor didn't match
// the backend multiplier (every other tier follows that pattern exactly). The backend now
// scores the indexer term with CompositeScorer.IndexerPriorityTieBreakWeight = 1.0, so the raw
// breakdown value IS already the priority-inverted 1-50 range and needs no further scaling.
describe('useScore composable - indexer normalization (backend/frontend scale reconciliation)', () => {
  it('normalizes the Indexer component to match CompositeScorer.IndexerPriorityTieBreakWeight (1.0), not the old 500 divisor', () => {
    // Priority 1 (best) -> backend raw = (51 - 1) * 1.0 = 50
    const bestPriority = computeNormalizedSmart({ Indexer: 50 })
    expect(bestPriority.components.Indexer).toBe(50)

    // Priority 50 (worst) -> backend raw = (51 - 50) * 1.0 = 1
    const worstPriority = computeNormalizedSmart({ Indexer: 1 })
    expect(worstPriority.components.Indexer).toBe(1)
  })

  it('agrees with the Quality/Format tiers: divisor equals the backend multiplier for every component', () => {
    // Quality: backend multiplies GetQualityScore (0-100) by 1000; frontend divides by 1000.
    // Format: backend multiplies GetFormatScore (0-100) by 100; frontend divides by 100.
    // Indexer: backend multiplies the 1-50 priority inversion by 1.0; frontend must not divide
    // further, or the two sides drift apart again exactly as they did before this fix.
    const { components } = computeNormalizedSmart({ Quality: 90000, Format: 8500, Indexer: 25 })

    expect(components.Quality).toBe(90)
    expect(components.Format).toBe(85)
    expect(components.Indexer).toBe(25)
  })
})
