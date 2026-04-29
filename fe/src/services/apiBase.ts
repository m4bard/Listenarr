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
const API_BASE_TEMPLATE = import.meta.env.DEV
  ? DEFAULT_API_ROOT
  : import.meta.env.VITE_API_BASE_URL || DEFAULT_API_ROOT

const trimTrailingSlash = (value: string): string => value.replace(/\/+$/, '')

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
  buildVersionedApiBase(API_BASE_TEMPLATE, API_VERSION_SEGMENT)
const computeApiBasePath = (): string => toPath(API_BASE_URL)
const computeEffectiveApiBase = (): string =>
  typeof window === 'undefined' && API_BASE_URL.startsWith('/')
    ? `http://localhost${API_BASE_URL}`
    : API_BASE_URL
const computeApiOrigin = (): string =>
  import.meta.env.DEV
    ? 'http://localhost:4545'
    : API_BASE_URL.replace(/\/api(?:\/v\d+(?:\.\d+)?)?$/i, '')

export let API_BASE_URL = computeApiBaseUrl()

export let API_BASE_PATH = computeApiBasePath()

export let EFFECTIVE_API_BASE = computeEffectiveApiBase()

export let API_ORIGIN = computeApiOrigin()

export let API_IMAGES_PATH_PREFIX = `${API_BASE_PATH}/images/`

const recomputeApiRuntimeValues = () => {
  API_BASE_URL = computeApiBaseUrl()
  API_BASE_PATH = computeApiBasePath()
  EFFECTIVE_API_BASE = computeEffectiveApiBase()
  API_ORIGIN = computeApiOrigin()
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
