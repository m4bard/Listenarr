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

// Shared, stateless helpers for surfaces that offer automatic search. `WantedView.vue`
// defines its own copies of both (see `757ff97c9`); they are left as-is there so this
// extraction does not touch a surface that already shipped. New callers should import
// from here instead of redefining them.

// Spacing between per-book searches in a bulk run, so one click does not burst every
// configured indexer.
export const SEARCH_SPACING_MS = 1000

export function formatSearchDuration(count: number): string {
  const seconds = Math.round((count * SEARCH_SPACING_MS) / 1000)
  if (seconds < 60) return `${Math.max(seconds, 1)} seconds`
  const minutes = Math.round(seconds / 60)
  return minutes === 1 ? 'a minute' : `${minutes} minutes`
}
