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
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'

// Fixtures are AudiobookId-keyed only -- no real book titles anywhere in this file.
type BlockedReleaseFixture = {
  id: number
  audiobookId: number
  releaseIdentifier: string
  title: string
  size?: number | null
  blockedAt: string
  reason: string
}

const makeEntry = (overrides: Partial<BlockedReleaseFixture> = {}): BlockedReleaseFixture => ({
  id: 1,
  audiobookId: 101,
  releaseIdentifier: 'btih:0000000000000000000000000000000000000000',
  title: 'Synthetic-Release-A',
  size: 800_000_000,
  blockedAt: '2026-10-01T12:00:00Z',
  reason: 'simulated failure',
  ...overrides,
})

const mockApi = (overrides: Record<string, unknown> = {}) => {
  const apiService = {
    getBlocklist: vi.fn(async () => [] as BlockedReleaseFixture[]),
    deleteBlocklistEntry: vi.fn(async () => undefined),
    ...overrides,
  }

  vi.doMock('@/services/api', () => ({ apiService }))

  return apiService
}

const mockLibraryStore = (audiobooks: Array<{ id: number; title: string }> = []) => {
  vi.doMock('@/stores/library', () => ({
    useLibraryStore: () => ({
      audiobooks,
      fetchLibrary: vi.fn(async () => undefined),
    }),
  }))
}

const mockToast = () => {
  vi.doMock('@/services/toastService', () => ({
    useToast: () => ({
      success: vi.fn(),
      error: vi.fn(),
      info: vi.fn(),
    }),
  }))
}

const mockErrorTracking = () => {
  vi.doMock('@/services/errorTracking', () => ({
    errorTracking: {
      captureException: vi.fn(),
    },
  }))
}

const mountBlocklistView = async () => {
  const { default: BlocklistViewComponent } = await import('@/views/activity/BlocklistView.vue')
  const wrapper = mount(BlocklistViewComponent, {
    global: {
      stubs: {
        RouterLink: { template: '<a><slot /></a>' },
      },
    },
  })

  await flushPromises()
  return wrapper
}

describe('BlocklistView', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.clearAllMocks()
  })

  it('renders a row for every entry the API returns', async () => {
    mockApi({
      getBlocklist: vi.fn(async () => [
        makeEntry({ id: 1, audiobookId: 101 }),
        makeEntry({ id: 2, audiobookId: 202, releaseIdentifier: 'btih:1111111111111111111111111111111111111111' }),
      ]),
    })
    mockLibraryStore([{ id: 101, title: 'Synthetic Audiobook A' }])
    mockToast()
    mockErrorTracking()

    const wrapper = await mountBlocklistView()

    const cards = wrapper.findAll('.blocklist-card')
    expect(cards).toHaveLength(2)
    // The book with a library entry shows its resolved title; the one with no match in the
    // library store falls back to an AudiobookId-only label rather than going blank.
    expect(wrapper.text()).toContain('Synthetic Audiobook A')
    expect(wrapper.text()).toContain('Audiobook #202')
    expect(wrapper.find('.blocklist-release-title').exists()).toBe(true)
  })

  it('does not call DELETE until the user confirms removal', async () => {
    const entry = makeEntry({ id: 42, audiobookId: 101 })
    const apiService = mockApi({
      getBlocklist: vi.fn(async () => [entry]),
    })
    mockLibraryStore([{ id: 101, title: 'Synthetic Audiobook A' }])
    mockToast()
    mockErrorTracking()

    // Confirmation is pending (never resolved) for this test, so if Remove fired the DELETE
    // synchronously -- the bug this test guards against -- the assertion below would already
    // see it called before we ever decide whether to confirm.
    const confirmModule = await import('@/composables/useConfirm')
    const showConfirm = vi.spyOn(confirmModule, 'showConfirm').mockReturnValue(new Promise(() => {}))

    const wrapper = await mountBlocklistView()
    await wrapper.find('.action-button.remove').trigger('click')
    await flushPromises()

    expect(showConfirm).toHaveBeenCalledTimes(1)
    expect(apiService.deleteBlocklistEntry).not.toHaveBeenCalled()
    expect(wrapper.findAll('.blocklist-card')).toHaveLength(1)
  })

  it('removes the row and calls DELETE with that entry\'s id once the user confirms', async () => {
    const entry = makeEntry({ id: 42, audiobookId: 101 })
    const apiService = mockApi({
      getBlocklist: vi.fn(async () => [entry]),
    })
    mockLibraryStore([{ id: 101, title: 'Synthetic Audiobook A' }])
    mockToast()
    mockErrorTracking()

    const confirmModule = await import('@/composables/useConfirm')
    vi.spyOn(confirmModule, 'showConfirm').mockResolvedValue(true as unknown as Promise<boolean>)

    const wrapper = await mountBlocklistView()
    expect(wrapper.findAll('.blocklist-card')).toHaveLength(1)

    await wrapper.find('.action-button.remove').trigger('click')
    await flushPromises()

    expect(apiService.deleteBlocklistEntry).toHaveBeenCalledWith(42)
    expect(apiService.deleteBlocklistEntry).toHaveBeenCalledTimes(1)
    expect(wrapper.findAll('.blocklist-card')).toHaveLength(0)
  })

  it('leaves the entry in place and never calls DELETE when the user cancels', async () => {
    const entry = makeEntry({ id: 42, audiobookId: 101 })
    const apiService = mockApi({
      getBlocklist: vi.fn(async () => [entry]),
    })
    mockLibraryStore([{ id: 101, title: 'Synthetic Audiobook A' }])
    mockToast()
    mockErrorTracking()

    const confirmModule = await import('@/composables/useConfirm')
    vi.spyOn(confirmModule, 'showConfirm').mockResolvedValue(false as unknown as Promise<boolean>)

    const wrapper = await mountBlocklistView()
    expect(wrapper.findAll('.blocklist-card')).toHaveLength(1)

    await wrapper.find('.action-button.remove').trigger('click')
    await flushPromises()

    expect(apiService.deleteBlocklistEntry).not.toHaveBeenCalled()
    expect(wrapper.findAll('.blocklist-card')).toHaveLength(1)
  })

  it('shows a non-blank empty state when nothing is blocked', async () => {
    mockApi({ getBlocklist: vi.fn(async () => []) })
    mockLibraryStore([])
    mockToast()
    mockErrorTracking()

    const wrapper = await mountBlocklistView()

    expect(wrapper.findAll('.blocklist-card')).toHaveLength(0)
    // This is the bug being fixed: before this page existed, "no blocked releases" was never
    // reachable at all. Pin that it renders real, specific copy, not an empty shell.
    expect(wrapper.text()).toContain('No Blocked Releases')
    expect(wrapper.text().length).toBeGreaterThan('No Blocked Releases'.length)
  })

  it('leaves the other rows alone when one entry fails to delete', async () => {
    const first = makeEntry({ id: 1, audiobookId: 101 })
    const second = makeEntry({ id: 2, audiobookId: 202 })
    const apiService = mockApi({
      getBlocklist: vi.fn(async () => [first, second]),
      deleteBlocklistEntry: vi.fn(async () => {
        throw new Error('network error')
      }),
    })
    mockLibraryStore([])
    const toastError = vi.fn()
    vi.doMock('@/services/toastService', () => ({
      useToast: () => ({ success: vi.fn(), error: toastError, info: vi.fn() }),
    }))
    mockErrorTracking()

    const confirmModule = await import('@/composables/useConfirm')
    vi.spyOn(confirmModule, 'showConfirm').mockResolvedValue(true as unknown as Promise<boolean>)

    const wrapper = await mountBlocklistView()
    await wrapper.findAll('.action-button.remove')[0].trigger('click')
    await flushPromises()

    expect(apiService.deleteBlocklistEntry).toHaveBeenCalledWith(1)
    expect(wrapper.findAll('.blocklist-card')).toHaveLength(2)
    expect(toastError).toHaveBeenCalled()
  })
})
