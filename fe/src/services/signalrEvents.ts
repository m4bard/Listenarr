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
import { ref } from 'vue'

// Reactive state for SignalR connection diagnostics and UI
export const isConnected = ref<boolean>(false)
export const lastError = ref<string | null>(null)
export const reconnectAttempts = ref<number>(0)

const connectedListeners: Set<() => void> = new Set()
const disconnectedListeners: Set<() => void> = new Set()

export function onConnected(cb: () => void): () => void {
  connectedListeners.add(cb)
  return () => {
    connectedListeners.delete(cb)
  }
}

export function onDisconnected(cb: () => void): () => void {
  disconnectedListeners.add(cb)
  return () => {
    disconnectedListeners.delete(cb)
  }
}

export function setConnected(val: boolean) {
  isConnected.value = val
  try {
    if (val) {
      for (const cb of Array.from(connectedListeners)) {
        try {
          cb()
        } catch {}
      }
    } else {
      for (const cb of Array.from(disconnectedListeners)) {
        try {
          cb()
        } catch {}
      }
    }
  } catch {}
}

export function setLastError(err: string | null) {
  lastError.value = err
}

export function setReconnectAttempts(n: number) {
  reconnectAttempts.value = n
}

export default {
  isConnected,
  lastError,
  reconnectAttempts,
  onConnected,
  onDisconnected,
  setConnected,
  setLastError,
  setReconnectAttempts,
}
