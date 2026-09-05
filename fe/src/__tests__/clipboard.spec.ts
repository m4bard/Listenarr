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
import { describe, it, expect, vi, afterEach } from 'vitest'
import { copyTextToClipboard } from '@/utils/clipboard'

type ExecCommandHost = { execCommand?: (command: string) => boolean }

const setExecCommand = (impl: ((command: string) => boolean) | undefined) => {
  const host = document as unknown as ExecCommandHost
  if (impl) {
    host.execCommand = impl
  } else {
    delete host.execCommand
  }
}

afterEach(() => {
  vi.unstubAllGlobals()
  setExecCommand(undefined)
})

describe('copyTextToClipboard', () => {
  it('uses the async Clipboard API when the origin is secure', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    vi.stubGlobal('navigator', { clipboard: { writeText } })
    const execCommand = vi.fn(() => true)
    setExecCommand(execCommand)

    await expect(copyTextToClipboard('SECRET')).resolves.toBe('clipboard-api')
    expect(writeText).toHaveBeenCalledWith('SECRET')
    expect(execCommand).not.toHaveBeenCalled()
  })

  // The plain-HTTP shape: on a non-secure origin navigator.clipboard is not
  // merely unusable, it is absent, so the property access is what fails.
  it('falls back to execCommand when navigator.clipboard is undefined', async () => {
    vi.stubGlobal('navigator', {})
    // Capture what the document would actually have copied: execCommand('copy')
    // takes the current selection, so a scratch element that is not focused and
    // not selected copies nothing at all.
    let selectedText: string | null = null
    let focused = false
    setExecCommand(
      vi.fn(() => {
        const scratch = document.querySelector('textarea')
        if (!scratch) return false
        focused = document.activeElement === scratch
        selectedText = scratch.value.slice(scratch.selectionStart ?? 0, scratch.selectionEnd ?? 0)
        return true
      }),
    )

    await expect(copyTextToClipboard('SECRET')).resolves.toBe('exec-command')
    expect(focused).toBe(true)
    expect(selectedText).toBe('SECRET')
  })

  it('falls back to execCommand when the Clipboard API rejects', async () => {
    const writeText = vi.fn().mockRejectedValue(new Error('NotAllowedError'))
    vi.stubGlobal('navigator', { clipboard: { writeText } })
    const execCommand = vi.fn(() => true)
    setExecCommand(execCommand)

    await expect(copyTextToClipboard('SECRET')).resolves.toBe('exec-command')
    expect(writeText).toHaveBeenCalled()
    expect(execCommand).toHaveBeenCalledWith('copy')
  })

  it('reports failure when neither path is available', async () => {
    vi.stubGlobal('navigator', {})
    setExecCommand(undefined)

    await expect(copyTextToClipboard('SECRET')).resolves.toBe('failed')
  })

  it('reports failure when execCommand declines the copy', async () => {
    vi.stubGlobal('navigator', {})
    setExecCommand(vi.fn(() => false))

    await expect(copyTextToClipboard('SECRET')).resolves.toBe('failed')
  })

  it('leaves no scratch textarea behind, on either outcome', async () => {
    vi.stubGlobal('navigator', {})

    setExecCommand(vi.fn(() => true))
    await copyTextToClipboard('SECRET')
    expect(document.querySelectorAll('textarea')).toHaveLength(0)

    setExecCommand(
      vi.fn(() => {
        throw new Error('boom')
      }),
    )
    await expect(copyTextToClipboard('SECRET')).resolves.toBe('failed')
    expect(document.querySelectorAll('textarea')).toHaveLength(0)
  })
})
