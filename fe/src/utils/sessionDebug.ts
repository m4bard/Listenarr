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
 * Session debugging utilities to help diagnose authentication and loading issues
 */

import { sessionTokenManager } from './sessionToken'

declare global {
  interface Window {
    __debugSession?: {
      logState: (context?: string) => void
      clearAuth: () => void
    }
  }
}

export const logSessionState = (context: string = 'Unknown') => {
  try {
    console.group(`[Session Debug] ${context}`)

    // Check localStorage
    const storedToken = localStorage.getItem('listenarr_session_token')
    console.log(
      'Stored session token:',
      storedToken ? `${storedToken.substring(0, 10)}...` : 'None',
    )

    // Check sessionTokenManager
    const managerToken = sessionTokenManager.getToken()
    console.log(
      'Session manager token:',
      managerToken ? `${managerToken.substring(0, 10)}...` : 'None',
    )
    console.log('Has valid token:', sessionTokenManager.hasToken())

    // Note: Authentication uses Bearer tokens, not cookies (cookies only used for CSRF)

    // Check sessionStorage
    const sessionItems: string[] = []
    try {
      for (let i = 0; i < sessionStorage.length; i++) {
        const key = sessionStorage.key(i)
        if (key && (key.includes('auth') || key.includes('session') || key.includes('listenarr'))) {
          sessionItems.push(key)
        }
      }
    } catch {}
    console.log('Session storage items:', sessionItems.length > 0 ? sessionItems : 'None')

    // Browser info
    console.log('User Agent:', navigator.userAgent)
    console.log('Current URL:', window.location.href)
    console.log('Cookies enabled:', navigator.cookieEnabled)

    console.groupEnd()
  } catch (error) {
    console.error('[Session Debug] Error logging session state:', error)
  }
}

export const clearAllAuthData = () => {
  try {
    console.log('[Session Debug] Clearing all authentication data...')

    // Clear session token manager
    sessionTokenManager.clearToken()

    // Clear localStorage
    try {
      localStorage.removeItem('listenarr_session_token')
      localStorage.removeItem('auth_token')
      console.log('[Session Debug] LocalStorage cleared')
    } catch (error) {
      console.warn('[Session Debug] Could not clear localStorage:', error)
    }

    // Clear sessionStorage
    try {
      sessionStorage.removeItem('listenarr_session_token')
      sessionStorage.removeItem('auth_token')
      sessionStorage.removeItem('listenarr_pending_redirect')
      console.log('[Session Debug] SessionStorage cleared')
    } catch (error) {
      console.warn('[Session Debug] Could not clear sessionStorage:', error)
    }

    // Note: Authentication uses Bearer tokens, not cookies (CSRF cookies will expire naturally)

    console.log('[Session Debug] All authentication data cleared')
  } catch (error) {
    console.error('[Session Debug] Error clearing auth data:', error)
  }
}

// Make debugging functions available globally in development
if (import.meta.env.DEV) {
  window.__debugSession = {
    logState: logSessionState,
    clearAuth: clearAllAuthData,
  }
  console.log(
    '[Session Debug] Global debug functions available: window.__debugSession.logState() and window.__debugSession.clearAuth()',
  )
}
