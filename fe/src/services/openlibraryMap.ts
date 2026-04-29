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
// Utility to map OpenLibraryBook to canonical SearchResult
import type { OpenLibraryBook } from './openlibrary'
import type { SearchResult } from '@/types'

/**
 * Map an OpenLibraryBook to the canonical SearchResult structure.
 * This ensures OpenLibrary results are compatible with Audible/Audnexus results.
 */
export function mapOpenLibraryBookToSearchResult(book: OpenLibraryBook): SearchResult {
  return {
    id: book.key || '',
    title: book.title || '',
    artist: Array.isArray(book.author_name) ? book.author_name.join(', ') : book.author_name || '',
    album: '',
    category: '',
    source: 'OpenLibrary',
    sourceLink: book.key ? `https://openlibrary.org${book.key}` : undefined,
    publishedDate: book.first_publish_year ? String(book.first_publish_year) : '',
    format: 'Book',
    score: undefined,
    // Indexer-specific (not used)
    thumbnailRetentionDays: undefined,
    size: 0,
    seeders: undefined,
    leechers: undefined,
    grabs: undefined,
    files: undefined,
    magnetLink: '',
    torrentUrl: '',
    nzbUrl: '',
    downloadType: '',
    quality: undefined,
    resultUrl: book.key ? `https://openlibrary.org${book.key}` : undefined,
    // Metadata-specific
    description: undefined,
    subtitle: undefined,
    publisher: book.publisher
      ? Array.isArray(book.publisher)
        ? book.publisher.join(', ')
        : book.publisher
      : undefined,
    language: book.language
      ? Array.isArray(book.language)
        ? book.language[0]
        : book.language
      : undefined,
    runtime: undefined,
    narrator: undefined,
    imageUrl: book.cover_i
      ? `https://covers.openlibrary.org/b/id/${book.cover_i}-L.jpg`
      : undefined,
    asin: '',
    isbn: book.isbn && book.isbn.length > 0 ? book.isbn[0] : '',
    series: book.seriesList && book.seriesList.length > 0 ? book.seriesList[0] : undefined,
    seriesNumber: undefined,
    seriesList: book.seriesList,
    genres: book.subject
      ? Array.isArray(book.subject)
        ? book.subject
        : [book.subject]
      : undefined,
    productUrl: book.key ? `https://openlibrary.org${book.key}` : undefined,
    isEnriched: false,
    metadataSource: 'openlibrary',
    authors: book.author_name
      ? Array.isArray(book.author_name)
        ? book.author_name.map((name) => ({ name }))
        : [{ name: book.author_name }]
      : undefined,
    narrators: [],
    lengthMinutes: undefined,
    link: book.key ? `https://openlibrary.org${book.key}` : undefined,
    releaseDate: book.first_publish_year ? String(book.first_publish_year) : undefined,
    publishDate: book.first_publish_year ? String(book.first_publish_year) : undefined,
  }
}
