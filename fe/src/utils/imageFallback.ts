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
import { getPlaceholderUrl } from '@/utils/placeholder'

export function handleImageError(ev: Event) {
  try {
    const img = ev?.target as HTMLImageElement | null
    if (!img) return

    // Prevent loops
    try {
      const imgRec = img as unknown as Record<string, unknown>
      if (imgRec.__imageFallbackDone) return
      imgRec.__imageFallbackDone = true
    } catch {}

    // Set placeholder
    try {
      img.src = getPlaceholderUrl()
    } catch {}
    try {
      ;(img as unknown as { onerror?: ((ev: Event) => void) | null }).onerror = null
    } catch {}
  } catch {}
}
