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
import { API_BASE_URL } from '@/services/apiBase'

// Websocket URLs for the realtime hubs. Split out of signalr.ts because that module opens a
// connection as soon as it is imported, which makes the URL itself awkward to assert.
//
// The prefix comes from API_BASE_URL, which carries the URL base the backend injected. The
// location-sniffing fallback below predates that and only matters when API_BASE_URL cannot be
// parsed at all.

const API_SUFFIX_REGEX = /\/api(?:\/v\d+(?:\.\d+)?)?$/i
const KNOWN_APP_ROUTE_PREFIXES = [
  '/library-import',
  '/audiobooks',
  '/collection',
  '/add-new',
  '/activity',
  '/wanted',
  '/calendar',
  '/downloads',
  '/settings',
  '/system',
  '/logs',
  '/login',
]

const stripApiSuffix = (value: string): string => value.replace(API_SUFFIX_REGEX, '')
const trimTrailingSlash = (value: string): string => value.replace(/\/+$/, '')
const ensureLeadingSlash = (value: string): string => (value.startsWith('/') ? value : `/${value}`)

const normalizePathPrefix = (value: string): string => {
  const trimmed = trimTrailingSlash((value || '').trim())
  if (!trimmed || trimmed === '/') return ''
  return ensureLeadingSlash(trimmed)
}

const toWebSocketOrigin = (httpOrigin: string): string => {
  const trimmed = (httpOrigin || '').trim()
  if (!trimmed) return ''
  if (trimmed.startsWith('https://')) return `wss://${trimmed.slice('https://'.length)}`
  if (trimmed.startsWith('http://')) return `ws://${trimmed.slice('http://'.length)}`
  return trimmed
}

const detectPathPrefixFromLocation = (): string => {
  if (typeof window === 'undefined') return ''
  const pathname = window.location?.pathname || '/'
  if (!pathname || pathname === '/') return ''

  for (const routePrefix of KNOWN_APP_ROUTE_PREFIXES) {
    const idx = pathname.indexOf(routePrefix)
    if (idx > 0) {
      return normalizePathPrefix(pathname.slice(0, idx))
    }
    if (idx === 0) {
      return ''
    }
  }

  // Fallback for subpath root requests (e.g., /listenarr before router navigation).
  const segments = pathname.split('/').filter(Boolean)
  if (segments.length === 1) {
    return normalizePathPrefix(`/${segments[0]}`)
  }

  return ''
}

const resolveHubHttpBase = (): { origin: string; pathPrefix: string } => {
  const browserOrigin =
    typeof window !== 'undefined' && window.location?.origin
      ? window.location.origin
      : 'http://localhost'

  const candidates = [
    (import.meta.env.VITE_API_BASE_URL || '').toString().trim(),
    (API_BASE_URL || '').toString().trim(),
  ]

  for (const candidate of candidates) {
    if (!candidate) continue
    try {
      const url = new URL(candidate, browserOrigin)
      const pathPrefix = normalizePathPrefix(stripApiSuffix(url.pathname || '/'))
      return { origin: url.origin || browserOrigin, pathPrefix }
    } catch {
      // Continue to next candidate.
    }
  }

  return { origin: browserOrigin, pathPrefix: detectPathPrefixFromLocation() }
}

export const buildHubWebSocketUrl = (hubPath: '/hubs/downloads' | '/hubs/settings'): string => {
  const resolved = resolveHubHttpBase()
  const origin = toWebSocketOrigin(resolved.origin)
  const prefix = normalizePathPrefix(resolved.pathPrefix)
  return `${origin}${prefix}${hubPath}`
}
