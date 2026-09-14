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
import type { Component } from 'vue'
import {
  PhArrowsClockwise,
  PhArrowsLeftRight,
  PhBooks,
  PhBroom,
  PhCheckCircle,
  PhCircle,
  PhCopy,
  PhDownloadSimple,
  PhFileMinus,
  PhFilePlus,
  PhFolderSimple,
  PhFolders,
  PhHandGrabbing,
  PhMagnifyingGlass,
  PhMinusCircle,
  PhPause,
  PhPencil,
  PhPlay,
  PhPlusCircle,
  PhProhibit,
  PhShieldCheck,
  PhSkipForward,
  PhSwap,
  PhTextAa,
  PhTrash,
  PhUpload,
  PhUploadSimple,
  PhWarning,
  PhWarningCircle,
  PhXCircle,
} from '@phosphor-icons/vue'

/**
 * One description per history event type: the icon to draw, the severity class the
 * surrounding chrome colours itself with, and the sentence-case label a human reads.
 *
 * `History.EventType` is a free-text column on the server and two separate vocabularies
 * write into it. `HistoryEvents` (listenarr.domain/ActivityHistory/HistoryEvents.cs)
 * declares PascalCase constants; a dozen more call sites write bare literals, several of
 * them with spaces. Neither list is a subset of the other, so a map that wants to cover
 * what actually lands in the column has to carry both. `HISTORY_EVENT_TYPES` below is that
 * union, and it is the list the spec asserts against so a new producer cannot be added
 * without this file noticing.
 */
export type HistoryEventKind = 'success' | 'info' | 'warning' | 'danger' | 'default'

export interface HistoryEventStyle {
  icon: Component
  kind: HistoryEventKind
  label: string
}

/**
 * Written by `HistoryEvents` in the domain layer. Kept in declaration order so a diff
 * against the C# file reads straight down.
 */
export const HISTORY_EVENT_CONSTANTS = [
  'Grabbed',
  'Downloading',
  'DownloadCompleted',
  'DownloadFailed',
  'ImportStarted',
  'Imported',
  'ImportFailed',
  'ImportRetry',
  'CleanupRequested',
  'CleanupSucceeded',
  'CleanupFailed',
  'ScanQueued',
  'ScanCompleted',
  'ScanFailed',
  'FileMoved',
  'FileCopied',
  'FileDeleted',
  'FileSkipped',
  'Renamed',
  'LibraryUpdated',
  'LibraryDeleted',
  'Paused',
  'Resumed',
  'Removed',
  'Checking',
  'Warning',
] as const

/**
 * Written as bare string literals at the call site rather than through `HistoryEvents`.
 * Several contain spaces, which is why an exact-match filter has to carry them verbatim.
 */
export const HISTORY_EVENT_LITERALS = [
  'Added',
  'Deleted',
  'Updated',
  'File Association Refused',
  'File Added',
  'Organized',
  'MoveFailed',
  'Scan Incomplete',
  'File Replaced',
  'File Removed',
  'Moved',
  'Root Folder Policy Changed',
] as const

/** Every event type a producer in the tree actually writes. */
export const HISTORY_EVENT_TYPES: readonly string[] = [
  ...HISTORY_EVENT_CONSTANTS,
  ...HISTORY_EVENT_LITERALS,
]

const HISTORY_EVENT_STYLES: Record<string, HistoryEventStyle> = {
  // Download lifecycle
  Grabbed: { icon: PhHandGrabbing, kind: 'info', label: 'Grabbed' },
  Downloading: { icon: PhDownloadSimple, kind: 'info', label: 'Downloading' },
  DownloadCompleted: { icon: PhCheckCircle, kind: 'success', label: 'Download completed' },
  DownloadFailed: { icon: PhXCircle, kind: 'danger', label: 'Download failed' },
  Paused: { icon: PhPause, kind: 'warning', label: 'Paused' },
  Resumed: { icon: PhPlay, kind: 'info', label: 'Resumed' },
  Removed: { icon: PhMinusCircle, kind: 'warning', label: 'Removed from client' },
  Checking: { icon: PhShieldCheck, kind: 'info', label: 'Checking' },

  // Import lifecycle
  ImportStarted: { icon: PhUploadSimple, kind: 'info', label: 'Import started' },
  Imported: { icon: PhUpload, kind: 'success', label: 'Imported' },
  ImportFailed: { icon: PhWarningCircle, kind: 'danger', label: 'Import failed' },
  ImportRetry: { icon: PhArrowsClockwise, kind: 'warning', label: 'Import retried' },

  // Cleanup
  CleanupRequested: { icon: PhBroom, kind: 'info', label: 'Cleanup requested' },
  CleanupSucceeded: { icon: PhBroom, kind: 'success', label: 'Cleanup succeeded' },
  CleanupFailed: { icon: PhBroom, kind: 'danger', label: 'Cleanup failed' },

  // Scanning
  ScanQueued: { icon: PhMagnifyingGlass, kind: 'info', label: 'Scan queued' },
  ScanCompleted: { icon: PhMagnifyingGlass, kind: 'success', label: 'Scan completed' },
  ScanFailed: { icon: PhMagnifyingGlass, kind: 'danger', label: 'Scan failed' },
  'Scan Incomplete': { icon: PhWarning, kind: 'warning', label: 'Scan incomplete' },

  // Files
  FileMoved: { icon: PhArrowsLeftRight, kind: 'info', label: 'File moved' },
  FileCopied: { icon: PhCopy, kind: 'info', label: 'File copied' },
  FileDeleted: { icon: PhFileMinus, kind: 'danger', label: 'File deleted' },
  FileSkipped: { icon: PhSkipForward, kind: 'warning', label: 'File skipped' },
  'File Added': { icon: PhFilePlus, kind: 'success', label: 'File added' },
  'File Removed': { icon: PhFileMinus, kind: 'warning', label: 'File removed' },
  'File Replaced': { icon: PhSwap, kind: 'info', label: 'File replaced' },
  'File Association Refused': {
    icon: PhProhibit,
    kind: 'warning',
    label: 'File association refused',
  },
  Renamed: { icon: PhTextAa, kind: 'info', label: 'Renamed' },
  Organized: { icon: PhFolders, kind: 'info', label: 'Organized' },
  Moved: { icon: PhArrowsLeftRight, kind: 'info', label: 'Moved' },
  MoveFailed: { icon: PhWarningCircle, kind: 'danger', label: 'Move failed' },

  // Library
  Added: { icon: PhPlusCircle, kind: 'success', label: 'Added to library' },
  Updated: { icon: PhPencil, kind: 'info', label: 'Updated' },
  Deleted: { icon: PhTrash, kind: 'danger', label: 'Deleted from library' },
  LibraryUpdated: { icon: PhBooks, kind: 'info', label: 'Library updated' },
  LibraryDeleted: { icon: PhBooks, kind: 'danger', label: 'Library deleted' },

  // System
  'Root Folder Policy Changed': {
    icon: PhFolderSimple,
    kind: 'warning',
    label: 'Root folder policy changed',
  },
  Warning: { icon: PhWarning, kind: 'warning', label: 'Warning' },
}

const FALLBACK_ICON = PhCircle
const FALLBACK_KIND: HistoryEventKind = 'default'

/**
 * Turn an unmapped event type into something readable rather than echoing raw PascalCase.
 * `DownloadFailed` becomes `Download failed`; a value that already has spaces is left as
 * written apart from its capitalisation.
 */
export function humanizeEventType(eventType: string): string {
  const trimmed = (eventType ?? '').trim()
  if (!trimmed) return 'Unknown event'
  const spaced = trimmed.includes(' ') ? trimmed : trimmed.replace(/([a-z0-9])([A-Z])/g, '$1 $2')
  const lowered = spaced.toLowerCase()
  return lowered.charAt(0).toUpperCase() + lowered.slice(1)
}

/** The full description for an event type, falling back for anything unmapped. */
export function getHistoryEventStyle(eventType: string): HistoryEventStyle {
  return (
    HISTORY_EVENT_STYLES[eventType] ?? {
      icon: FALLBACK_ICON,
      kind: FALLBACK_KIND,
      label: humanizeEventType(eventType),
    }
  )
}

/** The icon component for an event type. */
export function getEventIconComponent(eventType: string): Component {
  return getHistoryEventStyle(eventType).icon
}

/**
 * The CSS class the existing surfaces key their colours off. Kept in the
 * `event-<kind>` shape `AudiobookDetailView` already styles.
 */
export function getEventTypeClass(eventType: string): string {
  return `event-${getHistoryEventStyle(eventType).kind}`
}

/** The human label for an event type. */
export function formatEventTitle(eventType: string): string {
  return getHistoryEventStyle(eventType).label
}

/** Every event type this module has an explicit entry for. Used by the spec. */
export function styledEventTypes(): string[] {
  return Object.keys(HISTORY_EVENT_STYLES)
}

export interface HistoryEventPreset {
  /** Stable value for the `<select>` and for the query string. */
  id: string
  label: string
  /** Exact event-type strings this preset matches. Empty means no filter. */
  eventTypes: readonly string[]
}

/**
 * Groups of event types offered as a filter, in place of a flat dropdown of every raw
 * string the column can hold. The server takes a comma-separated `eventType`, so a preset
 * covering several types is still one request.
 */
export const HISTORY_EVENT_PRESETS: readonly HistoryEventPreset[] = [
  { id: 'all', label: 'All events', eventTypes: [] },
  { id: 'grabbed', label: 'Grabbed', eventTypes: ['Grabbed'] },
  {
    id: 'downloading',
    label: 'Downloading',
    eventTypes: ['Downloading', 'Checking', 'Paused', 'Resumed'],
  },
  {
    id: 'download-failed',
    label: 'Download failed',
    eventTypes: ['DownloadFailed', 'Removed'],
  },
  { id: 'imported', label: 'Imported', eventTypes: ['Imported', 'DownloadCompleted'] },
  {
    id: 'import-failed',
    label: 'Import failed',
    eventTypes: ['ImportFailed', 'ImportRetry'],
  },
  {
    id: 'library',
    label: 'Library',
    eventTypes: ['Added', 'Updated', 'Deleted', 'LibraryUpdated', 'LibraryDeleted'],
  },
  {
    id: 'files',
    label: 'Files',
    eventTypes: [
      'File Added',
      'File Removed',
      'File Replaced',
      'File Association Refused',
      'FileMoved',
      'FileCopied',
      'FileDeleted',
      'FileSkipped',
      'Organized',
      'Renamed',
      'Moved',
      'MoveFailed',
    ],
  },
  {
    id: 'scan',
    label: 'Scan',
    eventTypes: ['ScanQueued', 'ScanCompleted', 'ScanFailed', 'Scan Incomplete'],
  },
]

/** The preset for an id, falling back to "All events" for anything unrecognised. */
export function findEventPreset(id: string): HistoryEventPreset {
  return HISTORY_EVENT_PRESETS.find((preset) => preset.id === id) ?? HISTORY_EVENT_PRESETS[0]
}
