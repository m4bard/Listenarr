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

/**
 * Normalizes a collection-grouping label (author, series, genre, narrator, publisher, ...)
 * for comparison purposes: strips diacritics, lowercases, and collapses everything that
 * isn't a letter or digit down to single spaces. Two spellings that differ only in case,
 * punctuation, accents, or whitespace normalize to the same string.
 *
 * This is the single normalizer shared by every screen that groups or matches books by one
 * of these labels (the library grid's author/series cards, the author/series detail page,
 * and anything that navigates between them) so they can never disagree about which spelling
 * variants are "the same" one.
 */
export function normalizeCollectionText(value: string | undefined | null): string {
  if (!value) return ''
  return value
    .normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, ' ')
    .trim()
}

/**
 * Normalizes an identifier (ASIN, ISBN) for comparison: strips everything but letters and
 * digits and upcases the result.
 */
export function normalizeIdentifier(value: string | undefined | null): string {
  if (!value) return ''
  return value.replace(/[^A-Za-z0-9]/g, '').toUpperCase()
}

/**
 * A stable, order-independent key for a book's author list: each author normalized,
 * deduplicated, sorted, and joined. Used to compare "the same set of authors" across two
 * books regardless of spelling variance or author order.
 */
export function normalizeAuthorKey(authors: string[] | undefined): string {
  return (authors || [])
    .map((author) => normalizeCollectionText(author))
    .filter(Boolean)
    .sort()
    .join('|')
}

/**
 * A stable key combining a normalized title and a normalized author-set key, used to match
 * "the same book" across sources that may spell the title or authors slightly differently.
 */
export function buildTitleAuthorKey(title: string | undefined, authors: string[] | undefined): string {
  return `${normalizeCollectionText(title)}::${normalizeAuthorKey(authors)}`
}
