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
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, it, expect } from 'vitest'

// Regression guard for the queue/wanted grid container height defect
// (tracker item 293): the container's <style> block forced
// `height: calc(100vh - Npx)` unconditionally, so a container held that much
// space below the last row even when the virtualized content was far
// shorter (measured: 2200px forced height vs 1257px of real content, ~941px
// of dead space). The fix replaces the forced `height` with
// `height: auto` + `max-height: calc(100vh - Npx)`, so the container shrinks
// to fit short content while still capping (and scrolling) long content
// exactly as before.
//
// jsdom does not implement CSS layout: clientHeight/offsetHeight are always
// 0 and getComputedStyle() returns unresolved calc()/vh expressions rather
// than resolved pixel values (verified directly against this checkout's
// jsdom before writing this test). There is no computed-style or real-layout
// test infrastructure in this codebase (no existing spec exercises rendered
// height), so a genuine "does the container actually shrink" test is not
// practical inside the unit test suite. This test instead asserts on the
// component's own <style> source, which is the only thing jsdom lets us
// observe here, and is what actually caused the defect. It fails against
// the pre-fix source (base rule had `height: calc(...)`, no `max-height`)
// and passes against the fixed source.

const readSource = (relativePath: string): string => {
  const url = new URL(relativePath, import.meta.url)
  return readFileSync(fileURLToPath(url), 'utf-8')
}

/**
 * Extracts the CSS declaration block for the first (non-.is-static)
 * `.<selector> {` rule in a component's <style> section.
 */
const extractBaseRule = (source: string, selector: string): string => {
  const pattern = new RegExp(`\\n\\.${selector}\\s*\\{([^}]*)\\}`, 'm')
  const match = source.match(pattern)
  if (!match) {
    throw new Error(`Could not find base ".${selector} { ... }" rule in source`)
  }
  return match[1]
}

describe('queue/wanted grid container height (tracker #293)', () => {
  it('ActivityView .queue-grid-container shrinks to content instead of forcing a fixed viewport height', () => {
    const source = readSource('../views/activity/ActivityView.vue')
    const rule = extractBaseRule(source, 'queue-grid-container')

    expect(rule).toMatch(/^\s*height:\s*auto\s*;/m)
    expect(rule).toMatch(/^\s*max-height:\s*calc\(100vh - 200px\)\s*;/m)
    // The old defect: an unconditional forced height with no cap to shrink under.
    expect(rule).not.toMatch(/^\s*height:\s*calc\(100vh - 200px\)\s*;/m)
  })

  it('WantedView .wanted-grid-container shrinks to content instead of forcing a fixed viewport height', () => {
    const source = readSource('../views/content/WantedView.vue')
    const rule = extractBaseRule(source, 'wanted-grid-container')

    expect(rule).toMatch(/^\s*height:\s*auto\s*;/m)
    expect(rule).toMatch(/^\s*max-height:\s*calc\(100vh - 220px\)\s*;/m)
    expect(rule).not.toMatch(/^\s*height:\s*calc\(100vh - 220px\)\s*;/m)
  })
})
