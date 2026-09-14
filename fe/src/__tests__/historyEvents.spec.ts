/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
import { describe, it, expect } from 'vitest'
import { PhCircle } from '@phosphor-icons/vue'
import {
  HISTORY_EVENT_PRESETS,
  findEventPreset,
  formatEventTitle,
  getEventIconComponent,
  getEventTypeClass,
  getHistoryEventStyle,
  humanizeEventType,
  styledEventTypes,
} from '@/utils/historyEvents'

/*
 * The event types below are transcribed from the producers, not imported from the module
 * under test, so that the two statements stay independent. Importing the module's own list
 * would make every assertion here tautological, which is exactly how the map this replaces
 * came to carry four keys (Downloaded, Monitored, Unmonitored, Failed) that nothing writes.
 *
 * PASCAL_CASE_EVENTS is HistoryEvents in listenarr.domain/ActivityHistory/HistoryEvents.cs.
 * LITERAL_EVENTS is every bare string assigned to EventType elsewhere in the tree, found
 * with: grep -rn 'EventType = "' listenarr.* | grep -v Migrations
 */
const PASCAL_CASE_EVENTS = [
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
]

const LITERAL_EVENTS = [
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
]

const ALL_EVENTS = [...PASCAL_CASE_EVENTS, ...LITERAL_EVENTS]

describe('historyEvents map coverage', () => {
  it('styles every event type a producer actually writes', () => {
    const unstyled = ALL_EVENTS.filter(
      (eventType) => getHistoryEventStyle(eventType).icon === PhCircle,
    )
    expect(unstyled).toEqual([])
  })

  it('gives every real event type a label that is not the raw constant', () => {
    const shouted = ALL_EVENTS.filter((eventType) => {
      const label = formatEventTitle(eventType)
      return !label || /[a-z][A-Z]/.test(label)
    })
    expect(shouted).toEqual([])
  })

  it('gives every real event type a severity class the views already style', () => {
    const known = ['event-success', 'event-info', 'event-warning', 'event-danger', 'event-default']
    for (const eventType of ALL_EVENTS) {
      expect(known).toContain(getEventTypeClass(eventType))
    }
  })

  // The direct regression test for the defect this module replaces. A key for an event no
  // producer writes is dead decoration, and it hides the fact that real events are unstyled.
  it('carries no entry for an event type nothing writes', () => {
    const orphans = styledEventTypes().filter((eventType) => !ALL_EVENTS.includes(eventType))
    expect(orphans).toEqual([])
  })

  it('covers the whole vocabulary and nothing else', () => {
    expect(styledEventTypes().slice().sort()).toEqual(ALL_EVENTS.slice().sort())
  })

  // Named individually because these four were keys on the old map and are the reason it
  // was wrong. Re-adding one should fail here rather than pass quietly.
  it.each(['Downloaded', 'Monitored', 'Unmonitored', 'Failed'])(
    'does not resurrect the dead key %s',
    (deadKey) => {
      expect(styledEventTypes()).not.toContain(deadKey)
    },
  )
})

describe('historyEvents fallback', () => {
  it('falls back to a generic icon and a default class for an unknown type', () => {
    expect(getEventIconComponent('SomethingNobodyWritesYet')).toBe(PhCircle)
    expect(getEventTypeClass('SomethingNobodyWritesYet')).toBe('event-default')
  })

  it('renders an unknown PascalCase type as readable words, not raw PascalCase', () => {
    expect(formatEventTitle('SomethingNobodyWritesYet')).toBe('Something nobody writes yet')
  })

  it('leaves an unknown spaced type alone apart from its capitalisation', () => {
    expect(humanizeEventType('Some New Thing')).toBe('Some new thing')
  })

  it('does not return undefined or an empty label for an empty event type', () => {
    expect(formatEventTitle('')).toBe('Unknown event')
    expect(getHistoryEventStyle('').icon).toBe(PhCircle)
  })
})

describe('historyEvents presets', () => {
  it('opens with an unfiltered preset', () => {
    expect(HISTORY_EVENT_PRESETS[0].id).toBe('all')
    expect(HISTORY_EVENT_PRESETS[0].eventTypes).toEqual([])
  })

  it('only groups event types the map knows about', () => {
    for (const preset of HISTORY_EVENT_PRESETS) {
      for (const eventType of preset.eventTypes) {
        expect(ALL_EVENTS).toContain(eventType)
      }
    }
  })

  it('does not put one event type in two presets', () => {
    const grouped = HISTORY_EVENT_PRESETS.flatMap((preset) => preset.eventTypes)
    expect(grouped.length).toBe(new Set(grouped).size)
  })

  it('falls back to the unfiltered preset for an unknown id', () => {
    expect(findEventPreset('no-such-preset').id).toBe('all')
    expect(findEventPreset('files').eventTypes).toContain('File Added')
  })
})
