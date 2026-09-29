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

// Series identity (#953). A series is identified by its SeriesAsin where a membership carries one,
// and by its normalized display name only where none does. The library grid and the series page
// must agree on that identity, and the series page must hand it to the metadata lookup instead of
// letting the lookup re-resolve the series from its name.
//
// The fixtures are two distinct series that share one display name (an original series and a
// translated edition series published under the same title), which is exactly the case a
// name-keyed grid collapses into one tile and a name-keyed page resolves to whichever series the
// name lookup happens to find first. The identifiers are synthetic, not real catalogue ASINs.

import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory, type Router } from 'vue-router'
import AudiobooksView from '@/views/library/AudiobooksView.vue'
import CollectionView from '@/views/library/CollectionView.vue'
import { useLibraryStore } from '@/stores/library'
import type { Audiobook, AudiobookSeriesMembership } from '@/types'

const { mockGetSeriesCatalog, mockGetSeriesLookup } = vi.hoisted(() => ({
  mockGetSeriesCatalog: vi.fn(async () => null as unknown),
  mockGetSeriesLookup: vi.fn(async () => null as unknown),
}))

vi.mock('@/services/api', () => ({
  apiService: {
    getImageUrl: vi.fn((url: string) => url || 'https://via.placeholder.com/300x450?text=No+Image'),
    getBootstrapConfig: vi.fn(async () => ({})),
    getStartupConfig: vi.fn(async () => ({})),
    getApplicationSettings: vi.fn(async () => ({})),
    getQualityProfiles: vi.fn(async () => []),
    getLibrary: vi.fn(async () => []),
    getAuthorCatalog: vi.fn(async () => null),
    getAuthorLookup: vi.fn(async () => null),
    getSeriesCatalog: mockGetSeriesCatalog,
    getSeriesLookup: mockGetSeriesLookup,
    getAuthorMonitoringStatus: vi.fn(async () => ({ isMonitored: false, monitoredAuthor: null })),
    getSeriesMonitoringStatus: vi.fn(async () => ({ isMonitored: false, monitoredSeries: null })),
    getAudiobookDeleteCapabilities: vi.fn(async () => ({
      canRemoveFromLibrary: true,
      canDeleteTrackedFiles: true,
      canDeleteFolder: true,
      reason: null,
      fallbackAction: 'RemoveFromLibraryOnly' as const,
    })),
  },
}))

vi.mock('@/services/toastService', () => ({
  useToast: () => ({ success: vi.fn(), warning: vi.fn(), error: vi.fn(), info: vi.fn() }),
}))

// Synthetic series identifiers: one series, and a different series sharing its display name.
const ORIGINAL = 'SER0000EN1'
const TRANSLATED = 'SER0000DE1'
const NAME = 'Sherlock Holmes'

function membership(
  seriesName: string,
  seriesAsin?: string,
  seriesNumber = '1',
): AudiobookSeriesMembership {
  return { seriesName, seriesAsin, seriesNumber, isPrimary: true, sortOrder: 0 }
}

function book(id: number, title: string, memberships: AudiobookSeriesMembership[]): Audiobook {
  return {
    id,
    title,
    authors: ['Arthur Conan Doyle'],
    series: memberships[0]?.seriesName,
    seriesMemberships: memberships,
    imageUrl: `cover-${id}.jpg`,
    files: [],
  } as unknown as Audiobook
}

function installBrowserShims() {
  const g = globalThis as unknown as Record<string, unknown>
  if (typeof g.ResizeObserver === 'undefined') {
    g.ResizeObserver = class {
      observe() {}
      disconnect() {}
    }
  }
  if (typeof g.WebSocket === 'undefined') {
    g.WebSocket = function () {
      /* noop */
    }
  }
}

type SeriesTile = { key?: string; name: string; count: number; seriesAsin?: string }
type GridVm = {
  setGroupBy?: (value: string) => Promise<void> | void
  groupedCollections?: SeriesTile[]
}

async function mountGrid(books: Audiobook[]) {
  installBrowserShims()
  const pinia = createPinia()
  setActivePinia(pinia)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'home', component: { template: '<div />' } },
      { path: '/audiobooks', name: 'audiobooks', component: AudiobooksView },
      { path: '/collection/:type/:name', name: 'collection', component: { template: '<div />' } },
    ],
  })
  await router.push('/audiobooks')
  await router.isReady().catch(() => {})

  const store = useLibraryStore()
  store.audiobooks = books
  store.fetchLibrary = vi.fn(async () => undefined)

  const wrapper = mount(AudiobooksView, {
    global: {
      plugins: [pinia, router],
      stubs: [
        'BulkEditModal',
        'EditAudiobookModal',
        'CustomFilterModal',
        'FiltersDropdown',
        'CustomSelect',
      ],
    },
  })
  await new Promise((r) => setTimeout(r, 0))
  const vm = wrapper.vm as unknown as GridVm
  await vm.setGroupBy?.('series')
  await flushPromises()
  return { wrapper, vm, router }
}

function tiles(vm: GridVm): SeriesTile[] {
  return [...(vm.groupedCollections ?? [])].sort((a, b) => b.count - a.count)
}

describe('library grid: series tiles are keyed by series identity (#953)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('keeps two different series that share a display name as two tiles', async () => {
    const { wrapper, vm } = await mountGrid([
      book(1, 'A Study in Scarlet', [membership(NAME, ORIGINAL, '1')]),
      book(2, 'The Sign of the Four', [membership(NAME, ORIGINAL, '2')]),
      book(3, 'Eine Studie in Scharlachrot', [membership(NAME, TRANSLATED, '1')]),
    ])

    const result = tiles(vm)
    expect(result.map((t) => [t.name, t.count, t.seriesAsin])).toEqual([
      [NAME, 2, ORIGINAL],
      [NAME, 1, TRANSLATED],
    ])
    // Two tiles with the same visible name must still be two rendered cards, which needs a
    // per-identity v-for key rather than the display name.
    expect(new Set(result.map((t) => t.key)).size).toBe(2)
    expect(wrapper.findAll('.collection-card')).toHaveLength(2)
    wrapper.unmount()
  })

  it('merges spelling variants of one series (same identifier) into one tile', async () => {
    const { wrapper, vm } = await mountGrid([
      book(1, 'A Study in Scarlet', [membership('Sherlock Holmes', ORIGINAL, '1')]),
      book(2, 'The Sign of the Four', [membership('sherlock  holmes', ORIGINAL, '2')]),
    ])

    const result = tiles(vm)
    expect(result).toHaveLength(1)
    expect(result[0]).toMatchObject({ name: 'Sherlock Holmes', count: 2, seriesAsin: ORIGINAL })
    wrapper.unmount()
  })

  it('merges spelling variants of a name-only series into one tile', async () => {
    const { wrapper, vm } = await mountGrid([
      book(1, 'A Study in Scarlet', [membership('Sherlock Holmes')]),
      book(2, 'The Sign of the Four', [membership('Sherlock Holmès')]),
    ])

    const result = tiles(vm)
    expect(result).toHaveLength(1)
    expect(result[0]).toMatchObject({ name: 'Sherlock Holmes', count: 2 })
    expect(result[0]?.seriesAsin).toBeUndefined()
    wrapper.unmount()
  })

  it('files a membership without an identifier under the one identified series of that name', async () => {
    // Libraries hold a mix of rows written before and after identifiers were stored. Keying on
    // the identifier alone would split one series in two the moment one of its rows gained one.
    const { wrapper, vm } = await mountGrid([
      book(1, 'A Study in Scarlet', [membership(NAME, ORIGINAL, '1')]),
      book(2, 'The Sign of the Four', [membership(NAME, undefined, '2')]),
    ])

    const result = tiles(vm)
    expect(result).toHaveLength(1)
    expect(result[0]).toMatchObject({ name: NAME, count: 2, seriesAsin: ORIGINAL })
    wrapper.unmount()
  })

  it('does not guess when a name-only membership matches two identified series', async () => {
    const { wrapper, vm } = await mountGrid([
      book(1, 'A Study in Scarlet', [membership(NAME, ORIGINAL, '1')]),
      book(2, 'Eine Studie in Scharlachrot', [membership(NAME, TRANSLATED, '1')]),
      book(3, 'The Sign of the Four', [membership(NAME, undefined, '2')]),
    ])

    const result = tiles(vm)
    expect(result).toHaveLength(3)
    expect(result.map((t) => t.seriesAsin ?? null).sort()).toEqual(
      [ORIGINAL, TRANSLATED, null].sort(),
    )
    wrapper.unmount()
  })

  it('still groups books that only have the legacy series column', async () => {
    const legacy = (id: number, series: string) =>
      ({ id, title: `Book ${id}`, authors: ['A'], series, files: [] }) as unknown as Audiobook
    const { wrapper, vm } = await mountGrid([
      legacy(1, 'The Jungle Book'),
      legacy(2, 'the jungle book'),
    ])

    const result = tiles(vm)
    expect(result).toHaveLength(1)
    expect(result[0]).toMatchObject({ name: 'The Jungle Book', count: 2 })
    wrapper.unmount()
  })

  it('opens an identified series tile with its identifier, and a name-only tile without one', async () => {
    const { wrapper, router } = await mountGrid([
      book(1, 'A Study in Scarlet', [membership(NAME, ORIGINAL, '1')]),
      book(2, 'The Jungle Book', [membership('The Jungle Book')]),
    ])
    // Asserted on the navigation target the tile asks for, resolved the way the router would, so
    // the string and object forms of router.push compare alike.
    const pushSpy = vi.spyOn(router, 'push').mockResolvedValue(undefined)
    const target = (callIndex: number) => {
      const resolved = router.resolve(pushSpy.mock.calls[callIndex]![0])
      return { path: resolved.path, query: resolved.query }
    }
    const card = (title: string) =>
      wrapper.findAll('.collection-card').find((c) => c.text().includes(title))!

    await card(NAME).trigger('click')
    await card('The Jungle Book').trigger('click')

    expect(pushSpy).toHaveBeenCalledTimes(2)
    expect(target(0)).toEqual({
      path: `/collection/series/${encodeURIComponent(NAME)}`,
      query: { asin: ORIGINAL },
    })
    expect(target(1)).toEqual({
      path: `/collection/series/${encodeURIComponent('The Jungle Book')}`,
      query: {},
    })
    wrapper.unmount()
  })
})

async function mountSeriesPage(path: string, books: Audiobook[]) {
  installBrowserShims()
  const pinia = createPinia()
  setActivePinia(pinia)
  const router: Router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'home', component: { template: '<div />' } },
      { path: '/collection/:type/:name', name: 'collection', component: CollectionView },
    ],
  })
  await router.push(path)
  await router.isReady().catch(() => {})

  const store = useLibraryStore()
  store.audiobooks = books
  store.fetchLibrary = vi.fn(async () => undefined)

  const wrapper = mount(CollectionView, {
    global: {
      plugins: [pinia, router],
      stubs: ['EditAudiobookModal', 'CustomSelect', 'AddLibraryModal'],
    },
  })
  await flushPromises()
  await new Promise((r) => setTimeout(r, 0))
  await flushPromises()
  return { wrapper, router }
}

function shownTitles(wrapper: ReturnType<typeof mount>): string[] {
  return wrapper
    .findAll('.collection-card .collection-title')
    .map((el) => el.text())
    .sort()
}

// The lookup's third argument is the series ASIN it resolves by; the name is only its fallback.
function lookupAsins(): Array<string | undefined> {
  return mockGetSeriesLookup.mock.calls.map((call) => (call as unknown[])[2] as string | undefined)
}

const MIXED_LIBRARY = () => [
  book(1, 'A Study in Scarlet', [membership(NAME, ORIGINAL, '1')]),
  book(2, 'The Sign of the Four', [membership(NAME, ORIGINAL, '2')]),
  book(3, 'Eine Studie in Scharlachrot', [membership(NAME, TRANSLATED, '1')]),
]

describe('series page: resolves the series by identity (#953)', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    mockGetSeriesCatalog.mockReset()
    mockGetSeriesCatalog.mockResolvedValue(null)
    mockGetSeriesLookup.mockReset()
    mockGetSeriesLookup.mockResolvedValue(null)
  })

  it('shows only the books of the series named by the identifier in the link', async () => {
    const { wrapper } = await mountSeriesPage(
      `/collection/series/${encodeURIComponent(NAME)}?asin=${TRANSLATED}`,
      MIXED_LIBRARY(),
    )
    expect(shownTitles(wrapper)).toEqual(['Eine Studie in Scharlachrot'])
    wrapper.unmount()
  })

  it('includes a name-only membership on the page of the one identified series of that name', async () => {
    const { wrapper } = await mountSeriesPage(
      `/collection/series/${encodeURIComponent(NAME)}?asin=${ORIGINAL}`,
      [
        book(1, 'A Study in Scarlet', [membership(NAME, ORIGINAL, '1')]),
        book(2, 'The Sign of the Four', [membership(NAME, undefined, '2')]),
      ],
    )
    expect(shownTitles(wrapper)).toEqual(['A Study in Scarlet', 'The Sign of the Four'])
    wrapper.unmount()
  })

  it('hands the identifier from the link to the series lookup, ahead of the catalog guess', async () => {
    // The name-resolved catalog lands on the other series of the same name; the page must not
    // let that override the series the user opened.
    mockGetSeriesCatalog.mockResolvedValue({
      series: { asin: ORIGINAL, name: NAME },
      books: [],
      totalBooks: 0,
    })
    const { wrapper } = await mountSeriesPage(
      `/collection/series/${encodeURIComponent(NAME)}?asin=${TRANSLATED}`,
      MIXED_LIBRARY(),
    )
    expect(lookupAsins()).toEqual([TRANSLATED])
    wrapper.unmount()
  })

  it('uses the library identifier for the lookup when every membership of that name agrees', async () => {
    const { wrapper } = await mountSeriesPage(`/collection/series/${encodeURIComponent(NAME)}`, [
      book(1, 'A Study in Scarlet', [membership(NAME, ORIGINAL, '1')]),
      book(2, 'The Sign of the Four', [membership(NAME, undefined, '2')]),
    ])
    expect(lookupAsins()).toEqual([ORIGINAL])
    wrapper.unmount()
  })

  it('falls back to the name lookup when the name alone is ambiguous', async () => {
    mockGetSeriesCatalog.mockResolvedValue({
      series: { asin: ORIGINAL, name: NAME },
      books: [],
      totalBooks: 0,
    })
    const { wrapper } = await mountSeriesPage(
      `/collection/series/${encodeURIComponent(NAME)}`,
      MIXED_LIBRARY(),
    )
    // No link identifier and two library identifiers: behaviour is unchanged from before, the
    // catalog's answer is passed on and every book of that name is shown.
    expect(lookupAsins()).toEqual([ORIGINAL])
    expect(shownTitles(wrapper)).toEqual([
      'A Study in Scarlet',
      'Eine Studie in Scharlachrot',
      'The Sign of the Four',
    ])
    wrapper.unmount()
  })

  it('shows the position from the membership of the series being viewed', async () => {
    // One book, listed in both same-named series at different positions.
    const { wrapper } = await mountSeriesPage(
      `/collection/series/${encodeURIComponent(NAME)}?asin=${TRANSLATED}`,
      [
        book(1, 'Collected Stories', [
          membership(NAME, ORIGINAL, '3'),
          { ...membership(NAME, TRANSLATED, '7'), isPrimary: false, sortOrder: 1 },
        ]),
      ],
    )
    const vm = wrapper.vm as unknown as { audiobooks: Array<{ seriesNumber?: string }> }
    expect(vm.audiobooks.map((b) => b.seriesNumber)).toEqual(['7'])
    wrapper.unmount()
  })
})
