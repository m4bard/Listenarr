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

type StartupBootstrapConfig = import('@/types').StartupBootstrapConfig

let _cache: StartupBootstrapConfig | null = null
let _cacheTs = 0
let _inflight: Promise<StartupBootstrapConfig | null> | null = null
// Expose a simple counter for diagnostics/tests
export let fetchCount = 0

export async function getStartupConfigCached(ttlMs = 5000): Promise<StartupBootstrapConfig | null> {
  const now = Date.now()
  if (_cacheTs !== 0 && now - _cacheTs <= ttlMs) return _cache

  if (!_inflight) {
    fetchCount++
    _inflight = apiService
      .getBootstrapConfig()
      .then((cfg) => {
        logger.debug('[startupConfigCache] Raw bootstrap response:', cfg)
        applyApiVersionFromStartupConfig(cfg)
        _cache = cfg
        _cacheTs = Date.now()
        return cfg
      })
      .catch((err) => {
        logger.debug('[startupConfigCache] Error fetching bootstrap config:', err)
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
export function getCachedStartupConfig(): StartupBootstrapConfig | null {
  return _cache
}
