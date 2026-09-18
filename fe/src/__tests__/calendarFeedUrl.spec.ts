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
import {
  CALENDAR_FEED_DEFAULT_FUTURE_DAYS,
  CALENDAR_FEED_DEFAULT_PAST_DAYS,
  CALENDAR_FEED_MAX_DAYS,
  CALENDAR_FEED_PATH,
  buildCalendarFeedUrls,
  calendarFeedUrlBase,
  clampFeedDays,
  resolveCalendarFeedOrigin,
} from '@/utils/calendarFeedUrl'

const origin = { protocol: 'https:', host: 'books.example:8443' }

const defaults = {
  unmonitored: false,
  pastDays: CALENDAR_FEED_DEFAULT_PAST_DAYS,
  futureDays: CALENDAR_FEED_DEFAULT_FUTURE_DAYS,
  tags: '',
  apiKey: 'abc123',
}

describe('calendarFeedUrlBase', () => {
  // The Listenarr equivalent of window.Readarr.urlBase is whatever sits in front of the API
  // base path, so it is recovered by stripping the /api/v{n} suffix rather than guessed.
  it.each([
    ['/api/v1', ''],
    ['/api/v1.0', ''],
    ['/api', ''],
    ['', ''],
    ['/listenarr/api/v1', '/listenarr'],
    ['/listenarr/api/v2', '/listenarr'],
    ['/deep/path/api', '/deep/path'],
    ['/listenarr/', '/listenarr'],
  ])('reduces %s to %s', (apiBasePath, expected) => {
    expect(calendarFeedUrlBase(apiBasePath)).toBe(expected)
  })
})

describe('clampFeedDays', () => {
  // Mirrors CalendarQueryParameters.ClampFeedDays so the URL handed to the operator is the
  // window the server will actually serve, rather than one it silently narrows.
  it('keeps a value inside the supported range', () => {
    expect(clampFeedDays(14, CALENDAR_FEED_DEFAULT_PAST_DAYS)).toBe(14)
  })

  it('floors a negative value at zero', () => {
    expect(clampFeedDays(-5, CALENDAR_FEED_DEFAULT_PAST_DAYS)).toBe(0)
  })

  it('caps at the server ceiling', () => {
    expect(clampFeedDays(CALENDAR_FEED_MAX_DAYS + 1000, CALENDAR_FEED_DEFAULT_PAST_DAYS)).toBe(
      CALENDAR_FEED_MAX_DAYS,
    )
  })

  it('falls back to the supplied default when the input is not a number', () => {
    expect(clampFeedDays(Number.NaN, 28)).toBe(28)
    expect(clampFeedDays(undefined, 28)).toBe(28)
  })

  it('truncates a fractional value, since the server parameter is an int', () => {
    expect(clampFeedDays(7.9, CALENDAR_FEED_DEFAULT_PAST_DAYS)).toBe(7)
  })
})

describe('buildCalendarFeedUrls', () => {
  it('builds an absolute https url and a webcal url over the same query', () => {
    const { httpUrl, webcalUrl } = buildCalendarFeedUrls(defaults, origin, '')

    expect(httpUrl).toBe(
      `https://books.example:8443${CALENDAR_FEED_PATH}?pastDays=7&futureDays=28&apikey=abc123`,
    )
    expect(webcalUrl).toBe(
      `webcal://books.example:8443${CALENDAR_FEED_PATH}?pastDays=7&futureDays=28&apikey=abc123`,
    )
  })

  it('keeps the url base in front of the feed path', () => {
    const { httpUrl, webcalUrl } = buildCalendarFeedUrls(defaults, origin, '/listenarr')

    expect(httpUrl).toContain(`books.example:8443/listenarr${CALENDAR_FEED_PATH}?`)
    expect(webcalUrl).toContain(`books.example:8443/listenarr${CALENDAR_FEED_PATH}?`)
  })

  it('omits unmonitored unless it is switched on, matching the *arr modals', () => {
    expect(buildCalendarFeedUrls(defaults, origin, '').httpUrl).not.toContain('unmonitored')
    expect(buildCalendarFeedUrls({ ...defaults, unmonitored: true }, origin, '').httpUrl).toContain(
      'unmonitored=true',
    )
  })

  it('spells the tag parameter tags, which is what the controller reads', () => {
    // Readarr's own modal emits tags= while its controller binds tagList, so Readarr's
    // generated URL ignores the tag filter entirely. Listenarr's controller reads tags.
    const { httpUrl } = buildCalendarFeedUrls({ ...defaults, tags: 'tbr, classics' }, origin, '')

    expect(httpUrl).toContain('tags=tbr%2Cclassics')
    expect(httpUrl).not.toContain('tagList')
  })

  it('drops blank and whitespace-only tag entries', () => {
    expect(
      buildCalendarFeedUrls({ ...defaults, tags: '  ,  , tbr ,, ' }, origin, '').httpUrl,
    ).toContain('tags=tbr&')
    expect(buildCalendarFeedUrls({ ...defaults, tags: '  ,  ' }, origin, '').httpUrl).not.toContain(
      'tags=',
    )
  })

  it('url-encodes the api key rather than pasting it in raw', () => {
    const { httpUrl } = buildCalendarFeedUrls({ ...defaults, apiKey: 'a b+c/d' }, origin, '')

    expect(httpUrl).toContain('apikey=a%20b%2Bc%2Fd')
    expect(httpUrl).not.toContain('apikey=a b+c/d')
  })

  it('puts the api key last so a truncated paste fails loudly rather than silently', () => {
    const { httpUrl } = buildCalendarFeedUrls(
      { ...defaults, unmonitored: true, tags: 'tbr' },
      origin,
      '',
    )

    expect(httpUrl.indexOf('apikey=')).toBeGreaterThan(httpUrl.indexOf('tags='))
    expect(httpUrl.indexOf('apikey=')).toBeGreaterThan(httpUrl.indexOf('unmonitored='))
  })

  it('clamps the day counts into the range the server accepts', () => {
    const { httpUrl } = buildCalendarFeedUrls(
      { ...defaults, pastDays: -1, futureDays: CALENDAR_FEED_MAX_DAYS + 1 },
      origin,
      '',
    )

    expect(httpUrl).toContain('pastDays=0')
    expect(httpUrl).toContain(`futureDays=${CALENDAR_FEED_MAX_DAYS}`)
  })

  it('returns an empty pair when no api key is available yet', () => {
    // A bare link without the key 401s under [RequireApiKey], and the failure is not obvious
    // from the client's error message, so it is better not to offer one at all.
    expect(buildCalendarFeedUrls({ ...defaults, apiKey: '' }, origin, '')).toEqual({
      httpUrl: '',
      webcalUrl: '',
    })
  })

  describe('resolveCalendarFeedOrigin', () => {
    const location = { protocol: 'https:', host: 'spa.example' }

    it('uses the browser origin when the api base is relative', () => {
      // The default deployment: the instance serves the SPA and the feed, so the address the
      // browser reached is the address a calendar client can reach. This is what Readarr and
      // Sonarr assume unconditionally.
      expect(resolveCalendarFeedOrigin('', location, false)).toEqual(location)
      expect(resolveCalendarFeedOrigin('/listenarr', location, false)).toEqual(location)
    })

    it('uses the api origin when the api lives on another host', () => {
      // fe/src/services/apiBase.ts allows an absolute VITE_API_BASE_URL, and API_BASE_PATH is
      // toPath() of it, which discards the host. Building the feed URL from window.location would
      // point it at the SPA origin, which does not serve /feed. Listenarr's config permits this
      // split; Readarr's does not, so this is a divergence rather than a copied assumption.
      expect(resolveCalendarFeedOrigin('https://api.example.com', location, false)).toEqual({
        protocol: 'https:',
        host: 'api.example.com',
      })
    })

    it('keeps a non-default port on the api origin', () => {
      expect(resolveCalendarFeedOrigin('http://api.example.com:9090', location, false)).toEqual({
        protocol: 'http:',
        host: 'api.example.com:9090',
      })
    })

    it('falls back to the browser origin when the api origin will not parse', () => {
      expect(resolveCalendarFeedOrigin('not a url', location, false)).toEqual(location)
    })

    it('ignores the api origin under the dev server', () => {
      // API_ORIGIN is a hardcoded default in dev rather than a derived value, so it is not
      // evidence about where anything is reachable. The browser origin is the only honest answer
      // there, and the modal says separately that a dev URL is not servable.
      expect(resolveCalendarFeedOrigin('http://localhost:9999', location, true)).toEqual(location)
    })
  })

  it('carries the *arr defaults', () => {
    expect(CALENDAR_FEED_DEFAULT_PAST_DAYS).toBe(7)
    expect(CALENDAR_FEED_DEFAULT_FUTURE_DAYS).toBe(28)
    expect(CALENDAR_FEED_PATH).toBe('/feed/v1/calendar/Listenarr.ics')
  })
})
