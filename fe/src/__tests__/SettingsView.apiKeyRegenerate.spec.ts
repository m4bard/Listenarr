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

// Settings > General > "Generate new API key" (issue #1238): the confirmation appears, but the
// new key does not stick. The regenerate call saves the new key on the server, but the page keeps
// the startup config it loaded (old key included) and posts it back on "Save Settings", which
// writes the old key again.

import { describe, it, expect, vi, beforeEach } from 'vitest'
import type { Mock } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import SettingsView from '@/views/SettingsView.vue'
import { apiService } from '@/services/api'

vi.mock('@/composables/useConfirm', () => ({
  showConfirm: vi.fn(async () => true),
}))

vi.mock('@/services/api', () => ({
  apiService: {
    getBootstrapConfig: vi.fn(async () => ({})),
    getStartupConfig: vi.fn(),
    getApiKey: vi.fn(async () => ({ apiKey: 'OLD-KEY' })),
    getApiConfigurations: vi.fn(async () => []),
    getDownloadClientConfigurations: vi.fn(async () => []),
    getApplicationSettings: vi.fn(),
    getIndexers: vi.fn(async () => []),
    getQualityProfiles: vi.fn(async () => []),
    getAdminUsers: vi.fn(async () => []),
    generateInitialApiKey: vi.fn(),
    regenerateApiKey: vi.fn(async () => ({ apiKey: 'NEW-KEY' })),
    saveStartupConfig: vi.fn(async () => ({})),
  },
  getIndexers: vi.fn(async () => []),
  getQualityProfiles: vi.fn(async () => []),
  getRemotePathMappings: vi.fn(async () => []),
}))

async function mountGeneralSettings() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', name: 'home', component: { template: '<div />' } }],
  })
  await router.push('/')
  await router.isReady().catch(() => {})

  const pinia = createPinia()
  setActivePinia(pinia)
  const wrapper = mount(SettingsView, {
    global: { plugins: [pinia, router], stubs: ['FolderBrowser'] },
  })

  const generalTab = wrapper
    .findAll('button.tab-button')
    .find((b) => b.text().includes('General Settings'))
  await generalTab!.trigger('click')
  await vi.waitFor(() => expect(wrapper.find('button.regen-btn').exists()).toBe(true))

  const { useConfigurationStore } = await import('@/stores/configuration')
  useConfigurationStore().saveApplicationSettings = vi
    .fn()
    .mockImplementation(async (payload) => ({ ...payload, version: 2 }))

  return wrapper
}

describe('Settings: regenerate the API key, then save', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    ;(apiService.getStartupConfig as Mock).mockResolvedValue({
      apiKey: 'OLD-KEY',
      port: 5000,
      authenticationRequired: 'false',
    })
    ;(apiService.getApplicationSettings as Mock).mockResolvedValue({ version: 1 })
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText: vi.fn(async () => undefined) },
    })
  })

  it('does not write the old API key back when the settings are saved after a regeneration', async () => {
    const wrapper = await mountGeneralSettings()

    await wrapper.find('button.regen-btn').trigger('click')
    await flushPromises()
    expect(apiService.regenerateApiKey).toHaveBeenCalledTimes(1)

    const saveBtn = wrapper
      .findAll('button.btn.btn-primary')
      .find((b) => b.text().includes('Save Settings'))
    await saveBtn!.trigger('click')
    await vi.waitFor(() => expect(apiService.saveStartupConfig).toHaveBeenCalledTimes(1))

    const posted = (apiService.saveStartupConfig as Mock).mock.calls[0][0]
    expect(posted.apiKey).toBe('NEW-KEY')
  })

  it('shows the new API key in the field after a regeneration', async () => {
    const wrapper = await mountGeneralSettings()

    await wrapper.find('button.regen-btn').trigger('click')
    await flushPromises()

    expect((wrapper.find('input.api-key-field').element as HTMLInputElement).value).toBe('NEW-KEY')
  })
})
