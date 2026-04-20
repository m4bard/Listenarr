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
import type { QueueSnapshot, QueueUpdatePayload } from '@/types'

export const createEmptyQueueSnapshot = (): QueueSnapshot => ({
  items: [],
  clients: [],
  generatedAt: new Date(0).toISOString(),
  hasStaleData: false,
  hasUnavailableClients: false,
})

export const normalizeQueueSnapshot = (
  payload: QueueUpdatePayload | null | undefined,
): QueueSnapshot => {
  if (!payload) {
    return createEmptyQueueSnapshot()
  }

  if (Array.isArray(payload)) {
    return {
      items: payload,
      clients: [],
      generatedAt: new Date().toISOString(),
      hasStaleData: payload.some((item) => item.isStaleSnapshot),
      hasUnavailableClients: false,
    }
  }

  return {
    items: Array.isArray(payload.items) ? payload.items : [],
    clients: Array.isArray(payload.clients) ? payload.clients : [],
    generatedAt:
      typeof payload.generatedAt === 'string' && payload.generatedAt.length > 0
        ? payload.generatedAt
        : new Date().toISOString(),
    hasStaleData:
      typeof payload.hasStaleData === 'boolean'
        ? payload.hasStaleData
        : Array.isArray(payload.items)
          ? payload.items.some((item) => item.isStaleSnapshot)
          : false,
    hasUnavailableClients:
      typeof payload.hasUnavailableClients === 'boolean'
        ? payload.hasUnavailableClients
        : Array.isArray(payload.clients)
          ? payload.clients.some((client) => client.isUnavailable)
          : false,
  }
}
