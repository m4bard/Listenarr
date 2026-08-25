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
const DEFAULT_API_ROOT = '/api'
const DEFAULT_API_VERSION = '1'
const API_PREFIX_REGEX = /^\/api(?:\/v\d+(?:\.\d+)?)?/i
const API_SUFFIX_REGEX = /\/api(?:\/v\d+(?:\.\d+)?)?$/i
const ABSOLUTE_URL_REGEX = /^https?:\/\//i

const trimTrailingSlash = (value: string): string => value.replace(/\/+$/, '')

/**
 * Path the document is served under, taken from the <base href> the server injects.
 * Empty at the site root, '/example' when mounted on a sub-path.
 */
const computeAppBasePath = (): string => {
  if (typeof document === 'undefined') return ''
  const baseUri = document.baseURI
  if (!baseUri) return ''
  try {
    // Resolving './' against the base URI drops any filename, leaving the directory the
    // relative asset URLs in the built index.html are resolved against.
    return trimTrailingSlash(new URL('./', baseUri).pathname)
  } catch {
    return ''
  }
}

/**
 * Where the API lives before the version segment is appended.
 *
 * A VITE_API_BASE_URL naming another host is used verbatim; that deployment has said exactly where
 * to go. A path, whether configured or defaulted, is resolved against the base the document was
 * served under, so one build works at the site root and on any sub-path with no rebuild. The
 * shipped .env.production sets the path form, so the configured value cannot be treated as
 * already absolute against the site root.
 */
const computeApiBaseTemplate = (): string => {
  if (import.meta.env.DEV) return DEFAULT_API_ROOT
  const configured = (import.meta.env.VITE_API_BASE_URL || '').toString().trim()
  const template = configured || DEFAULT_API_ROOT
  if (ABSOLUTE_URL_REGEX.test(template)) return template
  const rooted = template.startsWith('/') ? template : `/${template}`
  return `${computeAppBasePath()}${rooted}`
}

const normalizeApiVersion = (value: string | undefined): string => {
  const normalized = (value || '').trim().replace(/^v/i, '')
  if (!normalized) return DEFAULT_API_VERSION
  // Treat equivalent forms like "1.0" or "1.0.0" as "1" to avoid
  // unnecessary runtime base-path churn (e.g., /api/v1 -> /api/v1.0).
  if (/^\d+(?:\.0+)+$/.test(normalized)) {
    const major = normalized.split('.')[0]
    return major && major.length > 0 ? major : DEFAULT_API_VERSION
  }
  return normalized
}

const normalizeApiBase = (base: string): string => {
  const trimmed = trimTrailingSlash((base || '').trim())
  if (!trimmed) return `${DEFAULT_API_ROOT}/${API_VERSION_SEGMENT}`
  if (/\/api\/v\d+(?:\.\d+)?$/i.test(trimmed)) return trimmed
  if (/\/api$/i.test(trimmed)) return `${trimmed}/${API_VERSION_SEGMENT}`
  return trimmed
}

const buildVersionedApiBase = (baseTemplate: string, apiVersionSegment: string): string => {
  const trimmed = trimTrailingSlash((baseTemplate || '').trim())
  if (!trimmed) return `${DEFAULT_API_ROOT}/${apiVersionSegment}`
  if (/\/api\/v\d+(?:\.\d+)?$/i.test(trimmed)) {
    return trimmed.replace(/\/api\/v\d+(?:\.\d+)?$/i, `/api/${apiVersionSegment}`)
  }
  if (/\/api$/i.test(trimmed)) return `${trimmed}/${apiVersionSegment}`
  return normalizeApiBase(trimmed)
}

const toPath = (base: string): string => {
  try {
    if (base.startsWith('http://') || base.startsWith('https://')) {
      return trimTrailingSlash(new URL(base).pathname)
    }
  } catch {}
  return trimTrailingSlash(base)
}

const normalizeEndpoint = (endpoint: string): string => {
  if (!endpoint) return ''
  const withLeadingSlash = endpoint.startsWith('/') ? endpoint : `/${endpoint}`
  return withLeadingSlash.replace(API_PREFIX_REGEX, '')
}

export let API_VERSION = normalizeApiVersion(import.meta.env.VITE_API_VERSION)
export let API_VERSION_SEGMENT = `v${API_VERSION}`

const computeApiBaseUrl = (): string =>
  buildVersionedApiBase(computeApiBaseTemplate(), API_VERSION_SEGMENT)
const computeApiBasePath = (): string => toPath(API_BASE_URL)
const computeEffectiveApiBase = (): string =>
  typeof window === 'undefined' && API_BASE_URL.startsWith('/')
    ? `http://localhost${API_BASE_URL}`
    : API_BASE_URL

/**
 * Scheme and host the API is reached on, or the empty string when it is same-origin. Any path
 * prefix belongs to API_BASE_PATH and API_PATH_PREFIX, so callers can concatenate either onto
 * this without repeating a sub-path.
 */
const computeApiOrigin = (): string => {
  if (import.meta.env.DEV) return 'http://localhost:4545'
  if (!ABSOLUTE_URL_REGEX.test(API_BASE_URL)) return ''
  try {
    return new URL(API_BASE_URL).origin
  } catch {
    return ''
  }
}

/**
 * Path prefix the API host serves Listenarr under, with the '/api/vN' suffix removed. Non-API
 * endpoints that the backend mounts beside the API, such as the SignalR hubs, live under it.
 */
const computeApiPathPrefix = (): string => API_BASE_PATH.replace(API_SUFFIX_REGEX, '')

export let API_BASE_URL = computeApiBaseUrl()

export let API_BASE_PATH = computeApiBasePath()

export let EFFECTIVE_API_BASE = computeEffectiveApiBase()

export let API_ORIGIN = computeApiOrigin()

export let API_PATH_PREFIX = computeApiPathPrefix()

export let API_IMAGES_PATH_PREFIX = `${API_BASE_PATH}/images/`

const recomputeApiRuntimeValues = () => {
  API_BASE_URL = computeApiBaseUrl()
  API_BASE_PATH = computeApiBasePath()
  EFFECTIVE_API_BASE = computeEffectiveApiBase()
  API_ORIGIN = computeApiOrigin()
  API_PATH_PREFIX = computeApiPathPrefix()
  API_IMAGES_PATH_PREFIX = `${API_BASE_PATH}/images/`
}

export const setApiVersion = (versionLike: unknown): boolean => {
  const parsed =
    typeof versionLike === 'number'
      ? String(versionLike)
      : typeof versionLike === 'string'
        ? versionLike
        : ''
  const normalized = normalizeApiVersion(parsed)
  if (!normalized || normalized === API_VERSION) return false
  API_VERSION = normalized
  API_VERSION_SEGMENT = `v${API_VERSION}`
  recomputeApiRuntimeValues()
  return true
}

export const applyApiVersionFromStartupConfig = (startupConfig: unknown): boolean => {
  if (!startupConfig || typeof startupConfig !== 'object') return false
  const obj = startupConfig as Record<string, unknown>
  const value = obj.apiVersion ?? obj.ApiVersion
  return setApiVersion(value)
}

export const buildApiPath = (endpoint: string): string =>
  `${API_BASE_PATH}${normalizeEndpoint(endpoint)}`

export const isApiImagesUrl = (url: string): boolean =>
  /\/api(?:\/v\d+(?:\.\d+)?)?\/images\//i.test(url || '')
