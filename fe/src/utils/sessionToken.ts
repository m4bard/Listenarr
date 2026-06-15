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
/**
 * Browser auth state utilities.
 *
 * The SPA no longer persists bearer credentials in browser storage. We keep
 * a lightweight marker so tabs can synchronize login/logout state without
 * storing a reusable secret client-side.
 */

class SessionTokenManager {
  private static readonly STORAGE_KEY = 'listenarr_session_token'
  private static readonly PERSISTENCE_MODE_KEY = 'listenarr_session_token_persistence'
  private static readonly EVENT_KEY = 'listenarr_session_event'
  private static readonly AUTH_MARKER = 'cookie-session'
  private token: string | null = null
  private persistence: 'session' | 'local' = 'session'
  private subscribers: Set<
    (token: string | null, context?: { source: 'initial' | 'local' | 'storage' }) => void
  > = new Set()

  constructor() {
    this.loadFromStorage()
    // Keep a listener for legacy localStorage migration scenarios.
    if (typeof window !== 'undefined' && window.addEventListener) {
      window.addEventListener('storage', this.handleStorageEvent)
    }
  }

  private loadFromStorage(): void {
    try {
      const sessionToken = sessionStorage.getItem(SessionTokenManager.STORAGE_KEY)
      if (sessionToken) {
        this.token = SessionTokenManager.AUTH_MARKER
        this.persistence = 'session'
        try {
          if (sessionToken !== SessionTokenManager.AUTH_MARKER) {
            sessionStorage.setItem(SessionTokenManager.STORAGE_KEY, SessionTokenManager.AUTH_MARKER)
          }
          localStorage.setItem(SessionTokenManager.PERSISTENCE_MODE_KEY, 'session')
        } catch {}
        return
      }

      const persistedToken = localStorage.getItem(SessionTokenManager.STORAGE_KEY)
      if (persistedToken) {
        this.token = SessionTokenManager.AUTH_MARKER
        this.persistence = 'local'
        try {
          if (persistedToken !== SessionTokenManager.AUTH_MARKER) {
            localStorage.setItem(SessionTokenManager.STORAGE_KEY, SessionTokenManager.AUTH_MARKER)
          }
          localStorage.setItem(SessionTokenManager.PERSISTENCE_MODE_KEY, 'local')
        } catch {}
        return
      }

      this.token = null
      this.persistence = 'session'
    } catch {
      this.token = null
      this.persistence = 'session'
    }
  }

  getToken(): string | null {
    return this.token
  }

  setToken(token: string | null, options?: { persistent?: boolean }): void {
    const marker = token ? SessionTokenManager.AUTH_MARKER : null
    this.token = marker
    try {
      if (marker) {
        const mode =
          options?.persistent === true
            ? 'local'
            : options?.persistent === false
              ? 'session'
              : this.persistence

        this.persistence = mode
        if (mode === 'local') {
          localStorage.setItem(SessionTokenManager.STORAGE_KEY, marker)
          sessionStorage.removeItem(SessionTokenManager.STORAGE_KEY)
        } else {
          sessionStorage.setItem(SessionTokenManager.STORAGE_KEY, marker)
          localStorage.removeItem(SessionTokenManager.STORAGE_KEY)
        }
        localStorage.setItem(SessionTokenManager.PERSISTENCE_MODE_KEY, mode)
      } else {
        this.persistence = 'session'
        sessionStorage.removeItem(SessionTokenManager.STORAGE_KEY)
        localStorage.removeItem(SessionTokenManager.STORAGE_KEY)
        localStorage.removeItem(SessionTokenManager.PERSISTENCE_MODE_KEY)
      }
      this.broadcastState(marker)
    } catch {
      // Storage might be unavailable
    }
    // Notify subscribers synchronously
    try {
      for (const cb of Array.from(this.subscribers)) cb(this.token, { source: 'local' })
    } catch {}
  }

  setAuthenticated(options?: { persistent?: boolean }): void {
    this.setToken(SessionTokenManager.AUTH_MARKER, options)
  }

  clearToken(): void {
    this.setToken(null)
  }

  hasToken(): boolean {
    return !!this.token
  }

  // Subscribe to token changes.
  onTokenChange(
    cb: (token: string | null, context?: { source: 'initial' | 'local' | 'storage' }) => void,
  ): () => void {
    this.subscribers.add(cb)
    // Call immediately with current value so subscribers have initial state
    try {
      cb(this.token, { source: 'initial' })
    } catch {}
    return () => {
      this.subscribers.delete(cb)
    }
  }

  private handleStorageEvent = (ev: StorageEvent) => {
    try {
      if (!ev) return
      if (ev.key === SessionTokenManager.EVENT_KEY) {
        try {
          const payload =
            typeof ev.newValue === 'string' && ev.newValue.length > 0
              ? (JSON.parse(ev.newValue) as { authenticated?: boolean })
              : null
          this.token = payload?.authenticated ? SessionTokenManager.AUTH_MARKER : null
        } catch {
          this.token = null
        }

        for (const cb of Array.from(this.subscribers)) {
          try {
            cb(this.token, { source: 'storage' })
          } catch {}
        }
        return
      }
      if (
        ev.key !== SessionTokenManager.STORAGE_KEY &&
        ev.key !== SessionTokenManager.PERSISTENCE_MODE_KEY
      ) {
        return
      }

      try {
        const localToken = localStorage.getItem(SessionTokenManager.STORAGE_KEY)
        if (localToken) {
          this.token = SessionTokenManager.AUTH_MARKER
          this.persistence = 'local'
          if (localToken !== SessionTokenManager.AUTH_MARKER) {
            localStorage.setItem(SessionTokenManager.STORAGE_KEY, SessionTokenManager.AUTH_MARKER)
          }
        } else {
          const sessionToken = sessionStorage.getItem(SessionTokenManager.STORAGE_KEY)
          this.token = sessionToken ? SessionTokenManager.AUTH_MARKER : null
          this.persistence = 'session'
          if (sessionToken && sessionToken !== SessionTokenManager.AUTH_MARKER) {
            sessionStorage.setItem(SessionTokenManager.STORAGE_KEY, SessionTokenManager.AUTH_MARKER)
          }
        }
      } catch {
        this.token = null
        this.persistence = 'session'
      }

      for (const cb of Array.from(this.subscribers)) {
        try {
          cb(this.token, { source: 'storage' })
        } catch {}
      }
    } catch {}
  }

  private broadcastState(marker: string | null): void {
    try {
      localStorage.setItem(
        SessionTokenManager.EVENT_KEY,
        JSON.stringify({
          authenticated: !!marker,
          at: Date.now(),
        }),
      )
    } catch {}
  }
}

export const sessionTokenManager = new SessionTokenManager()
