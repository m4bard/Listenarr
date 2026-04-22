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
import { apiService } from './api'
import { logger } from '@/utils/logger'
import { applyApiVersionFromStartupConfig } from './apiBase'

type StartupConfig = import('@/types').StartupConfig

let _cache: StartupConfig | null = null
let _cacheTs = 0
let _inflight: Promise<StartupConfig | null> | null = null
// Expose a simple counter for diagnostics/tests
export let fetchCount = 0

export async function getStartupConfigCached(ttlMs = 5000): Promise<StartupConfig | null> {
  const now = Date.now()
  // If we have a cached value, check if it's a 401 fallback and use a longer TTL
  const cacheObj = _cache as Record<string, unknown> | null
  const isAuthRequired = !!(cacheObj && cacheObj.authenticationRequired === true)
  const effectiveTtl = isAuthRequired ? 300000 : ttlMs // 5 minutes for 401, else normal TTL
  if (_cacheTs !== 0 && now - _cacheTs <= effectiveTtl) return _cache

  if (!_inflight) {
    fetchCount++
    _inflight = apiService
      .getStartupConfig()
      .then((cfg) => {
        const cfgForLog =
          cfg && typeof cfg === 'object'
            ? (() => {
                const cloned = { ...(cfg as Record<string, unknown>) }
                if (typeof cloned.apiKey === 'string' && cloned.apiKey.length > 0) cloned.apiKey = 'redacted'
                if (typeof cloned.ApiKey === 'string' && cloned.ApiKey.length > 0) cloned.ApiKey = 'redacted'
                return cloned
              })()
            : cfg
        logger.debug('[startupConfigCache] Raw config response:', cfgForLog)
        applyApiVersionFromStartupConfig(cfg)
        _cache = cfg
        _cacheTs = Date.now()
        return cfg
      })
      .catch((err) => {
        logger.debug('[startupConfigCache] Error fetching config:', err)
        // If 401 Unauthorized, treat as 'authentication required' for SPA logic
        const status = (err as { status?: number } | null)?.status
        if (status === 401) {
          const fallback: Partial<StartupConfig> = { authenticationRequired: true }
          _cache = fallback as StartupConfig
          _cacheTs = Date.now()
          return _cache
        }
        // On other errors, cache null result for the TTL
        _cache = null
        _cacheTs = Date.now()
        return null
      })
      .finally(() => {
        _inflight = null
      })
  }

  return _inflight
}

export function resetCache() {
  _cache = null
  _cacheTs = 0
  _inflight = null
  fetchCount = 0
}

// Synchronous access to the cached startup config (may be null)
export function getCachedStartupConfig(): StartupConfig | null {
  return _cache
}
