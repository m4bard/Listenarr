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
import { describe, it, expect, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import DownloadClientsTab from '@/views/settings/DownloadClientsTab.vue'
import { useConfigurationStore } from '@/stores/configuration'

// One client blocked by recent failures, one failing but not yet blocked (inside the five-minute
// grace), one healthy with no status row at all. Timestamps come back from SQLite with no zone
// designator, which is the case the date parsing has to get right.
const statuses = [
  {
    clientId: 'blocked',
    escalationLevel: 3,
    initialFailure: '2026-09-23T10:00:00',
    mostRecentFailure: '2026-09-23T11:45:00',
    disabledTill: '2999-01-01T00:00:00',
    isBlocked: true,
  },
  {
    clientId: 'failing',
    escalationLevel: 1,
    initialFailure: '2026-09-23T11:58:00',
    mostRecentFailure: '2026-09-23T11:58:00',
    disabledTill: null,
    isBlocked: false,
  },
]

vi.mock('@/services/api', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...(actual as object),
    getRemotePathMappings: vi.fn(async () => []),
    getDownloadClientStatuses: vi.fn(async () => statuses),
  }
})

describe('DownloadClientsTab failure status', () => {
  it('badges a blocked client and a failing one, and leaves a healthy one alone', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useConfigurationStore()
    vi.spyOn(store, 'loadDownloadClientConfigurations').mockResolvedValue(undefined as never)

    const base = {
      type: 'qbittorrent',
      host: 'dbhost.local',
      port: 8080,
      isEnabled: true,
      useSSL: false,
      downloadPath: '',
      username: '',
      password: '',
      settings: {},
      priority: 1,
    }
    store.downloadClientConfigurations = [
      { ...base, id: 'blocked', name: 'seedbox' },
      { ...base, id: 'failing', name: 'local' },
      { ...base, id: 'healthy', name: 'spare' },
    ] as never

    const wrapper = mount(DownloadClientsTab, { global: { plugins: [pinia] } })
    await flushPromises()

    const cards = wrapper.findAll('.indexer-card')
    expect(cards).toHaveLength(3)

    expect(cards[0].find('[data-testid="client-failure-status"]').exists()).toBe(true)
    expect(cards[0].text()).toContain('Unavailable until')

    expect(cards[1].find('[data-testid="client-failure-status"]').exists()).toBe(true)
    expect(cards[1].text()).toContain('Failing since')
    expect(cards[1].text()).not.toContain('Unavailable until')

    // The control: a client with no status row gains nothing.
    expect(cards[2].find('[data-testid="client-failure-status"]').exists()).toBe(false)
  })
})
