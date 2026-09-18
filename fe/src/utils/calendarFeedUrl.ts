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
 * Builds the iCalendar subscription URLs the calendar feed modal hands to an operator.
 *
 * The shape follows the *arr calendar link modals, which all build one host-relative string
 * and derive an https and a webcal form from it. Readarr does this in
 * frontend/src/Calendar/iCal/CalendarLinkModalContent.js (getUrls, and the two URLs at :38-39);
 * Sonarr's is frontend/src/Calendar/iCal/CalendarLinkModalContent.tsx:50-75.
 *
 * Split out of the component so the URL arithmetic is testable on its own, and because the
 * query is the part an operator pastes into third-party software and cannot debug.
 */

/** The feed route, unversioned against the API version. Matches CalendarFeedController. */
export const CALENDAR_FEED_PATH = '/feed/v1/calendar/Listenarr.ics'

/** Days back from today. The default the *arr feeds use. */
export const CALENDAR_FEED_DEFAULT_PAST_DAYS = 7

/** Days forward from today. The default the *arr feeds use. */
export const CALENDAR_FEED_DEFAULT_FUTURE_DAYS = 28

/** Mirrors CalendarQueryParameters.MaxFeedDays on the server. */
export const CALENDAR_FEED_MAX_DAYS = 3650

/** Strips the /api/v{n} suffix off the API base path, leaving any reverse-proxy prefix. */
const API_SUFFIX_REGEX = /\/api(?:\/v\d+(?:\.\d+)?)?\/?$/i

export interface CalendarFeedOptions {
  /** Include unmonitored audiobooks. */
  unmonitored: boolean
  /** Days back from today. */
  pastDays: number | undefined
  /** Days forward from today. */
  futureDays: number | undefined
  /** Comma separated tag names, as typed. */
  tags: string
  /** The instance API key. Without it there is no usable URL. */
  apiKey: string
}

export interface CalendarFeedOrigin {
  /** window.location.protocol, including the trailing colon. */
  protocol: string
  /** window.location.host, including the port when there is one. */
  host: string
}

export interface CalendarFeedUrls {
  /** The https (or http) form, for pasting into a calendar client. */
  httpUrl: string
  /** The webcal form, which most desktop clients register a handler for. */
  webcalUrl: string
}

/**
 * Recovers the path prefix Listenarr is mounted under, which is the Listenarr equivalent of
 * window.Readarr.urlBase. The frontend never receives the prefix directly, but the API base
 * path carries it, so removing the /api/v{n} tail leaves it behind.
 */
export function calendarFeedUrlBase(apiBasePath: string): string {
  const trimmed = (apiBasePath || '').trim()
  if (!trimmed) return ''
  const withoutApi = trimmed.replace(API_SUFFIX_REGEX, '')
  const withoutTrailingSlash = withoutApi.replace(/\/+$/, '')
  return withoutTrailingSlash === '/' ? '' : withoutTrailingSlash
}

/**
 * Clamps a day count the way CalendarQueryParameters.ClampFeedDays does, so the URL shown to
 * the operator is the window the server will serve rather than one it quietly narrows.
 */
export function clampFeedDays(days: number | undefined, fallback: number): number {
  const candidate = typeof days === 'number' && Number.isFinite(days) ? days : fallback
  return Math.min(Math.max(Math.trunc(candidate), 0), CALENDAR_FEED_MAX_DAYS)
}

/** Splits the comma separated tag box the same way the server splits the query parameter. */
function normalizeTags(tags: string): string[] {
  return (tags || '')
    .split(',')
    .map((tag) => tag.trim())
    .filter((tag) => tag.length > 0)
}

export function buildCalendarFeedUrls(
  options: CalendarFeedOptions,
  origin: CalendarFeedOrigin,
  urlBase: string,
): CalendarFeedUrls {
  if (!options.apiKey) {
    // A link without the key is refused by [RequireApiKey], and the 401 surfaces inside the
    // calendar client where the cause is not visible. Offer nothing rather than a dead URL.
    return { httpUrl: '', webcalUrl: '' }
  }

  const query: string[] = []

  if (options.unmonitored) {
    query.push('unmonitored=true')
  }

  const tags = normalizeTags(options.tags)
  if (tags.length > 0) {
    // "tags" is what CalendarFeedController binds. Readarr's modal emits tags= while its own
    // controller binds tagList (Readarr.Api.V1/Calendar/CalendarFeedController.cs:31), so
    // Readarr's generated URL silently ignores the tag filter. Do not copy that.
    query.push(`tags=${encodeURIComponent(tags.join(','))}`)
  }

  query.push(`pastDays=${clampFeedDays(options.pastDays, CALENDAR_FEED_DEFAULT_PAST_DAYS)}`)
  query.push(`futureDays=${clampFeedDays(options.futureDays, CALENDAR_FEED_DEFAULT_FUTURE_DAYS)}`)

  // Last on purpose: a paste truncated at the end loses the key and fails loudly, rather than
  // losing a filter and quietly serving a different window.
  query.push(`apikey=${encodeURIComponent(options.apiKey)}`)

  const relative = `${origin.host}${urlBase}${CALENDAR_FEED_PATH}?${query.join('&')}`

  return {
    httpUrl: `${origin.protocol}//${relative}`,
    webcalUrl: `webcal://${relative}`,
  }
}
