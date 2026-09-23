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
import { nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import DownloadClientFormModal from '@/components/domain/download/DownloadClientFormModal.vue'
import { useConfigurationStore } from '@/stores/configuration'

describe('DownloadClientFormModal', () => {
  it('renders password input for qbittorrent', async () => {
    const wrapper = mount(DownloadClientFormModal, {
      global: { plugins: [createPinia()] },
      props: { visible: true, editingClient: null },
    })

    // Provide an editingClient prop to initialize formData for qbittorrent
    await wrapper.setProps({
      editingClient: {
        id: '1',
        name: 'qbt',
        type: 'qbittorrent',
        host: 'qbittorrent.local',
        port: 8080,
        isEnabled: true,
        useSSL: false,
        downloadPath: '',
        username: '',
        password: '',
        settings: {},
      },
    })
    await wrapper.vm.$nextTick()

    const passwordComponent = wrapper.findComponent({ name: 'PasswordInput' })
    expect(passwordComponent.exists()).toBe(true)
  })

  it('renders api key input for sabnzbd', async () => {
    const wrapper = mount(DownloadClientFormModal, {
      global: { plugins: [createPinia()] },
      props: { visible: true, editingClient: null },
    })

    await wrapper.setProps({
      editingClient: {
        id: '2',
        name: 'sab',
        type: 'sabnzbd',
        host: 'sab.local',
        port: 8080,
        isEnabled: true,
        useSSL: false,
        downloadPath: '',
        username: '',
        password: '',
        settings: {},
      },
    })
    await wrapper.vm.$nextTick()

    const apiKeyComponent = wrapper.findComponent({ name: 'PasswordInput' })
    expect(apiKeyComponent.exists()).toBe(true)
  })

  it('test button on modal uses current input values and includes ID for existing client fallback', async () => {
    const api = await import('@/services/api')
    ;(api.testDownloadClient as unknown) = vi.fn(async (config: unknown) => ({
      success: true,
      message: 'ok',
      client: config,
    }))

    const wrapper = mount(DownloadClientFormModal, {
      global: { plugins: [createPinia()] },
      props: { visible: true, editingClient: null },
    })

    await wrapper.setProps({
      editingClient: {
        id: '3',
        name: 'qbt',
        type: 'qbittorrent',
        host: 'original.local',
        port: 8080,
        isEnabled: true,
        useSSL: false,
        downloadPath: '',
        username: '',
        settings: {},
        password: 'dbpass',
      },
    })
    await wrapper.vm.$nextTick()

    // change host input to a new value before testing
    const hostInput = wrapper.find('input[id="host"]')
    await hostInput.setValue('http://edited.local/nzbget')

    // click the Test button (use class selector to reliably find the correct button)
    const testButton = wrapper.find('button.btn-info')
    expect(testButton.exists()).toBe(true)
    await testButton.trigger('click')

    expect(api.testDownloadClient as unknown).toHaveBeenCalled()
    const calledWith = (api.testDownloadClient as unknown).mock.calls[0][0]
    expect(calledWith.host).toBe('edited.local')
    // Existing client id should be sent so backend can reuse saved credentials when needed.
    expect(calledWith.id).toBe('3')
  })

  it('modal sends existing client ID when password is cleared so backend can pull saved password', async () => {
    const api = await import('@/services/api')
    ;(api.testDownloadClient as unknown) = vi.fn(async (config: unknown) => ({
      success: true,
      message: 'ok',
      client: config,
    }))

    const wrapper = mount(DownloadClientFormModal, {
      global: { plugins: [createPinia()] },
      props: { visible: true, editingClient: null },
    })

    await wrapper.setProps({
      editingClient: {
        id: '4',
        name: 'qbt',
        type: 'qbittorrent',
        host: 'host.local',
        port: 8080,
        isEnabled: true,
        useSSL: false,
        downloadPath: '',
        username: '',
        settings: {},
        password: 'dbpass',
      },
    })
    await wrapper.vm.$nextTick()

    const passwordComponent = wrapper.findComponent({ name: 'PasswordInput' })
    expect(passwordComponent.exists()).toBe(true)
    // prepopulated value should match DB via v-model prop
    expect(passwordComponent.props('modelValue')).toBe('dbpass')

    // clear the password input by emitting v-model update
    await (passwordComponent.vm as unknown).$emit('update:modelValue', '')
    await nextTick()

    // click Test
    const testButton = wrapper.find('button.btn-info')
    await testButton.trigger('click')

    expect(api.testDownloadClient as unknown).toHaveBeenCalled()
    const calledWith = (api.testDownloadClient as unknown).mock.calls[0][0]
    // We still send an empty password input, but include id so backend can reuse saved credentials.
    expect(calledWith.password).toBe('')
    expect(calledWith.id).toBe('4')
  })

  it('offers only priority values the usenet planners accept', async () => {
    // The values here have to stay in step with DownloadClientPriorityTests on the backend.
    // They were not: the form offered default, last and first, none of which match an arm of
    // the switch in NzbgetRequestPlanner or SabnzbdAddRequestPlanner, so every choice but
    // Default resolved to normal and the control did nothing.
    const wrapper = mount(DownloadClientFormModal, {
      global: { plugins: [createPinia()] },
      props: { visible: true, editingClient: null },
    })

    await wrapper.setProps({
      editingClient: {
        id: '1',
        name: 'sab',
        type: 'sabnzbd',
        host: 'sabnzbd.local',
        port: 8080,
        isEnabled: true,
        useSSL: false,
      } as unknown as import('@/types').DownloadClientConfiguration,
    })
    await nextTick()

    const options = wrapper
      .findAll('#recentPriority option')
      .map((option) => option.attributes('value'))

    expect(options).toEqual(['default', 'low', 'normal', 'high', 'force'])
    expect(options).not.toContain('first')
    expect(options).not.toContain('last')

    wrapper.unmount()
  })

  it('no longer renders the superseded legacy removal checkboxes', async () => {
    // Remove Completed (Legacy) and Remove Failed (Legacy) had no backend reader at all, and
    // the Completed Download Action dropdown above them already covers removing and deleting.
    const wrapper = mount(DownloadClientFormModal, {
      global: { plugins: [createPinia()] },
      props: { visible: true, editingClient: null },
    })

    await wrapper.setProps({
      editingClient: {
        id: '1',
        name: 'sab',
        type: 'sabnzbd',
        host: 'sabnzbd.local',
        port: 8080,
        isEnabled: true,
        useSSL: false,
      } as unknown as import('@/types').DownloadClientConfiguration,
    })
    await nextTick()

    expect(wrapper.text()).not.toContain('Remove Completed (Legacy)')
    expect(wrapper.text()).not.toContain('Remove Failed (Legacy)')
    // The control: the section that replaced them is still there.
    expect(wrapper.find('#removeCompletedDownloads').exists()).toBe(true)

    wrapper.unmount()
  })

  it.each([
    ['last', 'normal'],
    ['first', 'normal'],
    ['high', 'high'],
    ['', 'default'],
  ])('reads a stored priority of %s back as %s', async (stored, expected) => {
    // Every install that ever moved this control off Default has 'last' or 'first' stored,
    // because those are the only other values the old list offered. Neither is in the new list,
    // and a select whose model holds a value none of its options carry renders with
    // selectedIndex -1 and an empty value, so the operator is shown a blank dropdown by the
    // release that fixed the control. Both fell through to normal on the wire before this
    // change, so normal is what preserves their behaviour.
    //
    // The last two rows are the control: a normaliser that simply returned 'normal' would break
    // the stored value that is already valid and the empty one that has always meant Default.
    const wrapper = mount(DownloadClientFormModal, {
      global: { plugins: [createPinia()] },
      props: { visible: true, editingClient: null },
    })

    await wrapper.setProps({
      editingClient: {
        id: '1',
        name: 'sab',
        type: 'sabnzbd',
        host: 'sabnzbd.local',
        port: 8080,
        isEnabled: true,
        useSSL: false,
        settings: stored ? { recentPriority: stored } : {},
      } as unknown as import('@/types').DownloadClientConfiguration,
    })
    await nextTick()

    const select = wrapper.find('#recentPriority').element as HTMLSelectElement
    expect(select.value).toBe(expected)
    expect(select.selectedIndex).toBeGreaterThanOrEqual(0)

    wrapper.unmount()
  })
  it('defaults Client Priority to 1 for a new client and sends it', async () => {
    const api = await import('@/services/api')
    ;(api.testDownloadClient as unknown) = vi.fn(async (config: unknown) => ({
      success: true,
      message: 'ok',
      client: config,
    }))

    const wrapper = mount(DownloadClientFormModal, {
      global: { plugins: [createPinia()] },
      props: { visible: true, editingClient: null },
    })
    await wrapper.vm.$nextTick()

    const priorityInput = wrapper.find('input[id="clientPriority"]')
    expect(priorityInput.exists()).toBe(true)
    expect((priorityInput.element as HTMLInputElement).value).toBe('1')

    await wrapper.find('button.btn-info').trigger('click')

    const calledWith = (api.testDownloadClient as unknown).mock.calls[0][0]
    expect(calledWith.priority).toBe(1)
  })

  it('round-trips an edited Client Priority into the payload', async () => {
    const api = await import('@/services/api')
    ;(api.testDownloadClient as unknown) = vi.fn(async (config: unknown) => ({
      success: true,
      message: 'ok',
      client: config,
    }))

    const wrapper = mount(DownloadClientFormModal, {
      global: { plugins: [createPinia()] },
      props: { visible: true, editingClient: null },
    })

    await wrapper.setProps({
      editingClient: {
        id: '5',
        name: 'seedbox',
        type: 'qbittorrent',
        host: 'host.local',
        port: 8080,
        isEnabled: true,
        useSSL: false,
        downloadPath: '',
        username: '',
        password: '',
        priority: 3,
        settings: {},
      },
    })
    await wrapper.vm.$nextTick()

    const priorityInput = wrapper.find('input[id="clientPriority"]')
    // A saved priority has to reach the form, otherwise editing any other field
    // silently resets it to the default on save.
    expect((priorityInput.element as HTMLInputElement).value).toBe('3')

    await priorityInput.setValue('9')
    await wrapper.find('button.btn-info').trigger('click')

    const calledWith = (api.testDownloadClient as unknown).mock.calls[0][0]
    expect(calledWith.priority).toBe(9)
  })

  it('sends Client Priority on the save path, not only the test path', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useConfigurationStore()
    store.saveDownloadClientConfiguration = vi.fn(async () => 'saved-id')

    const wrapper = mount(DownloadClientFormModal, {
      global: { plugins: [pinia] },
      props: { visible: true, editingClient: null },
    })

    await wrapper.setProps({
      editingClient: {
        id: '6',
        name: 'seedbox',
        type: 'qbittorrent',
        host: 'host.local',
        port: 8080,
        isEnabled: true,
        useSSL: false,
        downloadPath: '',
        username: '',
        password: '',
        priority: 2,
        settings: {},
      },
    })
    await wrapper.vm.$nextTick()

    await wrapper.find('input[id="clientPriority"]').setValue('6')
    // Save, not Test. The test-connection payload is a separate builder, and asserting on
    // it proves nothing about what actually gets persisted.
    await wrapper.find('button.btn-primary').trigger('click')
    await wrapper.vm.$nextTick()

    expect(store.saveDownloadClientConfiguration).toHaveBeenCalled()
    const saved = (store.saveDownloadClientConfiguration as unknown).mock.calls[0][0]
    expect(saved.priority).toBe(6)
  })

  it('never posts a blank priority, which the API cannot parse as an int', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const store = useConfigurationStore()
    store.saveDownloadClientConfiguration = vi.fn(async () => 'saved-id')

    const wrapper = mount(DownloadClientFormModal, {
      global: { plugins: [pinia] },
      props: { visible: true, editingClient: null },
    })
    await wrapper.vm.$nextTick()

    // Clearing the box makes v-model.number yield an empty string. Posting that raw is a
    // JSON parse failure on an int property, so the operator gets an opaque 400 instead of
    // the range message.
    await wrapper.find('input[id="clientPriority"]').setValue('')
    await wrapper.find('button.btn-primary').trigger('click')
    await wrapper.vm.$nextTick()

    const saved = (store.saveDownloadClientConfiguration as unknown).mock.calls[0][0]
    expect(saved.priority).toBe(1)

    // Control: a value that is out of range but parseable is clamped rather than sent on,
    // so the two paths are distinguishable.
    await wrapper.find('input[id="clientPriority"]').setValue('999')
    await wrapper.find('button.btn-primary').trigger('click')
    await wrapper.vm.$nextTick()

    const clamped = (store.saveDownloadClientConfiguration as unknown).mock.calls[1][0]
    expect(clamped.priority).toBe(50)
  })

  // Two different priorities live on this form. Client Priority orders clients against each
  // other and is read by the selector for every protocol. The queue priority is forwarded to
  // the client itself, and only the SABnzbd and NZBGet planners read it. They are kept in
  // separate sections so the heading never claims one is the other.
  const sectionOf = (wrapper: ReturnType<typeof mount>, selector: string) =>
    wrapper.findAll('.form-section').find((section) => section.find(selector).exists())

  it('keeps Client Priority and the usenet queue priority in separate sections', async () => {
    const wrapper = mount(DownloadClientFormModal, {
      global: { plugins: [createPinia()] },
      props: { visible: true, editingClient: null },
    })
    await wrapper.setProps({
      editingClient: {
        id: '1',
        name: 'sab',
        type: 'sabnzbd',
        host: 'sabnzbd.local',
        port: 8080,
        isEnabled: true,
        useSSL: false,
      } as unknown as import('@/types').DownloadClientConfiguration,
    })
    await nextTick()

    const clientSection = sectionOf(wrapper, '#clientPriority')
    const queueSection = sectionOf(wrapper, '#recentPriority')
    expect(clientSection).toBeDefined()
    expect(queueSection).toBeDefined()
    expect(clientSection!.element).not.toBe(queueSection!.element)
    expect(clientSection!.find('h3').text()).toBe('Client Selection')
    expect(queueSection!.find('h3').text()).toBe('Queue Priority')

    wrapper.unmount()
  })

  it('shows Client Priority for a torrent client but not the usenet-only queue priority', async () => {
    // Control for the test above: the queue priority section must still disappear for a
    // protocol whose planner never reads recentPriority, so widening Client Priority to every
    // client did not widen this with it.
    const wrapper = mount(DownloadClientFormModal, {
      global: { plugins: [createPinia()] },
      props: { visible: true, editingClient: null },
    })
    await wrapper.setProps({
      editingClient: {
        id: '2',
        name: 'qbit',
        type: 'qbittorrent',
        host: 'qbittorrent.local',
        port: 8080,
        isEnabled: true,
        useSSL: false,
      } as unknown as import('@/types').DownloadClientConfiguration,
    })
    await nextTick()

    expect(wrapper.find('#clientPriority').exists()).toBe(true)
    expect(wrapper.find('#recentPriority').exists()).toBe(false)
    const headings = wrapper.findAll('.form-section h3').map((h) => h.text())
    expect(headings).toContain('Client Selection')
    expect(headings).not.toContain('Queue Priority')

    wrapper.unmount()
  })
})
