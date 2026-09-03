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
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { useConfigurationStore } from '@/stores/configuration'

describe('NotificationsTab', () => {
  it('shows loading state and header spinner while application settings are loading', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)

    const cfg = useConfigurationStore()
    cfg.isLoading = true

    const NotificationsTab = (await import('@/views/settings/NotificationsTab.vue')).default
    const wrapper = mount(NotificationsTab, {
      props: { settings: null },
      global: { plugins: [pinia] },
    })

    await wrapper.vm.$nextTick()

    expect(wrapper.find('.loading-state').exists()).toBe(true)
    expect(wrapper.find('.section-header .small-inline-spinner').exists()).toBe(true)
  })

  it('uses the latest committed settings version across consecutive webhook saves', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)

    const cfg = useConfigurationStore()
    cfg.isLoading = false
    const initialSettings = { version: 3, webhookUrl: '', webhooks: [] }
    cfg.applicationSettings = initialSettings as never
    const submittedVersions: number[] = []
    cfg.saveApplicationSettings = vi.fn(async (payload) => {
      submittedVersions.push(payload.version)
      const saved = { ...payload, version: payload.version + 1 }
      cfg.applicationSettings = saved
      return saved
    })

    const NotificationsTab = (await import('@/views/settings/NotificationsTab.vue')).default
    const wrapper = mount(NotificationsTab, {
      props: { settings: initialSettings as never },
      global: { plugins: [pinia] },
    })
    const vm = wrapper.vm as unknown as { openWebhookForm: () => void }

    async function addWebhook(name: string, url: string, expectedSaveCount: number) {
      vm.openWebhookForm()
      await wrapper.vm.$nextTick()
      await wrapper.find('#webhook-name').setValue(name)
      await wrapper.find('#webhook-type').setValue('NTFY')
      await wrapper.vm.$nextTick()
      await wrapper.find('#webhook-url').setValue(url)
      const trigger = wrapper.find('.webhook-triggers input[type="checkbox"]')
      expect(trigger.exists()).toBe(true)
      await trigger.setValue(true)
      await wrapper.find('.webhook-modal form').trigger('submit')
      await vi.waitFor(() => {
        expect(submittedVersions).toHaveLength(expectedSaveCount)
      })
    }

    await addWebhook('First webhook', 'https://ntfy.example/first', 1)
    await addWebhook('Second webhook', 'https://ntfy.example/second', 2)

    expect(submittedVersions).toEqual([3, 4])
    const updates = wrapper.emitted('update:settings') ?? []
    expect(updates).toHaveLength(2)
    expect((updates[1]?.[0] as { version: number }).version).toBe(5)
  })

  describe('webhook URL validation', () => {
    async function mountAndOpenForm() {
      const pinia = createPinia()
      setActivePinia(pinia)

      const cfg = useConfigurationStore()
      cfg.isLoading = false

      const NotificationsTab = (await import('@/views/settings/NotificationsTab.vue')).default
      const wrapper = mount(NotificationsTab, {
        props: { settings: { webhookUrl: '', webhooks: [] } },
        global: { plugins: [pinia] },
      })

      // Open the webhook form and select NTFY type
      const vm = wrapper.vm as unknown as { openWebhookForm: () => void }
      vm.openWebhookForm()
      await wrapper.vm.$nextTick()

      return wrapper
    }

    async function setUrlAndBlur(wrapper: ReturnType<typeof mount>, url: string) {
      // Set the service type to NTFY so the URL input is shown
      const typeSelect = wrapper.find('#webhook-type')
      await typeSelect.setValue('NTFY')
      await wrapper.vm.$nextTick()

      const urlInput = wrapper.find('#webhook-url')
      await urlInput.setValue(url)
      await urlInput.trigger('blur')
      await wrapper.vm.$nextTick()
    }

    it('accepts https:// webhook URLs', async () => {
      const wrapper = await mountAndOpenForm()
      await setUrlAndBlur(wrapper, 'https://ntfy.tld.com/topic')

      const error = wrapper.find('#webhook-url + .error-text')
      expect(error.exists()).toBe(false)
    })

    it('accepts http:// webhook URLs for LAN/self-hosted instances', async () => {
      const wrapper = await mountAndOpenForm()
      await setUrlAndBlur(wrapper, 'http://ntfy.local/topic')

      const error = wrapper.find('#webhook-url + .error-text')
      expect(error.exists()).toBe(false)
    })

    it('rejects non-HTTP(S) schemes such as ftp://', async () => {
      const wrapper = await mountAndOpenForm()
      await setUrlAndBlur(wrapper, 'ftp://ntfy.local/topic')

      const error = wrapper.find('#webhook-url + .error-text')
      expect(error.exists()).toBe(true)
      expect(error.text()).toContain('valid URL')
    })
  })

  describe('trigger vocabulary', () => {
    // The backend gates a webhook row on Imported, Failed, Moved and System, but nothing
    // in production raises System, so the UI offers the three that actually arrive.
    // Offering anything else stores a subscription that dispatch never reads, which is the
    // defect this suite exists to prevent recurring. WebhookTriggerVocabularyTests holds
    // the matching assertion on the C# side.
    const offeredTriggers = ['Imported', 'Failed', 'Moved']

    async function mountWith(settings: unknown) {
      const pinia = createPinia()
      setActivePinia(pinia)

      const cfg = useConfigurationStore()
      cfg.isLoading = false
      cfg.applicationSettings = settings as never

      const NotificationsTab = (await import('@/views/settings/NotificationsTab.vue')).default
      return mount(NotificationsTab, {
        props: { settings: settings as never },
        global: { plugins: [pinia] },
      })
    }

    it('offers exactly the triggers the notification dispatch reads', async () => {
      const wrapper = await mountWith({ webhookUrl: '', webhooks: [] })
      const vm = wrapper.vm as unknown as { openWebhookForm: () => void }
      vm.openWebhookForm()
      await wrapper.vm.$nextTick()

      const offered = wrapper
        .findAll('.webhook-triggers .checkbox-group')
        .map((card) => card.text().trim())

      expect(offered).toEqual(['Import Complete', 'Download Failed', 'Files Moved'])
    })

    it('offers no legacy trigger option that a webhook row cannot receive', async () => {
      // The raw kebab values never reach the rendered DOM, so this asserts on the labels
      // the old build showed for them. Checking the raw values would pass either way.
      const wrapper = await mountWith({ webhookUrl: '', webhooks: [] })
      const vm = wrapper.vm as unknown as { openWebhookForm: () => void }
      vm.openWebhookForm()
      await wrapper.vm.$nextTick()

      const offered = wrapper
        .findAll('.webhook-triggers .checkbox-group')
        .map((card) => card.text().trim())

      for (const retiredLabel of [
        'Book Added',
        'Download Started',
        'Download Complete',
        'Processing Complete',
      ]) {
        expect(offered).not.toContain(retiredLabel)
      }
      expect(offered).toHaveLength(offeredTriggers.length)
    })

    it('saves the selected trigger using the name dispatch gates on', async () => {
      const settings = { version: 1, webhookUrl: '', webhooks: [] }
      const wrapper = await mountWith(settings)
      const cfg = useConfigurationStore()
      const saved: unknown[] = []
      cfg.saveApplicationSettings = vi.fn(async (payload) => {
        saved.push(payload)
        return { ...payload, version: payload.version + 1 }
      })

      const vm = wrapper.vm as unknown as { openWebhookForm: () => void }
      vm.openWebhookForm()
      await wrapper.vm.$nextTick()
      await wrapper.find('#webhook-name').setValue('Imports')
      await wrapper.find('#webhook-type').setValue('NTFY')
      await wrapper.vm.$nextTick()
      await wrapper.find('#webhook-url').setValue('https://ntfy.example/imports')
      await wrapper.find('.webhook-triggers input[type="checkbox"]').setValue(true)
      await wrapper.find('.webhook-modal form').trigger('submit')
      await vi.waitFor(() => {
        expect(saved).toHaveLength(1)
      })

      const payload = saved[0] as { webhooks: Array<{ triggers: string[] }> }
      expect(payload.webhooks[0]?.triggers).toEqual(['Imported'])
      expect(offeredTriggers).toContain(payload.webhooks[0]?.triggers[0])
    })

    it('surfaces a stored trigger this build cannot deliver rather than hiding it', async () => {
      // A row left behind by an install that never ran the startup migration. The badge
      // list used to filter unknown names out entirely, so the webhook looked healthy.
      const wrapper = await mountWith({
        webhookUrl: '',
        webhooks: [
          {
            id: 'stale',
            name: 'Stale webhook',
            url: 'https://ntfy.example/stale',
            type: 'NTFY',
            triggers: ['book-added'],
            isEnabled: true,
          },
        ],
      })
      await wrapper.vm.$nextTick()

      const warning = wrapper.find('.trigger-warning')
      expect(warning.exists()).toBe(true)
      expect(warning.text()).toContain('book-added')
      expect(warning.text()).toContain('Not delivered')
    })

    it('flags a stored System subscription, which is gated but never raised', async () => {
      const wrapper = await mountWith({
        webhookUrl: '',
        webhooks: [
          {
            id: 'system',
            name: 'System webhook',
            url: 'https://ntfy.example/system',
            type: 'NTFY',
            triggers: ['System'],
            isEnabled: true,
          },
        ],
      })
      await wrapper.vm.$nextTick()

      const warning = wrapper.find('.trigger-warning')
      expect(warning.exists()).toBe(true)
      expect(warning.text()).toContain('System')
    })

    it('warns when a webhook has no triggers left to deliver', async () => {
      const wrapper = await mountWith({
        webhookUrl: '',
        webhooks: [
          {
            id: 'empty',
            name: 'Emptied webhook',
            url: 'https://ntfy.example/empty',
            type: 'NTFY',
            triggers: [],
            isEnabled: true,
          },
        ],
      })
      await wrapper.vm.$nextTick()

      const warning = wrapper.find('.trigger-warning')
      expect(warning.exists()).toBe(true)
      expect(warning.text()).toContain('never fire')
    })
  })
})
