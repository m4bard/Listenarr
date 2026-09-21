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
import type { Mock } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import SettingsView from '@/views/SettingsView.vue'
import AuthorIdentityRepairSection from '@/components/settings/AuthorIdentityRepairSection.vue'
import { apiService } from '@/services/api'
import type { ApplicationSettings } from '@/types'

const toast = vi.hoisted(() => ({
  success: vi.fn(),
  error: vi.fn(),
  info: vi.fn(),
  warning: vi.fn(),
}))

vi.mock('@/services/toastService', () => ({ useToast: () => toast }))

// The pass as an install that has been switched on to preview would have it.
const storedSettings = (overrides: Partial<ApplicationSettings> = {}) =>
  ({
    authorIdentityRepairEnabled: true,
    authorIdentityRepairDryRun: true,
    authorIdentityRepairIntervalHours: 6,
    authorIdentityRepairMaxRowsPerRun: 40,
    authorIdentityRepairRecheckAfterDays: 14,
    ...overrides,
  }) as ApplicationSettings

vi.mock('@/services/api', () => ({
  apiService: {
    getBootstrapConfig: vi.fn(async () => ({ AuthenticationRequired: 'Disabled' })),
    getStartupConfig: vi.fn(async () => ({ authenticationRequired: 'false' })),
    getApiKey: vi.fn(async () => ({ apiKey: 'abc' })),
    getApiConfigurations: vi.fn(async () => []),
    getDownloadClientConfigurations: vi.fn(async () => []),
    getApplicationSettings: vi.fn(),
    saveApplicationSettings: vi.fn(),
    getIndexers: vi.fn(async () => []),
    getQualityProfiles: vi.fn(async () => []),
    getAdminUsers: vi.fn(async () => []),
    generateInitialApiKey: vi.fn(async () => ({ apiKey: 'abc' })),
    regenerateApiKey: vi.fn(async () => ({ apiKey: 'abc' })),
    saveStartupConfig: vi.fn(async () => ({})),
  },
  getIndexers: vi.fn(async () => []),
  deleteIndexer: vi.fn(async () => ({})),
  toggleIndexer: vi.fn(async (id: number) => ({ id, isEnabled: true })),
  testIndexer: vi.fn(async (id: number) => ({ success: true, message: 'ok', indexer: { id } })),
  getQualityProfiles: vi.fn(async () => []),
  deleteQualityProfile: vi.fn(async () => ({})),
  createQualityProfile: vi.fn(async (p: unknown) => p),
  updateQualityProfile: vi.fn(async (id: number, p: unknown) => p),
  getRemotePathMappings: vi.fn(async () => []),
  createRemotePathMapping: vi.fn(async (p: unknown) => p),
  updateRemotePathMapping: vi.fn(async (id: number, p: unknown) => p),
  deleteRemotePathMapping: vi.fn(async () => ({})),
}))

const flush = () => new Promise((r) => setTimeout(r, 0))

async function mountSettingsOnGeneral() {
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
  await flush()
  const vm = wrapper.vm as unknown as {
    activeTab: string
    saveSettings: () => Promise<void>
  }
  vm.activeTab = 'general'
  await flush()
  await wrapper.vm.$nextTick()
  return { wrapper, vm }
}

describe('SettingsView, the author identity repair section', () => {
  beforeEach(() => {
    toast.success.mockReset()
    toast.error.mockReset()
    toast.info.mockReset()
    toast.warning.mockReset()
    ;(apiService.getApplicationSettings as Mock).mockReset()
    ;(apiService.getApplicationSettings as Mock).mockImplementation(async () => storedSettings())
    ;(apiService.saveApplicationSettings as Mock).mockReset()
  })

  it('posts the mode the operator chose, not the one that was stored', async () => {
    ;(apiService.saveApplicationSettings as Mock).mockImplementation(
      async (s: ApplicationSettings) => s,
    )
    const { wrapper, vm } = await mountSettingsOnGeneral()

    const section = wrapper.findComponent(AuthorIdentityRepairSection)
    expect(section.exists()).toBe(true)
    // Switch the pass off, which is the one mode change an operator reaches in a single click.
    await section.findAll('.radio-label')[0].trigger('click')
    await vm.saveSettings()
    await flush()

    const posted = (apiService.saveApplicationSettings as Mock).mock
      .calls[0][0] as ApplicationSettings
    expect(posted.authorIdentityRepairEnabled).toBe(false)
    expect(posted.authorIdentityRepairDryRun).toBe(true)
    // The numbers beside it are carried through rather than reset by the mode change.
    expect(posted.authorIdentityRepairIntervalHours).toBe(6)
    expect(posted.authorIdentityRepairMaxRowsPerRun).toBe(40)
    expect(posted.authorIdentityRepairRecheckAfterDays).toBe(14)

    // The control for the failure case below: a save that worked says so and reports nothing
    // as having gone wrong.
    expect(toast.success).toHaveBeenCalled()
    expect(toast.error).not.toHaveBeenCalled()
  })

  it('posts the number it displayed when the stored one was out of range', async () => {
    // The whole point of clamping on display: an operator who is shown 168 and saves must not
    // leave a row saying 9999 behind. This is the level the disagreement would show up at,
    // because the section emits a numeric field only when it is typed in and the view posts the
    // settings object verbatim.
    ;(apiService.getApplicationSettings as Mock).mockImplementation(async () =>
      storedSettings({ authorIdentityRepairIntervalHours: 9999 }),
    )
    ;(apiService.saveApplicationSettings as Mock).mockImplementation(
      async (s: ApplicationSettings) => s,
    )
    const { wrapper, vm } = await mountSettingsOnGeneral()

    const displayed = wrapper
      .findComponent(AuthorIdentityRepairSection)
      .findAll('input[type="number"]')
      .map((i) => (i.element as HTMLInputElement).value)
    expect(displayed[0]).toBe('168')

    await vm.saveSettings()
    await flush()

    const posted = (apiService.saveApplicationSettings as Mock).mock
      .calls[0][0] as ApplicationSettings
    expect(posted.authorIdentityRepairIntervalHours).toBe(168)
    // The two that were already in range are posted exactly as they were stored.
    expect(posted.authorIdentityRepairMaxRowsPerRun).toBe(40)
    expect(posted.authorIdentityRepairRecheckAfterDays).toBe(14)
  })

  it('re-arms the repair gate when the save that would have stored it failed', async () => {
    // The one path that can leave the acknowledgement ticked across a mode change the operator
    // did not make: the view reloads the server's copy on a failed save, so the section is put
    // back to preview while the box that unlocked repair is still ticked. One further click
    // would then store a repair nobody acknowledged twice.
    ;(apiService.saveApplicationSettings as Mock).mockImplementation(async () => {
      throw new Error('the settings row was refused')
    })
    const { wrapper, vm } = await mountSettingsOnGeneral()

    const section = () => wrapper.findComponent(AuthorIdentityRepairSection)
    await section().find('input[type="checkbox"]').setValue(true)
    await section().findAll('.radio-label')[2].trigger('click')
    await flush()
    expect((section().findAll('input[type="radio"]')[2].element as HTMLInputElement).checked).toBe(
      true,
    )

    await vm.saveSettings()
    await flush()
    await wrapper.vm.$nextTick()

    // Back to preview, and back behind the gate.
    const radios = section().findAll('input[type="radio"]')
    expect((radios[1].element as HTMLInputElement).checked).toBe(true)
    expect((radios[2].element as HTMLInputElement).disabled).toBe(true)
    const box = section().find('input[type="checkbox"]')
    expect(box.exists()).toBe(true)
    expect((box.element as HTMLInputElement).checked).toBe(false)
  })

  it('surfaces a save that failed rather than letting it pass for a saved change', async () => {
    ;(apiService.saveApplicationSettings as Mock).mockImplementation(async () => {
      throw new Error('the settings row was refused')
    })
    const { wrapper, vm } = await mountSettingsOnGeneral()

    const section = wrapper.findComponent(AuthorIdentityRepairSection)
    await section.findAll('.radio-label')[0].trigger('click')
    await vm.saveSettings()
    await flush()

    expect(apiService.saveApplicationSettings).toHaveBeenCalled()
    expect(toast.success).not.toHaveBeenCalled()
    const errorCall = toast.error.mock.calls.at(-1)
    expect(errorCall?.[0]).toBe('Save failed')
    expect(String(errorCall?.[1])).toContain('settings row was refused')

    // And the section goes back to showing what the server still holds, so the page does not
    // keep displaying a mode that was never stored.
    await wrapper.vm.$nextTick()
    const radios = wrapper.findComponent(AuthorIdentityRepairSection).findAll('input[type="radio"]')
    expect((radios[1].element as HTMLInputElement).checked).toBe(true)
  })
})
