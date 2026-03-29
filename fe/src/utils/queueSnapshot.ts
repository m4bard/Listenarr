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
