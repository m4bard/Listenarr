/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { describe, it, beforeEach, expect, vi } from 'vitest'
import HistoryView from '@/views/activity/HistoryView.vue'
import type { History, HistoryPage, HistoryQueryParams } from '@/types'

const getHistory = vi.fn<(params?: HistoryQueryParams) => Promise<HistoryPage>>()
const getHistoryDetails = vi.fn()
const deleteHistoryEntry = vi.fn()
const clearAllHistory = vi.fn()

vi.mock('@/services/api', () => ({
  apiService: {
    getHistory: (params?: HistoryQueryParams) => getHistory(params),
    getHistoryDetails: (id: number) => getHistoryDetails(id),
    deleteHistoryEntry: (id: number) => deleteHistoryEntry(id),
    clearAllHistory: () => clearAllHistory(),
  },
}))

vi.mock('@/services/errorTracking', () => ({
  errorTracking: { captureException: vi.fn() },
}))

vi.mock('vue-router', () => ({
  RouterLink: {
    name: 'RouterLink',
    props: ['to'],
    template: '<a :href="String(to)"><slot /></a>',
  },
  useRoute: () => ({ params: {}, query: {} }),
  useRouter: () => ({ push: vi.fn() }),
}))

function entry(overrides: Partial<History> = {}): History {
  return {
    id: 1,
    eventType: 'Grabbed',
    outcome: 'Succeeded',
    source: 'Living Room SAB',
    sourceTitle: 'Some Release 2026',
    timestamp: new Date().toISOString(),
    correlationId: 'corr-1',
    ...overrides,
  }
}

/*
 * The total is deliberately not the length of the returned array. That is the one shape where
 * deriving the pager from either source gives the same answer, and it is the shape a
 * hand-written fixture falls into by default, so a suite built only from it cannot tell a
 * server-paged list from a client-paged one.
 */
function page(rows: History[], total = 137): HistoryPage {
  return { history: rows, total, limit: 25, offset: 0 }
}

function mountView() {
  return mount(HistoryView, { global: { plugins: [createPinia()] } })
}

function lastQuery(): HistoryQueryParams {
  return getHistory.mock.calls[getHistory.mock.calls.length - 1][0] as HistoryQueryParams
}

describe('HistoryView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    getHistory.mockResolvedValue(page([entry()]))
    getHistoryDetails.mockResolvedValue({ entry: entry(), related: [entry()] })
    deleteHistoryEntry.mockResolvedValue({ message: 'ok', id: 1 })
    clearAllHistory.mockResolvedValue({ message: 'ok', deletedCount: 137 })
  })

  it('opens on the newest page with no filters applied', async () => {
    mountView()
    await flushPromises()

    expect(getHistory).toHaveBeenCalledTimes(1)
    const query = lastQuery()
    expect(query.limit).toBe(25)
    expect(query.offset).toBe(0)
    expect(query.sortBy).toBe('timestamp')
    expect(query.sortDirection).toBe('desc')
    expect(query.eventType).toBeUndefined()
    expect(query.outcome).toBeUndefined()
    expect(query.from).toBeUndefined()
  })

  it('derives the pager from the response total, not from the rows returned', async () => {
    getHistory.mockResolvedValue(page([entry({ id: 1 }), entry({ id: 2 })], 137))
    const wrapper = mountView()
    await flushPromises()

    expect(wrapper.find('[data-testid="pagination-info"]').text()).toContain('of 137 entries')
    // 137 over a page size of 25 is six pages, of which the window shows five. A pager built
    // from the two rows that came back would offer one page and a disabled next button.
    expect(wrapper.find('[data-testid="page-5"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="page-next"]').attributes('disabled')).toBeUndefined()
  })

  it('reaches the last page through the sliding window', async () => {
    getHistory.mockResolvedValue(page([entry({ id: 1 }), entry({ id: 2 })], 137))
    const wrapper = mountView()
    await flushPromises()

    await wrapper.find('[data-testid="page-5"]').trigger('click')
    await flushPromises()
    expect(lastQuery().offset).toBe(100)

    await wrapper.find('[data-testid="page-next"]').trigger('click')
    await flushPromises()
    expect(lastQuery().offset).toBe(125)
    expect(wrapper.find('[data-testid="page-next"]').attributes('disabled')).toBeDefined()
  })

  describe('filters', () => {
    it('sends a whole multi-event preset as one request', async () => {
      const wrapper = mountView()
      await flushPromises()

      await wrapper.find('#history-event-filter').setValue('download-failed')
      await flushPromises()

      expect(getHistory).toHaveBeenCalledTimes(2)
      expect(lastQuery().eventType).toBe('DownloadFailed,Removed')
    })

    it('sends the outcome filter by name', async () => {
      const wrapper = mountView()
      await flushPromises()

      await wrapper.find('#history-outcome-filter').setValue('Failed')
      await flushPromises()

      expect(lastQuery().outcome).toBe('Failed')
    })

    it('turns a range preset into a from bound and leaves to unset', async () => {
      const wrapper = mountView()
      await flushPromises()

      await wrapper.find('#history-range-filter').setValue('24h')
      await flushPromises()

      const query = lastQuery()
      expect(query.from).toBeTruthy()
      const hoursBack = (Date.now() - new Date(query.from as string).getTime()) / 3600_000
      expect(hoursBack).toBeGreaterThan(23.9)
      expect(hoursBack).toBeLessThan(24.1)
      expect(query.to).toBeUndefined()
    })

    // Page 4 of the old result set silently relabelled as page 4 of the new one is the classic
    // paged-list bug, and it does not announce itself.
    it('resets the offset when a filter changes', async () => {
      const wrapper = mountView()
      await flushPromises()

      await wrapper.find('[data-testid="page-3"]').trigger('click')
      await flushPromises()
      expect(lastQuery().offset).toBe(50)

      await wrapper.find('#history-outcome-filter').setValue('Failed')
      await flushPromises()
      expect(lastQuery().offset).toBe(0)
    })

    // And the inverse: paging must not quietly drop the filters that produced the set.
    it('keeps the active filters when the page changes', async () => {
      const wrapper = mountView()
      await flushPromises()

      await wrapper.find('#history-event-filter').setValue('imported')
      await wrapper.find('#history-outcome-filter').setValue('Failed')
      await flushPromises()

      await wrapper.find('[data-testid="page-2"]').trigger('click')
      await flushPromises()

      const query = lastQuery()
      expect(query.offset).toBe(25)
      expect(query.eventType).toBe('Imported,DownloadCompleted')
      expect(query.outcome).toBe('Failed')
    })

    it('resets the offset when the page size changes', async () => {
      const wrapper = mountView()
      await flushPromises()

      await wrapper.find('[data-testid="page-3"]').trigger('click')
      await flushPromises()

      await wrapper.find('#history-page-size').setValue('50')
      await flushPromises()

      const query = lastQuery()
      expect(query.limit).toBe(50)
      expect(query.offset).toBe(0)
    })
  })

  describe('sorting', () => {
    it('sorts by the clicked column and resets the offset', async () => {
      const wrapper = mountView()
      await flushPromises()

      await wrapper.find('[data-testid="page-3"]').trigger('click')
      await flushPromises()

      await wrapper.find('[data-testid="sort-outcome"]').trigger('click')
      await flushPromises()

      const query = lastQuery()
      expect(query.sortBy).toBe('outcome')
      expect(query.sortDirection).toBe('desc')
      expect(query.offset).toBe(0)
    })

    it('toggles the direction when the same column is clicked again', async () => {
      const wrapper = mountView()
      await flushPromises()

      await wrapper.find('[data-testid="sort-timestamp"]').trigger('click')
      await flushPromises()
      expect(lastQuery().sortDirection).toBe('asc')

      await wrapper.find('[data-testid="sort-timestamp"]').trigger('click')
      await flushPromises()
      expect(lastQuery().sortDirection).toBe('desc')
    })
  })

  describe('the audiobook column', () => {
    it('links a row that carries an audiobook id', async () => {
      getHistory.mockResolvedValue(
        page([entry({ id: 7, audiobookId: 42, audiobookTitle: 'A Book' })]),
      )
      const wrapper = mountView()
      await flushPromises()

      const link = wrapper.find('a.audiobook-link')
      expect(link.exists()).toBe(true)
      expect(link.attributes('href')).toBe('/audiobooks/42')
    })

    // Everything written through the download path has a title and no id, so this is the
    // ordinary case rather than a degenerate one, and it must not throw or render a dead link.
    it('renders a title with no id as plain text', async () => {
      getHistory.mockResolvedValue(
        page([entry({ id: 7, audiobookId: undefined, audiobookTitle: 'A Book' })]),
      )
      const wrapper = mountView()
      await flushPromises()

      expect(wrapper.find('a.audiobook-link').exists()).toBe(false)
      expect(wrapper.find('.cell.audiobook').text()).toBe('A Book')
    })
  })

  describe('row expansion', () => {
    it('fetches details once and does not refetch on collapse and re-expand', async () => {
      const wrapper = mountView()
      await flushPromises()

      await wrapper.find('[data-testid="expand-1"]').trigger('click')
      await flushPromises()
      expect(getHistoryDetails).toHaveBeenCalledTimes(1)
      expect(wrapper.find('[data-testid="details-1"]').exists()).toBe(true)

      await wrapper.find('[data-testid="expand-1"]').trigger('click')
      await flushPromises()
      expect(wrapper.find('[data-testid="details-1"]').exists()).toBe(false)

      await wrapper.find('[data-testid="expand-1"]').trigger('click')
      await flushPromises()
      expect(getHistoryDetails).toHaveBeenCalledTimes(1)
    })

    it('renders the correlated chain when there is more than one attempt', async () => {
      getHistoryDetails.mockResolvedValue({
        entry: entry({ id: 1 }),
        related: [
          entry({ id: 1, eventType: 'Grabbed' }),
          entry({ id: 2, eventType: 'ImportFailed', outcome: 'Failed' }),
          entry({ id: 3, eventType: 'Imported' }),
        ],
      })
      const wrapper = mountView()
      await flushPromises()

      await wrapper.find('[data-testid="expand-1"]').trigger('click')
      await flushPromises()

      expect(wrapper.findAll('.chain-step')).toHaveLength(3)
      expect(wrapper.find('.attempt-chain').text()).toContain('Import failed')
    })

    // Library events mostly take the entity's default correlation id, so they correlate with
    // nothing but themselves. Timeline chrome around one entry would imply a chain that is not
    // there.
    it('renders a lone entry without timeline chrome', async () => {
      getHistoryDetails.mockResolvedValue({
        entry: entry({ id: 1, message: 'Added to library' }),
        related: [entry({ id: 1 })],
      })
      const wrapper = mountView()
      await flushPromises()

      await wrapper.find('[data-testid="expand-1"]').trigger('click')
      await flushPromises()

      expect(wrapper.find('.attempt-chain').exists()).toBe(false)
      expect(wrapper.find('[data-testid="details-1"]').text()).toContain('Added to library')
    })

    it('shows the error separately from the message when they differ', async () => {
      getHistoryDetails.mockResolvedValue({
        entry: entry({ id: 1, message: 'Import failed', error: 'permission denied' }),
        related: [entry({ id: 1 })],
      })
      const wrapper = mountView()
      await flushPromises()

      await wrapper.find('[data-testid="expand-1"]').trigger('click')
      await flushPromises()

      expect(wrapper.find('.error-text').text()).toBe('permission denied')
    })

    it('surfaces a failed details fetch rather than an empty expansion', async () => {
      getHistoryDetails.mockRejectedValue(new Error('boom'))
      const wrapper = mountView()
      await flushPromises()

      await wrapper.find('[data-testid="expand-1"]').trigger('click')
      await flushPromises()

      expect(wrapper.find('[data-testid="details-error"]').exists()).toBe(true)
    })
  })

  /*
   * The raw payload carries absolute paths and raw exception text straight from the file layer
   * (upstream #975). It is not created here and this page does not fix it, but it must not be
   * put in front of somebody who only opened a row.
   */
  describe('the raw data viewer', () => {
    beforeEach(() => {
      getHistoryDetails.mockResolvedValue({
        entry: entry({ id: 1, data: '{"finalPath":"/somewhere/a file.m4b"}' }),
        related: [entry({ id: 1 })],
      })
    })

    it('does not render the raw payload when a row is expanded', async () => {
      const wrapper = mountView()
      await flushPromises()

      await wrapper.find('[data-testid="expand-1"]').trigger('click')
      await flushPromises()

      expect(wrapper.find('[data-testid="raw-toggle"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="raw-data"]').exists()).toBe(false)
      expect(wrapper.find('[data-testid="details-1"]').text()).not.toContain('/somewhere')
    })

    it('renders it only after its own toggle is pressed', async () => {
      const wrapper = mountView()
      await flushPromises()

      await wrapper.find('[data-testid="expand-1"]').trigger('click')
      await flushPromises()
      await wrapper.find('[data-testid="raw-toggle"]').trigger('click')
      await flushPromises()

      expect(wrapper.find('[data-testid="raw-data"]').text()).toContain('finalPath')
    })

    it('hides it again when a different row is opened', async () => {
      getHistory.mockResolvedValue(page([entry({ id: 1 }), entry({ id: 2 })]))
      const wrapper = mountView()
      await flushPromises()

      await wrapper.find('[data-testid="expand-1"]').trigger('click')
      await flushPromises()
      await wrapper.find('[data-testid="raw-toggle"]').trigger('click')
      await flushPromises()
      expect(wrapper.find('[data-testid="raw-data"]').exists()).toBe(true)

      await wrapper.find('[data-testid="expand-2"]').trigger('click')
      await flushPromises()
      expect(wrapper.find('[data-testid="raw-data"]').exists()).toBe(false)
    })

    it('shows a payload that is not JSON as it was stored', async () => {
      getHistoryDetails.mockResolvedValue({
        entry: entry({ id: 1, data: 'not json at all' }),
        related: [entry({ id: 1 })],
      })
      const wrapper = mountView()
      await flushPromises()

      await wrapper.find('[data-testid="expand-1"]').trigger('click')
      await flushPromises()
      await wrapper.find('[data-testid="raw-toggle"]').trigger('click')
      await flushPromises()

      expect(wrapper.find('[data-testid="raw-data"]').text()).toBe('not json at all')
    })
  })

  describe('destructive actions', () => {
    it('asks for confirmation before deleting and removes the row without a refetch', async () => {
      const wrapper = mountView()
      await flushPromises()

      await wrapper.find('[data-testid="delete-1"]').trigger('click')
      await flushPromises()
      expect(deleteHistoryEntry).not.toHaveBeenCalled()

      wrapper.findComponent({ name: 'ConfirmModal' }).vm.$emit('confirm')
      await flushPromises()

      expect(deleteHistoryEntry).toHaveBeenCalledWith(1)
      expect(wrapper.find('[data-testid="history-row-1"]').exists()).toBe(false)
      expect(getHistory).toHaveBeenCalledTimes(1)
    })

    it('states the scope on the clear-all control and again in the confirmation', async () => {
      const wrapper = mountView()
      await flushPromises()

      expect(wrapper.find('[data-testid="clear-all"]').text()).toContain('137')

      await wrapper.find('[data-testid="clear-all"]').trigger('click')
      await flushPromises()

      const confirmations = wrapper.findAllComponents({ name: 'ConfirmModal' })
      const clearModal = confirmations[confirmations.length - 1]
      expect(clearModal.props('visible')).toBe(true)
      expect(clearModal.props('message')).toContain('137')
    })

    /*
     * HistoryController is the only controller behind RequireAdministratorSession and the auth
     * store has no role, so these controls cannot be hidden by role; they attempt and handle.
     * An install running with authentication off never reaches either branch, which is why
     * they are asserted here and nowhere else.
     */
    it.each([401, 403])('explains a %i and stops inviting a second attempt', async (status) => {
      deleteHistoryEntry.mockRejectedValue(Object.assign(new Error('Denied'), { status }))
      const wrapper = mountView()
      await flushPromises()

      await wrapper.find('[data-testid="delete-1"]').trigger('click')
      wrapper.findComponent({ name: 'ConfirmModal' }).vm.$emit('confirm')
      await flushPromises()

      const message = wrapper.find('[data-testid="admin-message"]')
      expect(message.exists()).toBe(true)
      expect(message.text()).toContain('administrator')
      expect(wrapper.find('[data-testid="delete-1"]').attributes('disabled')).toBeDefined()
      expect(wrapper.find('[data-testid="clear-all"]').attributes('disabled')).toBeDefined()
      expect(wrapper.find('[data-testid="history-row-1"]').exists()).toBe(true)
    })

    it('does not disable the controls for an ordinary failure', async () => {
      deleteHistoryEntry.mockRejectedValue(
        Object.assign(new Error('Server error'), { status: 500 }),
      )
      const wrapper = mountView()
      await flushPromises()

      await wrapper.find('[data-testid="delete-1"]').trigger('click')
      wrapper.findComponent({ name: 'ConfirmModal' }).vm.$emit('confirm')
      await flushPromises()

      expect(wrapper.find('[data-testid="admin-message"]').text()).toContain('Server error')
      expect(wrapper.find('[data-testid="delete-1"]').attributes('disabled')).toBeUndefined()
    })
  })

  /*
   * A page that renders "No history" when the fetch threw is indistinguishable from one
   * correctly reporting an empty table, and a fresh test instance has an empty table, so that
   * is exactly where the mistake would be invisible.
   */
  describe('empty against broken', () => {
    it('renders an empty state for a successful empty response', async () => {
      getHistory.mockResolvedValue(page([], 0))
      const wrapper = mountView()
      await flushPromises()

      expect(wrapper.find('[data-testid="history-empty"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="history-error"]').exists()).toBe(false)
    })

    it('renders an error state for a rejected fetch', async () => {
      getHistory.mockRejectedValue(new Error('Network error'))
      const wrapper = mountView()
      await flushPromises()

      expect(wrapper.find('[data-testid="history-error"]').exists()).toBe(true)
      expect(wrapper.find('[data-testid="history-empty"]').exists()).toBe(false)
    })

    it('renders differently in the two cases', async () => {
      getHistory.mockResolvedValue(page([], 0))
      const emptyText = mountView().text()
      await flushPromises()

      getHistory.mockRejectedValue(new Error('Network error'))
      const brokenView = mountView()
      await flushPromises()

      expect(brokenView.text()).not.toBe(emptyText)
    })
  })
})
