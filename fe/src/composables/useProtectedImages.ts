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
import { onUnmounted, reactive } from 'vue'
import { apiService } from '@/services/api'
import { isApiImagesUrl } from '@/services/apiBase'

export function isLikelyBackendImageUrl(url: string): boolean {
  if (!url) return false
  if (isApiImagesUrl(url)) return true
  if (url.startsWith('/config/cache/images/')) return true
  if (url.includes('/config/cache/images/')) return true
  return false
}

export function useProtectedImages() {
  const BACKEND_RETRY_COOLDOWN_MS = 30000
  const protectedImageSrcMap = reactive<Record<string, string>>({})
  const protectedImageError = reactive<Record<string, boolean>>({})
  const protectedImageErrorAt = reactive<Record<string, number>>({})
  const protectedImageSourceMap = reactive<Record<string, string>>({})
  const protectedImageObjectUrls = new Set<string>()

  function revokeProtectedImageUrl(url: string | undefined) {
    if (!url) return
    if (!url.startsWith('blob:')) return
    try {
      URL.revokeObjectURL(url)
    } catch {}
    try {
      protectedImageObjectUrls.delete(url)
    } catch {}
  }

  function resetProtectedImageKey(cacheKey: string) {
    const previous = protectedImageSrcMap[cacheKey]
    if (previous) revokeProtectedImageUrl(previous)
    delete protectedImageSrcMap[cacheKey]
    delete protectedImageError[cacheKey]
    delete protectedImageErrorAt[cacheKey]
  }

  function clearProtectedImages() {
    for (const objectUrl of Array.from(protectedImageObjectUrls)) {
      revokeProtectedImageUrl(objectUrl)
    }
    for (const key of Object.keys(protectedImageSrcMap)) {
      delete protectedImageSrcMap[key]
    }
    for (const key of Object.keys(protectedImageError)) {
      delete protectedImageError[key]
    }
    for (const key of Object.keys(protectedImageErrorAt)) {
      delete protectedImageErrorAt[key]
    }
    for (const key of Object.keys(protectedImageSourceMap)) {
      delete protectedImageSourceMap[key]
    }
  }

  function getProtectedImageSrc(rawImageUrl: string | undefined, cacheKey: string, fallback = ''): string {
    if (!rawImageUrl) return fallback
    const safeKey = (cacheKey || 'default').replace(/[^A-Za-z0-9._-]/g, '_')

    const previousSource = protectedImageSourceMap[safeKey]
    if (previousSource !== rawImageUrl) {
      protectedImageSourceMap[safeKey] = rawImageUrl
      resetProtectedImageKey(safeKey)
    }

    const existing = protectedImageSrcMap[safeKey]
    if (existing) return existing
    if (protectedImageError[safeKey]) {
      const retryResolved = apiService.getImageUrl(rawImageUrl)
      if (!retryResolved) return fallback
      const isBackendRetry = isLikelyBackendImageUrl(retryResolved)
      const lastFailure = protectedImageErrorAt[safeKey] ?? 0
      if (isBackendRetry && Date.now() - lastFailure < BACKEND_RETRY_COOLDOWN_MS) {
        return fallback
      }
      delete protectedImageError[safeKey]
      delete protectedImageErrorAt[safeKey]
    }

    const resolvedImmediate = apiService.getImageUrl(rawImageUrl)
    if (resolvedImmediate) {
      protectedImageSrcMap[safeKey] = resolvedImmediate
      delete protectedImageError[safeKey]
      delete protectedImageErrorAt[safeKey]
      return resolvedImmediate
    }

    return fallback
  }

  onUnmounted(() => {
    clearProtectedImages()
  })

  return {
    clearProtectedImages,
    getProtectedImageSrc,
    revokeProtectedImageUrl,
  }
}
