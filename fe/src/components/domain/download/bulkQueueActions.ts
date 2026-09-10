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
export interface BulkOutcome {
  succeeded: string[]
  failed: string[]
}

/**
 * Run one call per row, in order, and report per id.
 *
 * Sequential on purpose. These are the same per-row endpoints the single-row actions use, several
 * of them reach a download client, and a burst of parallel deletes is a good way to be rate
 * limited by one. A failure does not stop the run: the ids that failed come back so the caller can
 * leave them selected.
 */
export async function runSequentially<T extends { id: string }>(
  rows: T[],
  action: (row: T) => Promise<unknown>,
  onError?: (row: T, error: unknown) => void,
): Promise<BulkOutcome> {
  const succeeded: string[] = []
  const failed: string[] = []

  for (const row of rows) {
    try {
      await action(row)
      succeeded.push(row.id)
    } catch (error) {
      failed.push(row.id)
      onError?.(row, error)
    }
  }

  return { succeeded, failed }
}

/** One line for one toast: "Removed 7 of 9. 2 failed." */
export function summarizeBulk(verb: string, outcome: BulkOutcome): string {
  const total = outcome.succeeded.length + outcome.failed.length
  const head = `${verb} ${outcome.succeeded.length} of ${total}.`
  return outcome.failed.length > 0 ? `${head} ${outcome.failed.length} failed.` : head
}
