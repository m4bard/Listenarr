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
export const TITLE_SIMILARITY_FLOOR = 0.85

const LEADING_ARTICLE = /^(the|a|an)\s+/

const NUMBER_WORDS =
  'one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen twenty'.split(
    ' ',
  )
const ROMAN_NUMERALS =
  'i ii iii iv v vi vii viii ix x xi xii xiii xiv xv xvi xvii xviii xix xx'.split(' ')
const VOLUME_MARKER = new RegExp(
  `\\b(book|part|volume|vol)\\s+(\\d+|${NUMBER_WORDS.join('|')}|${ROMAN_NUMERALS.join('|')})\\b`,
  'gu',
)

function volumeValue(marker: string): number {
  return NUMBER_WORDS.indexOf(marker) + 1 || ROMAN_NUMERALS.indexOf(marker) + 1 || Number(marker)
}

// "vol 3", "volume three" and "volume iii" all read "volume 3", so the same volume is the same words
function canonicalVolume(_match: string, kind: string, marker: string): string {
  return `${kind === 'vol' ? 'volume' : kind} ${volumeValue(marker)}`
}

// The volume a normalized title names, as a number, or null
function volumeNumber(normalized: string): number | null {
  const marker = /\b(?:book|part|volume) (\d+)\b/u.exec(normalized)
  return marker ? Number(marker[1]) : null
}

function foldAccents(text: string): string {
  return text.normalize('NFD').replace(/[̀-ͯ]/g, '')
}

// The title as it is compared (keepBracketed leaves a "(Part 2)" or "[Part 2]" in, for its volume): no case, accents, folder number ("01 - "), parenthetical, bracketed
// or "- Unabridged" suffix or leading article; "&" read as "and"; volume markers in one form
function normalizeTitle(title: string, keepBracketed = false): string {
  return foldAccents(title.toLowerCase())
    .replace(/^\s*\d+\s*[-.]\s+/, '')
    .replace(
      keepBracketed ? /\s-\s+unabridged\b/g : /\([^)]*\)|\[[^\]]*\]|\s-\s+unabridged\b/g,
      ' ',
    )
    .replace(/&/g, ' and ')
    .replace(/['\u2019]/g, '')
    .replace(/[^\p{L}\p{N}:;\s]/gu, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .replace(VOLUME_MARKER, canonicalVolume)
    .replace(LEADING_ARTICLE, '')
}

function tokens(text: string): string[] {
  return text.split(/[^\p{L}\p{N}]+/u).filter(Boolean)
}

// The title's words, and its words before a subtitle or "Series, Book N" suffix (after ':' or ';')
function titleParts(title: string): {
  full: string[]
  main: string[]
  hasSubtitle: boolean
  volume: number | null
} {
  const normalized = normalizeTitle(title)
  const main = normalized.split(/[:;]/)[0] ?? normalized
  return {
    full: tokens(normalized),
    main: tokens(main),
    hasSubtitle: main !== normalized,
    volume: volumeNumber(normalizeTitle(title, true)),
  }
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

// 1 for the same title, 0 for none in common or for different volumes of one title. A subtitle or series suffix on one side is ignored
// when the other side has none; two titles that both have one are compared whole, so "Series: One"
// and "Series: Two" are not the same book.
export function titleSimilarity(detected: string, candidate: string): number {
  const d = titleParts(detected)
  const c = titleParts(candidate)
  if (!d.full.length || !c.full.length) return 0
  if (d.volume !== null && c.volume !== null && d.volume !== c.volume) return 0
  return Math.max(
    dice(d.full, c.full),
    d.hasSubtitle ? 0 : dice(d.full, c.main),
    c.hasSubtitle ? 0 : dice(d.main, c.full),
  )
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

// Whole-title equality, so "Dune" beats "Dune: Messiah" even though both score 1
function isSameTitle(detected: string, candidate: string): boolean {
  return normalizeTitle(detected) === normalizeTitle(candidate)
}

// From a list of search results, the one whose title best matches the detected title (an equal
// score goes to the same whole title, then to the matching author), or null when none resembles it.
export function pickBestMatch(
  results: SearchResult[],
  detectedTitle: string,
  detectedAuthor?: string,
): SearchResult | null {
  let best: { result: SearchResult; score: number; sameTitle: boolean; authorOk: boolean } | null =
    null
  for (const result of results) {
    const score = titleSimilarity(detectedTitle, result.title ?? '')
    if (score < TITLE_SIMILARITY_FLOOR) continue
    const sameTitle = isSameTitle(detectedTitle, result.title ?? '')
    const authorOk = authorMatches(detectedAuthor, result)
    const better =
      !best ||
      score > best.score ||
      (score === best.score &&
        (sameTitle !== best.sameTitle ? sameTitle : authorOk && !best.authorOk))
    if (better) best = { result, score, sameTitle, authorOk }
  }
  return best?.result ?? null
}
