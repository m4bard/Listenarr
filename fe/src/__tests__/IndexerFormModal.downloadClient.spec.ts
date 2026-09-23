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
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import IndexerFormModal from '@/components/settings/IndexerFormModal.vue'
import { useConfigurationStore } from '@/stores/configuration'
import type { DownloadClientConfiguration, Indexer } from '@/types'

vi.mock('@/services/api', async (importOriginal) => {
  const actual = (await importOriginal()) as Record<string, unknown>
  return {
    ...actual,
    createIndexer: vi.fn(async (indexer: unknown) => indexer),
    updateIndexer: vi.fn(async (_id: number, indexer: unknown) => indexer),
  }
})

const client = (
  id: string,
  type: DownloadClientConfiguration['type'],
  isEnabled = true,
): DownloadClientConfiguration =>
  ({
    id,
    name: `${id} name`,
    type,
    host: 'localhost',
    port: 8080,
    username: '',
    password: '',
    downloadPath: '',
    useSSL: false,
    isEnabled,
    settings: {},
  }) as DownloadClientConfiguration

const tracker = (overrides: Partial<Indexer> = {}): Indexer =>
  ({
    id: 3,
    name: 'Private Tracker',
    type: 'Torrent',
    implementation: 'Torznab',
    url: 'https://tracker.example.test',
    apiKey: 'key',
    categories: '3030',
    enableRss: true,
    enableAutomaticSearch: true,
    enableInteractiveSearch: true,
    enableAnimeStandardSearch: false,
    isEnabled: true,
    priority: 25,
    minimumAge: 0,
    retention: 0,
    maximumSize: 0,
    additionalSettings: '',
    createdAt: '',
    updatedAt: '',
    escalationLevel: 0,
    ...overrides,
  }) as Indexer

describe('IndexerFormModal download client binding', () => {
  let pinia: Pinia

  beforeEach(async () => {
    pinia = createPinia()
    setActivePinia(pinia)
    const store = useConfigurationStore()
    store.downloadClientConfigurations = [
      client('qb-local', 'qbittorrent'),
      client('tr-off', 'transmission', false),
      client('sab', 'sabnzbd'),
    ]
    const api = await import('@/services/api')
    vi.mocked(api.updateIndexer).mockClear()
    vi.mocked(api.createIndexer).mockClear()
  })

  const mountFor = async (indexer: Indexer | null) => {
    const wrapper = mount(IndexerFormModal, {
      global: { plugins: [pinia] },
      props: { visible: true, editingIndexer: indexer },
    })
    await flushPromises()
    return wrapper
  }

  const optionValues = (wrapper: Awaited<ReturnType<typeof mountFor>>) =>
    wrapper.findAll('#downloadClientId option').map((o) => (o.element as HTMLOptionElement).value)

  it('offers Any plus the enabled clients of the indexer protocol, and nothing else', async () => {
    const wrapper = await mountFor(tracker())

    // The disabled Transmission and the SABnzbd client are both absent: one cannot receive a
    // grab, and the other cannot receive a torrent.
    expect(optionValues(wrapper)).toEqual(['', 'qb-local'])
  })

  it('offers the usenet clients to a usenet indexer', async () => {
    const wrapper = await mountFor(
      tracker({ type: 'Usenet', implementation: 'Newznab', name: 'Usenet Indexer' }),
    )

    expect(optionValues(wrapper)).toEqual(['', 'sab'])
  })

  it('loads a stored binding and saves it back unchanged', async () => {
    const wrapper = await mountFor(tracker({ downloadClientId: 'qb-local' }))

    const select = wrapper.find('#downloadClientId').element as HTMLSelectElement
    expect(select.value).toBe('qb-local')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    const api = await import('@/services/api')
    expect(api.updateIndexer).toHaveBeenCalledTimes(1)
    expect(vi.mocked(api.updateIndexer).mock.calls[0]![1]).toMatchObject({
      downloadClientId: 'qb-local',
    })
  })

  it('saves Any as no binding', async () => {
    // Control for the round trip above: the value sent follows the select, rather than
    // echoing whatever the indexer arrived with.
    const wrapper = await mountFor(tracker({ downloadClientId: 'qb-local' }))

    await wrapper.find('#downloadClientId').setValue('')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    const api = await import('@/services/api')
    expect(vi.mocked(api.updateIndexer).mock.calls[0]![1]).toMatchObject({
      downloadClientId: null,
    })
  })

  it('keeps and flags a binding to a client that no longer exists instead of dropping it', async () => {
    // Readarr keeps the dangling id and warns about it (IndexerDownloadClientCheck). Quietly
    // turning it into Any on the next save would re-route the tracker's grabs without the
    // operator ever choosing that.
    const wrapper = await mountFor(tracker({ downloadClientId: 'qb-deleted' }))

    const select = wrapper.find('#downloadClientId').element as HTMLSelectElement
    expect(select.value).toBe('qb-deleted')
    expect(wrapper.find('#downloadClientId option[value="qb-deleted"]').text()).toMatch(
      /unavailable/i,
    )

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    const api = await import('@/services/api')
    expect(vi.mocked(api.updateIndexer).mock.calls[0]![1]).toMatchObject({
      downloadClientId: 'qb-deleted',
    })
  })

  it('has no client choice for Internet Archive, whose grabs never reach a client', async () => {
    const wrapper = await mountFor(
      tracker({
        implementation: 'InternetArchive',
        type: 'Usenet',
        name: 'Archive',
        downloadClientId: 'sab',
      }),
    )

    expect(wrapper.find('#downloadClientId').exists()).toBe(false)

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    const api = await import('@/services/api')
    expect(vi.mocked(api.updateIndexer).mock.calls[0]![1]).toMatchObject({
      downloadClientId: null,
    })
  })
})
