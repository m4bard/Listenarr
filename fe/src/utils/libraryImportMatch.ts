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
import type { SearchResult } from '@/types'

// A search answers a loose pool of candidates, so an automatic match is only taken when the
// candidate's title resembles the detected one. Below the floor the row stays unmatched: the user
// picks, instead of importing the wrong book.
export const TITLE_SIMILARITY_FLOOR = 0.8

const LEADING_ARTICLE = /^(the|a|an)\s+/

function foldAccents(text: string): string {
  return text.normalize('NFD').replace(/[̀-ͯ]/g, '')
}

// The title as it is compared: no case, accents, folder number ("01 - "), parenthetical or leading article
function normalizeTitle(title: string): string {
  return foldAccents(title.toLowerCase())
    .replace(/^\s*\d+\s*[-.]\s+/, '')
    .replace(/\([^)]*\)/g, ' ')
    .replace(/[^\p{L}\p{N}:;\s]/gu, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .replace(LEADING_ARTICLE, '')
}

function tokens(text: string): string[] {
  return text.split(/[^\p{L}\p{N}]+/u).filter(Boolean)
}

// The title as given, and without a subtitle or "Series, Book N" suffix
function titleForms(title: string): string[][] {
  const normalized = normalizeTitle(title)
  const main = normalized.split(/[:;]/)[0] ?? normalized
  return [tokens(normalized), tokens(main)].filter((t) => t.length > 0)
}

function dice(a: string[], b: string[]): number {
  const remaining = [...b]
  let shared = 0
  for (const token of a) {
    const at = remaining.indexOf(token)
    if (at >= 0) {
      shared++
      remaining.splice(at, 1)
    }
  }
  return (2 * shared) / (a.length + b.length)
}

// 1 for the same title, 0 for none in common; a subtitle or series suffix on either side is ignored
export function titleSimilarity(detected: string, candidate: string): number {
  const candidateForms = titleForms(candidate)
  return Math.max(0, ...titleForms(detected).flatMap((d) => candidateForms.map((c) => dice(d, c))))
}

function compactName(name: string): string {
  return foldAccents(name.toLowerCase()).replace(/[^\p{L}\p{N}]/gu, '')
}

// A missing author name on either side is not a match
function authorMatches(detectedAuthor: string | undefined, result: SearchResult): boolean {
  const detected = compactName(detectedAuthor ?? '')
  const candidate = compactName(result.authors?.[0]?.name ?? '')
  return !!detected && !!candidate && (candidate.includes(detected) || detected.includes(candidate))
}

// From a list of search results, the one whose title best matches the detected title (then whose
// author matches), or null when none resembles it.
export function pickBestMatch(
  results: SearchResult[],
  detectedTitle: string,
  detectedAuthor?: string,
): SearchResult | null {
  let best: { result: SearchResult; score: number; authorOk: boolean } | null = null
  for (const result of results) {
    const score = titleSimilarity(detectedTitle, result.title ?? '')
    if (score < TITLE_SIMILARITY_FLOOR) continue
    const authorOk = authorMatches(detectedAuthor, result)
    if (!best || score > best.score || (score === best.score && authorOk && !best.authorOk)) {
      best = { result, score, authorOk }
    }
  }
  return best?.result ?? null
}
