/**
 * Session token management utilities
 */

class SessionTokenManager {
  private static readonly STORAGE_KEY = 'listenarr_session_token'
  private static readonly PERSISTENCE_MODE_KEY = 'listenarr_session_token_persistence'
  private token: string | null = null
  private persistence: 'session' | 'local' = 'session'
  private subscribers: Set<(token: string | null) => void> = new Set()

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
        this.token = sessionToken
        this.persistence = 'session'
        try {
          localStorage.setItem(SessionTokenManager.PERSISTENCE_MODE_KEY, 'session')
        } catch {}
        return
      }

      const persistedToken = localStorage.getItem(SessionTokenManager.STORAGE_KEY)
      if (persistedToken) {
        this.token = persistedToken
        this.persistence = 'local'
        try {
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
    this.token = token
    try {
      if (token) {
        const mode =
          options?.persistent === true
            ? 'local'
            : options?.persistent === false
              ? 'session'
              : this.persistence

        this.persistence = mode
        if (mode === 'local') {
          localStorage.setItem(SessionTokenManager.STORAGE_KEY, token)
          sessionStorage.removeItem(SessionTokenManager.STORAGE_KEY)
        } else {
          sessionStorage.setItem(SessionTokenManager.STORAGE_KEY, token)
          localStorage.removeItem(SessionTokenManager.STORAGE_KEY)
        }
        localStorage.setItem(SessionTokenManager.PERSISTENCE_MODE_KEY, mode)
      } else {
        this.persistence = 'session'
        sessionStorage.removeItem(SessionTokenManager.STORAGE_KEY)
        localStorage.removeItem(SessionTokenManager.STORAGE_KEY)
        localStorage.removeItem(SessionTokenManager.PERSISTENCE_MODE_KEY)
      }
    } catch {
      // Storage might be unavailable
    }
    // Notify subscribers synchronously
    try {
      for (const cb of Array.from(this.subscribers)) cb(this.token)
    } catch {}
  }

  clearToken(): void {
    this.setToken(null)
  }

  hasToken(): boolean {
    return !!this.token
  }

  // Subscribe to token changes.
  onTokenChange(cb: (token: string | null) => void): () => void {
    this.subscribers.add(cb)
    // Call immediately with current value so subscribers have initial state
    try {
      cb(this.token)
    } catch {}
    return () => {
      this.subscribers.delete(cb)
    }
  }

  private handleStorageEvent = (ev: StorageEvent) => {
    try {
      if (!ev) return
      if (
        ev.key !== SessionTokenManager.STORAGE_KEY &&
        ev.key !== SessionTokenManager.PERSISTENCE_MODE_KEY
      ) {
        return
      }

      try {
        const localToken = localStorage.getItem(SessionTokenManager.STORAGE_KEY)
        if (localToken) {
          this.token = localToken
          this.persistence = 'local'
        } else {
          this.token = sessionStorage.getItem(SessionTokenManager.STORAGE_KEY)
          this.persistence = 'session'
        }
      } catch {
        this.token = null
        this.persistence = 'session'
      }

      for (const cb of Array.from(this.subscribers)) {
        try {
          cb(this.token)
        } catch {}
      }
    } catch {}
  }
}

export const sessionTokenManager = new SessionTokenManager()
