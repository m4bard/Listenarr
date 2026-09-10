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
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import AuthorSeriesSection from '@/components/domain/collection/AuthorSeriesSection.vue'
import type { AuthorSeriesBook } from '@/components/domain/collection/authorSeriesGrouping'

const { mockGetSeriesMonitoringStatus, mockMonitorSeries, mockUnmonitorSeries } = vi.hoisted(
  () => ({
    mockGetSeriesMonitoringStatus: vi.fn(async () => ({
      isMonitored: false,
      monitoredSeries: null,
    })),
    mockMonitorSeries: vi.fn(async () => ({
      message: 'Series monitoring enabled',
      monitoredSeries: {
        id: 7,
        seriesName: 'Marrow Vault',
        region: 'us',
        language: 'english',
        createdAt: '2026-03-18T00:00:00Z',
        updatedAt: '2026-03-18T00:00:00Z',
      },
      addedCount: 0,
      existingCount: 0,
      failedCount: 0,
    })),
    mockUnmonitorSeries: vi.fn(async () => ({ message: 'Series monitoring disabled' })),
  }),
)

vi.mock('@/services/api', () => ({
  apiService: {
    getImageUrl: vi.fn((url: string) => url || ''),
    getSeriesMonitoringStatus: mockGetSeriesMonitoringStatus,
    monitorSeries: mockMonitorSeries,
    unmonitorSeries: mockUnmonitorSeries,
  },
}))

vi.mock('@/services/toastService', () => ({
  useToast: () => ({
    success: vi.fn(),
    warning: vi.fn(),
    error: vi.fn(),
    info: vi.fn(),
  }),
}))

const books: AuthorSeriesBook[] = [
  {
    key: 'library-1',
    title: 'Vault One',
    inLibrary: true,
    monitored: true,
    imageUrl: 'vault-1.jpg',
    seriesMemberships: [
      { seriesName: 'Marrow Vault', seriesAsin: 'B0SERIES01', seriesNumber: '1' },
    ],
  },
  {
    key: 'author-catalog-B0BOOK02',
    title: 'Vault Two',
    inLibrary: false,
    imageUrl: 'vault-2.jpg',
    series: 'Marrow Vault',
    seriesNumber: '2',
  },
  {
    key: 'library-2',
    title: 'Ledger One',
    inLibrary: true,
    monitored: false,
    imageUrl: 'ledger-1.jpg',
    seriesMemberships: [
      { seriesName: 'Ledgerwood Cycle', seriesAsin: 'B0SERIES02', seriesNumber: '1' },
    ],
  },
  {
    key: 'author-catalog-B0BOOK04',
    title: 'Tidewater One',
    inLibrary: false,
    imageUrl: 'tide-1.jpg',
    series: 'Tidewater Papers',
    seriesNumber: '1',
  },
]

function mountSection(sectionBooks: AuthorSeriesBook[] = books) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'home', component: { template: '<div />' } },
      {
        path: '/collection/:type/:name',
        name: 'collection',
        component: { template: '<div />' },
      },
    ],
  })

  return mount(AuthorSeriesSection, {
    props: { books: sectionBooks, region: 'us', language: 'english' },
    global: { plugins: [router] },
  })
}

function expandedPanels(wrapper: ReturnType<typeof mountSection>) {
  return wrapper
    .findAll('.author-series-books')
    .filter((panel) => (panel.element as HTMLElement).style.display !== 'none')
}

describe('AuthorSeriesSection', () => {
  beforeEach(() => {
    mockGetSeriesMonitoringStatus.mockReset()
    mockGetSeriesMonitoringStatus.mockResolvedValue({ isMonitored: false, monitoredSeries: null })
    mockMonitorSeries.mockReset()
    mockUnmonitorSeries.mockReset()
  })

  it('renders one collapsed row per distinct series with the header count', async () => {
    const wrapper = mountSection()
    await flushPromises()

    expect(wrapper.find('.section-title').text()).toBe('Series')
    expect(wrapper.find('.section-count').text()).toBe('3')
    expect(wrapper.findAll('.author-series-card')).toHaveLength(3)
    expect(expandedPanels(wrapper)).toHaveLength(0)
    expect(wrapper.findAll('.author-series-disclosure')[0]!.attributes('aria-expanded')).toBe(
      'false',
    )
  })

  it('gives every panel a valid HTML id that its disclosure control points at', async () => {
    const wrapper = mountSection()
    await flushPromises()

    const panelIds = wrapper.findAll('.author-series-books').map((panel) => panel.attributes('id'))
    expect(panelIds).toEqual([
      'author-series-books-asin-b0series02',
      'author-series-books-asin-b0series01',
      'author-series-books-name-tidewater-papers',
    ])
    for (const id of panelIds) {
      expect(id).toMatch(/^[A-Za-z][A-Za-z0-9-]*$/)
    }

    const disclosures = wrapper.findAll('.author-series-disclosure')
    expect(disclosures.map((button) => button.attributes('aria-controls'))).toEqual(panelIds)
    expect(disclosures[1]!.attributes('aria-label')).toBe('Show the books in Marrow Vault')
  })

  it('renders nothing when no book carries a series', () => {
    const wrapper = mountSection([{ key: 'library-9', title: 'Standalone', inLibrary: true }])
    expect(wrapper.find('.author-series-section').exists()).toBe(false)
  })

  it('shows the library counts and links to each series collection', async () => {
    const wrapper = mountSection()
    await flushPromises()

    const links = wrapper.findAll('.author-series-name')
    expect(links.map((link) => link.text())).toEqual([
      'Ledgerwood Cycle',
      'Marrow Vault',
      'Tidewater Papers',
    ])
    expect(links.map((link) => link.attributes('href'))).toEqual([
      '/collection/series/Ledgerwood%20Cycle',
      '/collection/series/Marrow%20Vault',
      '/collection/series/Tidewater%20Papers',
    ])

    const counts = wrapper.findAll('.author-series-identity .pill').map((pill) => pill.text())
    expect(counts).toEqual(['1 of 1 in library', '1 of 2 in library', '0 of 1 in library'])
  })

  it('expands and collapses every series from the section header', async () => {
    const wrapper = mountSection()
    await flushPromises()

    const expandAll = wrapper.find('.author-series-expand-all')
    expect(expandAll.text()).toContain('Expand All')

    await expandAll.trigger('click')
    expect(expandedPanels(wrapper)).toHaveLength(3)
    expect(wrapper.findAll('.author-series-book')).toHaveLength(4)
    expect(wrapper.text()).toContain('Vault One')

    const positions = wrapper
      .findAll('.author-series-card')[1]!
      .findAll('.author-series-position')
      .map((position) => position.text())
    expect(positions).toEqual(['#1', '#2'])

    expect(wrapper.find('.author-series-expand-all').text()).toContain('Collapse All')
    await wrapper.find('.author-series-expand-all').trigger('click')
    expect(expandedPanels(wrapper)).toHaveLength(0)
  })

  it('states availability once and shows a monitored badge only for library books', async () => {
    const wrapper = mountSection()
    await flushPromises()
    await wrapper.find('.author-series-expand-all').trigger('click')

    const marrowBooks = wrapper.findAll('.author-series-card')[1]!.findAll('.author-series-book')
    expect(marrowBooks[0]!.findAll('.pill').map((pill) => pill.text())).toEqual(['In Library'])
    expect(marrowBooks[0]!.find('.author-series-monitored-badge').text()).toBe('Monitored')

    expect(marrowBooks[1]!.findAll('.pill').map((pill) => pill.text())).toEqual(['Not Added'])
    expect(marrowBooks[1]!.find('.author-series-monitored-badge').exists()).toBe(false)
    expect(marrowBooks[1]!.text().match(/Not Added/g)).toHaveLength(1)
  })

  it('expands a single series from its own disclosure control', async () => {
    const wrapper = mountSection()
    await flushPromises()

    await wrapper.findAll('.author-series-disclosure')[1]!.trigger('click')

    expect(expandedPanels(wrapper)).toHaveLength(1)
    expect(expandedPanels(wrapper)[0]!.findAll('.author-series-book')).toHaveLength(2)
  })

  it('asks for monitoring status only for series that have an ASIN', async () => {
    const wrapper = mountSection()
    await flushPromises()

    expect(mockGetSeriesMonitoringStatus).toHaveBeenCalledTimes(2)
    expect(mockGetSeriesMonitoringStatus.mock.calls.map((call) => call[0])).toEqual([
      'Ledgerwood Cycle',
      'Marrow Vault',
    ])

    const monitorButtons = wrapper.findAll('.author-series-monitor-btn')
    expect(monitorButtons[2]!.attributes('aria-disabled')).toBe('true')
    expect(monitorButtons[2]!.attributes('disabled')).toBeUndefined()
    expect(monitorButtons[0]!.attributes('aria-disabled')).toBe('false')

    // The reason has to be reachable by assistive tech, not only as a hover title.
    const noteId = monitorButtons[2]!.attributes('aria-describedby')
    expect(noteId).toBe('author-series-monitor-note-name-tidewater-papers')
    expect(wrapper.find(`#${noteId}`).text()).toContain('known by name only')

    await monitorButtons[2]!.trigger('click')
    await flushPromises()
    expect(mockMonitorSeries).not.toHaveBeenCalled()
  })

  it('ignores a monitoring record that names a different series ASIN', async () => {
    mockGetSeriesMonitoringStatus.mockResolvedValue({
      isMonitored: true,
      monitoredSeries: {
        id: 3,
        seriesName: 'Marrow Vault',
        seriesAsin: 'B0OTHERSERIES',
        region: 'us',
        language: 'english',
        createdAt: '2026-03-18T00:00:00Z',
        updatedAt: '2026-03-18T00:00:00Z',
      },
    })

    const wrapper = mountSection()
    await flushPromises()

    const monitorButtons = wrapper.findAll('.author-series-monitor-btn')
    expect(monitorButtons[1]!.text()).toContain('Monitor')
    expect(monitorButtons[1]!.text()).not.toContain('Monitoring')
  })

  it('monitors a series through the existing series monitoring endpoint', async () => {
    const wrapper = mountSection()
    await flushPromises()

    await wrapper.findAll('.author-series-monitor-btn')[1]!.trigger('click')
    await flushPromises()

    expect(mockMonitorSeries).toHaveBeenCalledWith({
      name: 'Marrow Vault',
      asin: 'B0SERIES01',
      region: 'us',
      language: 'english',
    })
    expect(wrapper.emitted('monitoring-changed')).toHaveLength(1)
    expect(wrapper.findAll('.author-series-monitor-btn')[1]!.text()).toContain('Monitoring')
  })

  it('unmonitors a series and reports the change to the page', async () => {
    mockGetSeriesMonitoringStatus.mockImplementation(async (name: string) => ({
      isMonitored: name === 'Marrow Vault',
      monitoredSeries:
        name === 'Marrow Vault'
          ? {
              id: 11,
              seriesName: 'Marrow Vault',
              seriesAsin: 'B0SERIES01',
              region: 'us',
              language: 'english',
              createdAt: '2026-03-18T00:00:00Z',
              updatedAt: '2026-03-18T00:00:00Z',
            }
          : null,
    }))

    const wrapper = mountSection()
    await flushPromises()

    expect(wrapper.findAll('.author-series-monitor-btn')[1]!.text()).toContain('Monitoring')

    await wrapper.findAll('.author-series-monitor-btn')[1]!.trigger('click')
    await flushPromises()

    expect(mockUnmonitorSeries).toHaveBeenCalledWith(11)
    expect(mockMonitorSeries).not.toHaveBeenCalled()
    expect(wrapper.emitted('monitoring-changed')).toHaveLength(1)
    expect(wrapper.findAll('.author-series-monitor-btn')[1]!.text()).toBe('Monitor')
  })
})
