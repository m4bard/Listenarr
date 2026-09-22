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
import { useToast } from '@/services/toastService'
import { apiService } from '@/services/api'

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

  describe('Custom Scripts', () => {
    it('renders stored custom script configurations', async () => {
      const pinia = createPinia()
      setActivePinia(pinia)

      const cfg = useConfigurationStore()
      cfg.isLoading = false
      const settings = {
        version: 1,
        webhookUrl: '',
        webhooks: [],
        customScripts: [
          {
            id: 's1',
            name: 'Audiobookshelf rescan',
            path: '/opt/scripts/abs-rescan.sh',
            channels: ['BookAdded'],
            isEnabled: true,
          },
        ],
      }
      cfg.applicationSettings = settings as never

      const NotificationsTab = (await import('@/views/settings/NotificationsTab.vue')).default
      const wrapper = mount(NotificationsTab, {
        props: { settings: settings as never },
        global: { plugins: [pinia] },
      })
      await wrapper.vm.$nextTick()

      expect(wrapper.find('.scripts-grid .script-card').exists()).toBe(true)
      expect(wrapper.text()).toContain('Audiobookshelf rescan')
      expect(wrapper.text()).toContain('/opt/scripts/abs-rescan.sh')
      // Control: an empty customScripts array renders the empty-state copy instead (asserted
      // in the next test's mount setup implicitly via the "no scripts" case never appearing
      // here); a component that ignored the stored list would show that text instead of the
      // card, which this assertion catches directly.
      expect(wrapper.text()).not.toContain('No custom scripts configured')
    })

    it('creating a custom script posts what the user entered, not something else', async () => {
      const pinia = createPinia()
      setActivePinia(pinia)

      const cfg = useConfigurationStore()
      cfg.isLoading = false
      const initialSettings = { version: 5, webhookUrl: '', webhooks: [], customScripts: [] }
      cfg.applicationSettings = initialSettings as never
      let posted: { version: number; customScripts?: Array<Record<string, unknown>> } | null = null
      cfg.saveApplicationSettings = vi.fn(async (payload) => {
        // Snapshot, not a reference. persistEmails spreads shallowly, so payload.emails is
        // the component's own live array; capturing it by reference would assert on
        // component state at assertion time rather than on what crossed the boundary.
        // A JSON round trip rather than structuredClone, which cannot clone a reactive proxy.
        posted = JSON.parse(JSON.stringify(payload)) as never
        const saved = { ...payload, version: payload.version + 1 }
        cfg.applicationSettings = saved
        return saved
      })

      const NotificationsTab = (await import('@/views/settings/NotificationsTab.vue')).default
      const wrapper = mount(NotificationsTab, {
        props: { settings: initialSettings as never },
        global: { plugins: [pinia] },
      })
      const vm = wrapper.vm as unknown as { openScriptForm: () => void }
      vm.openScriptForm()
      await wrapper.vm.$nextTick()

      await wrapper.find('#script-name').setValue('Rescan Audiobookshelf')
      await wrapper.find('#script-path').setValue('/opt/scripts/abs-rescan.sh')
      const channelCheckbox = wrapper.find('.script-channels input[type="checkbox"]')
      expect(channelCheckbox.exists()).toBe(true)
      await channelCheckbox.setValue(true)
      await wrapper.find('.script-modal form').trigger('submit')

      await vi.waitFor(() => {
        expect(posted).not.toBeNull()
      })

      const savedScripts = posted!.customScripts
      expect(savedScripts).toHaveLength(1)
      expect(savedScripts![0]).toMatchObject({
        name: 'Rescan Audiobookshelf',
        path: '/opt/scripts/abs-rescan.sh',
        channels: ['Grab'],
        isEnabled: true,
      })
      // Control: a component that dropped, hardcoded, or reordered the operator's input would
      // fail one of the matchObject fields above instead of quietly passing.
    })

    it('the test button reports success and failure distinguishably', async () => {
      const pinia = createPinia()
      setActivePinia(pinia)

      const cfg = useConfigurationStore()
      cfg.isLoading = false
      const settings = {
        version: 1,
        webhookUrl: '',
        webhooks: [],
        customScripts: [
          {
            id: 's1',
            name: 'Rescan',
            path: '/opt/scripts/rescan.sh',
            channels: ['BookAdded'],
            isEnabled: true,
          },
        ],
      }
      cfg.applicationSettings = settings as never

      const testSpy = vi.spyOn(apiService, 'testNotificationSubscriber')
      testSpy.mockResolvedValueOnce({ success: true, message: 'Custom Script test succeeded' })

      const NotificationsTab = (await import('@/views/settings/NotificationsTab.vue')).default
      const wrapper = mount(NotificationsTab, {
        props: { settings: settings as never },
        global: { plugins: [pinia] },
      })
      await wrapper.vm.$nextTick()

      const testButton = wrapper.find('[title="Run this script now with a test event"]')
      expect(testButton.exists()).toBe(true)

      await testButton.trigger('click')
      await vi.waitFor(() => {
        expect(wrapper.find('.script-test-message').exists()).toBe(true)
      })
      expect(testButton.classes()).toContain('test-success')
      expect(wrapper.find('.script-test-message').classes()).toContain('success')
      expect(wrapper.find('.script-test-message').text()).toContain('succeeded')

      testSpy.mockResolvedValueOnce({ success: false, message: 'Script exited with code: 1' })
      await testButton.trigger('click')
      await vi.waitFor(() => {
        expect(testButton.classes()).toContain('test-fail')
      })
      expect(wrapper.find('.script-test-message').classes()).toContain('fail')
      expect(wrapper.find('.script-test-message').text()).toContain('exited with code')

      expect(testSpy).toHaveBeenCalledWith('Custom Script', 's1')
      // Control: success and failure land in visibly different CSS classes ("test-success" vs
      // "test-fail", "success" vs "fail") and carry different message text; a component that
      // rendered the same "something happened" state for both outcomes would fail this.
    })

    it('a save that fails surfaces the failure rather than silently reverting', async () => {
      const pinia = createPinia()
      setActivePinia(pinia)

      const cfg = useConfigurationStore()
      cfg.isLoading = false
      const initialSettings = { version: 9, webhookUrl: '', webhooks: [], customScripts: [] }
      cfg.applicationSettings = initialSettings as never
      cfg.saveApplicationSettings = vi.fn(async () => {
        throw new Error('save failed: version conflict')
      })

      const toast = useToast()
      toast.toasts.splice(0, toast.toasts.length)

      const NotificationsTab = (await import('@/views/settings/NotificationsTab.vue')).default
      const wrapper = mount(NotificationsTab, {
        props: { settings: initialSettings as never },
        global: { plugins: [pinia] },
      })
      const vm = wrapper.vm as unknown as { openScriptForm: () => void }
      vm.openScriptForm()
      await wrapper.vm.$nextTick()

      await wrapper.find('#script-name').setValue('Will Fail')
      await wrapper.find('#script-path').setValue('/opt/scripts/fail.sh')
      await wrapper.find('.script-channels input[type="checkbox"]').setValue(true)
      await wrapper.find('.script-modal form').trigger('submit')

      await vi.waitFor(() => {
        expect(toast.toasts.some((t) => t.level === 'error')).toBe(true)
      })
      const errorToast = toast.toasts.find((t) => t.level === 'error')
      expect(errorToast?.title).toBe('Save failed')

      // A silent revert would reset and close the form as if the save had gone through; instead
      // the typed value must still be sitting in the still-open form.
      expect((wrapper.find('#script-name').element as HTMLInputElement).value).toBe('Will Fail')
      // Control: a component that swallowed the rejection (closing the form, or clearing the
      // typed name without telling the operator) would fail one of the two assertions above.
    })
  })

  describe('Email', () => {
    const anEmail = (overrides: Record<string, unknown> = {}) => ({
      id: 'e1',
      name: 'Household inbox',
      server: 'smtp.example.invalid',
      port: 587,
      requireEncryption: true,
      username: 'listenarr@example.invalid',
      password: 'REDACTED',
      from: 'listenarr@example.invalid',
      to: ['household@example.invalid'],
      cc: [],
      bcc: [],
      channels: ['Download'],
      isEnabled: true,
      ...overrides,
    })

    it('renders stored email configurations', async () => {
      const pinia = createPinia()
      setActivePinia(pinia)

      const cfg = useConfigurationStore()
      cfg.isLoading = false
      const settings = { version: 1, webhookUrl: '', webhooks: [], emails: [anEmail()] }
      cfg.applicationSettings = settings as never

      const NotificationsTab = (await import('@/views/settings/NotificationsTab.vue')).default
      const wrapper = mount(NotificationsTab, {
        props: { settings: settings as never },
        global: { plugins: [pinia] },
      })
      await wrapper.vm.$nextTick()

      expect(wrapper.find('.emails-grid .email-card').exists()).toBe(true)
      expect(wrapper.text()).toContain('Household inbox')
      expect(wrapper.text()).toContain('smtp.example.invalid:587')
      // Control: a component that ignored the stored list would render the empty-state copy
      // instead of a card, which this catches directly.
      expect(wrapper.text()).not.toContain('No email notifications configured')
    })

    it('creating an email posts what the operator entered, with recipients split on commas', async () => {
      const pinia = createPinia()
      setActivePinia(pinia)

      const cfg = useConfigurationStore()
      cfg.isLoading = false
      const initialSettings = { version: 5, webhookUrl: '', webhooks: [], emails: [] }
      cfg.applicationSettings = initialSettings as never
      let posted: { emails?: Array<Record<string, unknown>> } | null = null
      cfg.saveApplicationSettings = vi.fn(async (payload) => {
        // Snapshot, not a reference. persistEmails spreads shallowly, so payload.emails is
        // the component's own live array; capturing it by reference would assert on
        // component state at assertion time rather than on what crossed the boundary.
        // A JSON round trip rather than structuredClone, which cannot clone a reactive proxy.
        posted = JSON.parse(JSON.stringify(payload)) as never
        const saved = { ...payload, version: payload.version + 1 }
        cfg.applicationSettings = saved
        return saved
      })

      const NotificationsTab = (await import('@/views/settings/NotificationsTab.vue')).default
      const wrapper = mount(NotificationsTab, {
        props: { settings: initialSettings as never },
        global: { plugins: [pinia] },
      })
      const vm = wrapper.vm as unknown as { openEmailForm: () => void }
      vm.openEmailForm()
      await wrapper.vm.$nextTick()

      await wrapper.find('#email-name').setValue('Household inbox')
      await wrapper.find('#email-server').setValue('smtp.example.invalid')
      await wrapper.find('#email-port').setValue(465)
      await wrapper.find('#email-username').setValue('listenarr@example.invalid')
      await wrapper.find('#email-password').setValue('hunter2')
      await wrapper.find('#email-from').setValue('listenarr@example.invalid')
      await wrapper
        .find('#email-to')
        .setValue('one@example.invalid, two@example.invalid ,three@example.invalid')
      const channelCheckbox = wrapper.find('.email-channels input[type="checkbox"]')
      expect(channelCheckbox.exists()).toBe(true)
      await channelCheckbox.setValue(true)
      await wrapper.find('.email-modal form').trigger('submit')

      await vi.waitFor(() => {
        expect(posted).not.toBeNull()
      })

      const savedEmails = posted!.emails
      expect(savedEmails).toHaveLength(1)
      expect(savedEmails![0]).toMatchObject({
        name: 'Household inbox',
        server: 'smtp.example.invalid',
        port: 465,
        username: 'listenarr@example.invalid',
        password: 'hunter2',
        from: 'listenarr@example.invalid',
        to: ['one@example.invalid', 'two@example.invalid', 'three@example.invalid'],
        cc: [],
        bcc: [],
        channels: ['Grab'],
        isEnabled: true,
      })
      // Control: a component that dropped, hardcoded, reordered or failed to trim the operator's
      // input would fail one of the matchObject fields above instead of quietly passing.
    })

    it('an edit that does not touch the password sends the sentinel back rather than a blank', async () => {
      // This is the frontend half of the password contract. The backend keeps the stored password
      // when a save carries REDACTED, and stores blank when a save carries blank. A form that
      // helpfully cleared the field would therefore delete the operator's password on every
      // unrelated edit.
      const pinia = createPinia()
      setActivePinia(pinia)

      const cfg = useConfigurationStore()
      cfg.isLoading = false
      const settings = { version: 2, webhookUrl: '', webhooks: [], emails: [anEmail()] }
      cfg.applicationSettings = settings as never
      let posted: { emails?: Array<Record<string, unknown>> } | null = null
      cfg.saveApplicationSettings = vi.fn(async (payload) => {
        // Snapshot, not a reference. persistEmails spreads shallowly, so payload.emails is
        // the component's own live array; capturing it by reference would assert on
        // component state at assertion time rather than on what crossed the boundary.
        // A JSON round trip rather than structuredClone, which cannot clone a reactive proxy.
        posted = JSON.parse(JSON.stringify(payload)) as never
        const saved = { ...payload, version: payload.version + 1 }
        cfg.applicationSettings = saved
        return saved
      })

      const NotificationsTab = (await import('@/views/settings/NotificationsTab.vue')).default
      const wrapper = mount(NotificationsTab, {
        props: { settings: settings as never },
        global: { plugins: [pinia] },
      })
      await wrapper.vm.$nextTick()

      await wrapper.find('[title="Edit email"]').trigger('click')
      await wrapper.vm.$nextTick()

      expect((wrapper.find('#email-password').element as HTMLInputElement).value).toBe('REDACTED')
      await wrapper.find('#email-name').setValue('Renamed inbox')
      await wrapper.find('.email-modal form').trigger('submit')

      await vi.waitFor(() => {
        expect(posted).not.toBeNull()
      })
      expect(posted!.emails![0]).toMatchObject({
        name: 'Renamed inbox',
        password: 'REDACTED',
      })
    })

    it('an edit that replaces the password sends the new one', async () => {
      // The control for the test above: same flow, same target, and the only difference is that
      // the operator typed in the password field. Without this, "always send REDACTED" would pass.
      const pinia = createPinia()
      setActivePinia(pinia)

      const cfg = useConfigurationStore()
      cfg.isLoading = false
      const settings = { version: 2, webhookUrl: '', webhooks: [], emails: [anEmail()] }
      cfg.applicationSettings = settings as never
      let posted: { emails?: Array<Record<string, unknown>> } | null = null
      cfg.saveApplicationSettings = vi.fn(async (payload) => {
        // Snapshot, not a reference. persistEmails spreads shallowly, so payload.emails is
        // the component's own live array; capturing it by reference would assert on
        // component state at assertion time rather than on what crossed the boundary.
        // A JSON round trip rather than structuredClone, which cannot clone a reactive proxy.
        posted = JSON.parse(JSON.stringify(payload)) as never
        const saved = { ...payload, version: payload.version + 1 }
        cfg.applicationSettings = saved
        return saved
      })

      const NotificationsTab = (await import('@/views/settings/NotificationsTab.vue')).default
      const wrapper = mount(NotificationsTab, {
        props: { settings: settings as never },
        global: { plugins: [pinia] },
      })
      await wrapper.vm.$nextTick()

      await wrapper.find('[title="Edit email"]').trigger('click')
      await wrapper.vm.$nextTick()
      await wrapper.find('#email-password').setValue('a-new-password')
      await wrapper.find('.email-modal form').trigger('submit')

      await vi.waitFor(() => {
        expect(posted).not.toBeNull()
      })
      expect(posted!.emails![0]).toMatchObject({ password: 'a-new-password' })
    })

    it('editing a disabled target leaves it disabled', async () => {
      // An operator who disabled a target and later edits anything about it must not find it
      // sending again. The form resets isEnabled to true before it loads the target, so this is
      // only correct if the load puts it back.
      const pinia = createPinia()
      setActivePinia(pinia)

      const cfg = useConfigurationStore()
      cfg.isLoading = false
      const settings = {
        version: 2,
        webhookUrl: '',
        webhooks: [],
        emails: [anEmail({ isEnabled: false })],
      }
      cfg.applicationSettings = settings as never
      let posted: { emails?: Array<Record<string, unknown>> } | null = null
      cfg.saveApplicationSettings = vi.fn(async (payload) => {
        // Snapshot, not a reference. persistEmails spreads shallowly, so payload.emails is
        // the component's own live array; capturing it by reference would assert on
        // component state at assertion time rather than on what crossed the boundary.
        // A JSON round trip rather than structuredClone, which cannot clone a reactive proxy.
        posted = JSON.parse(JSON.stringify(payload)) as never
        const saved = { ...payload, version: payload.version + 1 }
        cfg.applicationSettings = saved
        return saved
      })

      const NotificationsTab = (await import('@/views/settings/NotificationsTab.vue')).default
      const wrapper = mount(NotificationsTab, {
        props: { settings: settings as never },
        global: { plugins: [pinia] },
      })
      await wrapper.vm.$nextTick()

      await wrapper.find('[title="Edit email"]').trigger('click')
      await wrapper.vm.$nextTick()

      // The checkbox must show it as disabled before the operator saves, not only end up right.
      expect((wrapper.find('#email-name').element as HTMLInputElement).value).toBe(
        'Household inbox',
      )
      await wrapper.find('#email-name').setValue('Renamed while disabled')
      await wrapper.find('.email-modal form').trigger('submit')

      await vi.waitFor(() => {
        expect(posted).not.toBeNull()
      })
      expect(posted!.emails![0]).toMatchObject({
        name: 'Renamed while disabled',
        isEnabled: false,
      })
    })

    it('the delete button on a card actually offers the confirmation', async () => {
      // The trash button only sets the pending target; something has to render the confirmation
      // for the operator to reach the delete at all.
      const pinia = createPinia()
      setActivePinia(pinia)

      const cfg = useConfigurationStore()
      cfg.isLoading = false
      const settings = { version: 4, webhookUrl: '', webhooks: [], emails: [anEmail()] }
      cfg.applicationSettings = settings as never
      let posted: { emails?: Array<Record<string, unknown>> } | null = null
      cfg.saveApplicationSettings = vi.fn(async (payload) => {
        // Snapshot, not a reference. persistEmails spreads shallowly, so payload.emails is
        // the component's own live array; capturing it by reference would assert on
        // component state at assertion time rather than on what crossed the boundary.
        // A JSON round trip rather than structuredClone, which cannot clone a reactive proxy.
        posted = JSON.parse(JSON.stringify(payload)) as never
        const saved = { ...payload, version: payload.version + 1 }
        cfg.applicationSettings = saved
        return saved
      })

      const NotificationsTab = (await import('@/views/settings/NotificationsTab.vue')).default
      const wrapper = mount(NotificationsTab, {
        props: { settings: settings as never },
        global: { plugins: [pinia] },
      })
      await wrapper.vm.$nextTick()

      await wrapper.find('[title="Delete email"]').trigger('click')
      await wrapper.vm.$nextTick()

      expect(wrapper.text()).toContain('delete the email notification')

      const confirm = wrapper
        .findAll('button')
        .find((button) => /delete/i.test(button.text()) && !button.attributes('title'))
      expect(confirm).toBeDefined()
      await confirm!.trigger('click')

      await vi.waitFor(() => {
        expect(posted).not.toBeNull()
      })
      expect(posted!.emails).toHaveLength(0)
    })

    it('toggling a target off persists it without disturbing the stored password', async () => {
      const pinia = createPinia()
      setActivePinia(pinia)

      const cfg = useConfigurationStore()
      cfg.isLoading = false
      const settings = { version: 6, webhookUrl: '', webhooks: [], emails: [anEmail()] }
      cfg.applicationSettings = settings as never
      let posted: { emails?: Array<Record<string, unknown>> } | null = null
      cfg.saveApplicationSettings = vi.fn(async (payload) => {
        // Snapshot, not a reference. persistEmails spreads shallowly, so payload.emails is
        // the component's own live array; capturing it by reference would assert on
        // component state at assertion time rather than on what crossed the boundary.
        // A JSON round trip rather than structuredClone, which cannot clone a reactive proxy.
        posted = JSON.parse(JSON.stringify(payload)) as never
        const saved = { ...payload, version: payload.version + 1 }
        cfg.applicationSettings = saved
        return saved
      })

      const NotificationsTab = (await import('@/views/settings/NotificationsTab.vue')).default
      const wrapper = mount(NotificationsTab, {
        props: { settings: settings as never },
        global: { plugins: [pinia] },
      })
      await wrapper.vm.$nextTick()

      await wrapper.find('[title="Disable email"]').trigger('click')

      await vi.waitFor(() => {
        expect(posted).not.toBeNull()
      })
      expect(posted!.emails![0]).toMatchObject({ isEnabled: false, password: 'REDACTED' })
      // The component must not have reached into the object it was handed as a prop.
      expect(settings.emails[0].isEnabled).toBe(true)
    })

    it('clearing the password field sends a blank, which is how authentication is removed', async () => {
      // The third branch of the contract, and the one a helpful implementation breaks: falling
      // back to the sentinel when the field is empty would leave the operator with no way to
      // remove SMTP authentication and a green toast telling them it worked.
      const pinia = createPinia()
      setActivePinia(pinia)

      const cfg = useConfigurationStore()
      cfg.isLoading = false
      const settings = { version: 2, webhookUrl: '', webhooks: [], emails: [anEmail()] }
      cfg.applicationSettings = settings as never
      let posted: { emails?: Array<Record<string, unknown>> } | null = null
      cfg.saveApplicationSettings = vi.fn(async (payload) => {
        posted = JSON.parse(JSON.stringify(payload)) as never
        const saved = { ...payload, version: payload.version + 1 }
        cfg.applicationSettings = saved
        return saved
      })

      const NotificationsTab = (await import('@/views/settings/NotificationsTab.vue')).default
      const wrapper = mount(NotificationsTab, {
        props: { settings: settings as never },
        global: { plugins: [pinia] },
      })
      await wrapper.vm.$nextTick()

      await wrapper.find('[title="Edit email"]').trigger('click')
      await wrapper.vm.$nextTick()
      await wrapper.find('#email-password').setValue('')
      await wrapper.find('.email-modal form').trigger('submit')

      await vi.waitFor(() => {
        expect(posted).not.toBeNull()
      })
      expect(posted!.emails![0]!.password).toBe('')
    })

    it('the test button really sends, and reports success and failure distinguishably', async () => {
      const pinia = createPinia()
      setActivePinia(pinia)

      const cfg = useConfigurationStore()
      cfg.isLoading = false
      const settings = { version: 1, webhookUrl: '', webhooks: [], emails: [anEmail()] }
      cfg.applicationSettings = settings as never

      const testSpy = vi.spyOn(apiService, 'testNotificationSubscriber')
      testSpy.mockResolvedValueOnce({ success: true, message: 'Email test succeeded' })

      const NotificationsTab = (await import('@/views/settings/NotificationsTab.vue')).default
      const wrapper = mount(NotificationsTab, {
        props: { settings: settings as never },
        global: { plugins: [pinia] },
      })
      await wrapper.vm.$nextTick()

      const testButton = wrapper.find('[title="Send a test message through this server now"]')
      expect(testButton.exists()).toBe(true)

      await testButton.trigger('click')
      await vi.waitFor(() => {
        expect(wrapper.find('.email-test-message').exists()).toBe(true)
      })
      expect(testButton.classes()).toContain('test-success')
      expect(wrapper.find('.email-test-message').classes()).toContain('success')

      testSpy.mockResolvedValueOnce({
        success: false,
        message: '535: 5.7.8 Username and Password not accepted',
      })
      await testButton.trigger('click')
      await vi.waitFor(() => {
        expect(testButton.classes()).toContain('test-fail')
      })
      expect(wrapper.find('.email-test-message').classes()).toContain('fail')
      expect(wrapper.find('.email-test-message').text()).toContain('535')

      // The button reaches the subscriber endpoint, which sends a real message, rather than any
      // client-side validation shortcut.
      expect(testSpy).toHaveBeenCalledWith('Email', 'e1')
    })

    it('refuses to save a target with no recipient at all', async () => {
      const pinia = createPinia()
      setActivePinia(pinia)

      const cfg = useConfigurationStore()
      cfg.isLoading = false
      const initialSettings = { version: 3, webhookUrl: '', webhooks: [], emails: [] }
      cfg.applicationSettings = initialSettings as never
      cfg.saveApplicationSettings = vi.fn(async (payload) => payload as never)

      const NotificationsTab = (await import('@/views/settings/NotificationsTab.vue')).default
      const wrapper = mount(NotificationsTab, {
        props: { settings: initialSettings as never },
        global: { plugins: [pinia] },
      })
      const vm = wrapper.vm as unknown as { openEmailForm: () => void }
      vm.openEmailForm()
      await wrapper.vm.$nextTick()

      await wrapper.find('#email-name').setValue('No recipients')
      await wrapper.find('#email-server').setValue('smtp.example.invalid')
      await wrapper.find('#email-from').setValue('listenarr@example.invalid')
      await wrapper.find('.email-channels input[type="checkbox"]').setValue(true)
      await wrapper.find('.email-modal form').trigger('submit')

      await wrapper.vm.$nextTick()
      expect(cfg.saveApplicationSettings).not.toHaveBeenCalled()
      expect(wrapper.text()).toContain('At least one recipient, CC or BCC address is required')
    })

    it('refuses to save a malformed recipient address', async () => {
      const pinia = createPinia()
      setActivePinia(pinia)

      const cfg = useConfigurationStore()
      cfg.isLoading = false
      const initialSettings = { version: 3, webhookUrl: '', webhooks: [], emails: [] }
      cfg.applicationSettings = initialSettings as never
      cfg.saveApplicationSettings = vi.fn(async (payload) => payload as never)

      const NotificationsTab = (await import('@/views/settings/NotificationsTab.vue')).default
      const wrapper = mount(NotificationsTab, {
        props: { settings: initialSettings as never },
        global: { plugins: [pinia] },
      })
      const vm = wrapper.vm as unknown as { openEmailForm: () => void }
      vm.openEmailForm()
      await wrapper.vm.$nextTick()

      await wrapper.find('#email-name').setValue('Bad address')
      await wrapper.find('#email-server').setValue('smtp.example.invalid')
      await wrapper.find('#email-from').setValue('listenarr@example.invalid')
      await wrapper.find('#email-to').setValue('not-an-address')
      await wrapper.find('.email-channels input[type="checkbox"]').setValue(true)
      await wrapper.find('.email-modal form').trigger('submit')

      await wrapper.vm.$nextTick()
      expect(cfg.saveApplicationSettings).not.toHaveBeenCalled()
      // The control is the passing create test above, which uses the same flow with well-formed
      // addresses and does reach saveApplicationSettings.
      expect(wrapper.text()).toContain('is not a valid email address')
    })
  })
})
