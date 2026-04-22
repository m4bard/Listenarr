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
import { ref, onMounted, onUnmounted } from 'vue'
import * as signalR from '@microsoft/signalr'
import type { LogEntry } from '@/types'
import { sessionTokenManager } from '@/utils/sessionToken'
import { getStartupConfigCached } from '@/services/startupConfigCache'
import { logger } from '@/utils/logger'
import { API_BASE_PATH, API_ORIGIN } from '@/services/apiBase'

/**
 * Composable for real-time system logs via SignalR
 * Automatically connects on component mount and disconnects on unmount
 * This ensures the connection only exists when the component is active
 *
 * @param maxLogs - Maximum number of logs to keep in memory
 * @param autoConnect - Whether to automatically connect on mount (default: true)
 */
export function useSystemLogs(maxLogs = 100, autoConnect = true) {
  const logs = ref<LogEntry[]>([])
  const isConnected = ref(false)
  const isConnecting = ref(false)
  const connection = ref<signalR.HubConnection | null>(null)

  const connect = async () => {
    if (connection.value || isConnecting.value) return

    isConnecting.value = true

    try {
      // Get authentication token
      let accessToken = sessionTokenManager.getToken()

      // If no session token, try to get API key (for non-authenticated mode)
      if (!accessToken) {
        try {
          const sc = await getStartupConfigCached(2000)
          const apiKey = sc?.apiKey
          const rawAuth =
            sc?.authenticationRequired ??
            (sc as unknown as Record<string, unknown>)?.AuthenticationRequired
          const authEnabled =
            typeof rawAuth === 'boolean'
              ? rawAuth
              : typeof rawAuth === 'string'
                ? rawAuth.toLowerCase() === 'enabled' || rawAuth.toLowerCase() === 'true'
                : false

          if (apiKey && !authEnabled) {
            accessToken = apiKey
          }
        } catch (e) {
          logger.debug('[LogHub] Failed to get API key', e)
        }
      }

      const hubUrl = `${API_ORIGIN}/hubs/logs`

      // Create SignalR connection
      connection.value = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl, {
          transport:
            signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.ServerSentEvents,
          skipNegotiation: false,
          accessTokenFactory: () => accessToken || '',
        })
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000]) // Retry delays in ms
        .configureLogging(signalR.LogLevel.Information)
        .build()

      // Set up event handlers
      connection.value.on('ReceiveLog', (logEntry: LogEntry) => {
        console.log('[LogHub] Received log:', logEntry)
        // Add new log to the beginning of the array
        logs.value.unshift(logEntry)

        // Keep only the most recent logs
        if (logs.value.length > maxLogs) {
          logs.value = logs.value.slice(0, maxLogs)
        }
      })

      connection.value.onreconnecting(() => {
        isConnected.value = false
        console.log('[LogHub] Reconnecting...')
      })

      connection.value.onreconnected(() => {
        isConnected.value = true
        console.log('[LogHub] Reconnected')
      })

      connection.value.onclose(() => {
        isConnected.value = false
        console.log('[LogHub] Connection closed')
      })

      // Start the connection
      await connection.value.start()
      isConnected.value = true
      console.log('[LogHub] Connected successfully')

      // Load initial logs from API
      await loadInitialLogs()
    } catch (error) {
      console.error('[LogHub] Connection error:', error)
      isConnected.value = false
    } finally {
      isConnecting.value = false
    }
  }

  const disconnect = async () => {
    if (connection.value) {
      try {
        await connection.value.stop()
        connection.value = null
        isConnected.value = false
        console.log('[LogHub] Disconnected')
      } catch (error) {
        console.error('[LogHub] Disconnect error:', error)
      }
    }
  }

  const loadInitialLogs = async () => {
    try {
      const response = await fetch(`${API_ORIGIN}${API_BASE_PATH}/system/logs?limit=100`)
      if (response.ok) {
        const initialLogs = (await response.json()) as LogEntry[]
        // Sort by timestamp descending (newest first)
        logs.value = initialLogs.sort((a, b) => {
          const dateA = new Date(a.timestamp).getTime()
          const dateB = new Date(b.timestamp).getTime()
          return dateB - dateA
        })
      }
    } catch (error) {
      console.error('[LogHub] Failed to load initial logs:', error)
    }
  }

  const clearLogs = () => {
    logs.value = []
  }

  // Auto-connect on mount if enabled
  onMounted(() => {
    if (autoConnect) {
      connect()
    }
  })

  // Auto-disconnect on unmount
  onUnmounted(() => {
    disconnect()
  })

  return {
    logs,
    isConnected,
    isConnecting,
    connect,
    disconnect,
    clearLogs,
  }
}
