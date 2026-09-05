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
 * How a copy attempt ended, so callers can tell "the text is on the clipboard"
 * from "the browser refused and the user needs another way to get it".
 */
export type ClipboardOutcome = 'clipboard-api' | 'exec-command' | 'failed'

/**
 * Copy text to the clipboard, falling back when the async Clipboard API is absent.
 *
 * `navigator.clipboard` is only defined in a secure context, which means HTTPS or
 * localhost. A self-hosted instance reached over plain HTTP at a LAN address or
 * hostname has no `navigator.clipboard` at all, so the property access itself
 * throws. That is the common deployment shape, not an edge case, which is why
 * this falls back to `document.execCommand('copy')` rather than just reporting
 * failure.
 */
export async function copyTextToClipboard(text: string): Promise<ClipboardOutcome> {
  const clipboard = typeof navigator === 'undefined' ? undefined : navigator.clipboard

  if (clipboard && typeof clipboard.writeText === 'function') {
    try {
      await clipboard.writeText(text)
      return 'clipboard-api'
    } catch {
      // Permission denied, or a document that is not focused. Fall through and
      // try execCommand, which is subject to different rules.
    }
  }

  return copyViaExecCommand(text) ? 'exec-command' : 'failed'
}

/**
 * The pre-Clipboard-API copy path: put the text in an off-screen textarea, select
 * it, and ask the document to copy the selection.
 *
 * Deprecated but implemented everywhere, and unlike the async API it works on a
 * non-secure origin. It does require an active user gesture, so it is reliable on
 * the path where `navigator.clipboard` was missing (nothing has been awaited yet,
 * so the click is still in progress) and best-effort on the path where the async
 * API was tried first.
 */
function copyViaExecCommand(text: string): boolean {
  if (typeof document === 'undefined' || typeof document.execCommand !== 'function') {
    return false
  }

  const previouslyFocused = document.activeElement as HTMLElement | null
  const textarea = document.createElement('textarea')
  textarea.value = text
  textarea.setAttribute('readonly', '')
  // Off-screen rather than hidden: a display:none or visibility:hidden element
  // cannot hold a selection, and scrolling to a fixed element does not move the page.
  textarea.style.position = 'fixed'
  textarea.style.top = '0'
  textarea.style.left = '-9999px'
  textarea.style.opacity = '0'
  document.body.appendChild(textarea)

  try {
    // Focus explicitly. execCommand('copy') copies the document selection, and a
    // selection in an unfocused element is not the one the document will copy.
    textarea.focus()
    textarea.select()
    textarea.setSelectionRange(0, text.length)
    return document.execCommand('copy') === true
  } catch {
    return false
  } finally {
    textarea.remove()
    previouslyFocused?.focus?.()
  }
}
