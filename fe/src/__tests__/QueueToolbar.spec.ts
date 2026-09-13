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
import { mount } from '@vue/test-utils'
import { describe, it, expect } from 'vitest'
import { nextTick } from 'vue'
import QueueToolbar from '@/components/domain/download/QueueToolbar.vue'
import { ConfirmModal } from '@/components/feedback'

const mountToolbar = (selected: Array<{ id: string; status: string }> = []) =>
  mount(QueueToolbar, { props: { selected } })

describe('QueueToolbar', () => {
  it('hides the selection verbs when nothing is selected and keeps the sweeps', () => {
    const wrapper = mountToolbar()
    expect(wrapper.find('[data-test="queue-remove-selected"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="queue-retry-selected"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="queue-clear-selection"]').exists()).toBe(false)
    expect(wrapper.find('[data-test="queue-clear-completed"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="queue-clear-failed"]').exists()).toBe(true)
  })

  it('shows the count and enables Retry when every selected row is import blocked', () => {
    const wrapper = mountToolbar([
      { id: 'a', status: 'importblocked' },
      { id: 'b', status: 'ImportBlocked' },
    ])
    expect(wrapper.get('[data-test="queue-selected-count"]').text()).toContain('2')
    expect(wrapper.get('[data-test="queue-retry-selected"]').attributes('disabled')).toBeUndefined()
  })

  it('disables Retry when one selected row is not import blocked', () => {
    const wrapper = mountToolbar([
      { id: 'a', status: 'importblocked' },
      { id: 'b', status: 'downloading' },
    ])
    expect(wrapper.get('[data-test="queue-retry-selected"]').attributes('disabled')).toBeDefined()
  })

  it('emits remove-selected only once the confirmation is accepted', async () => {
    const wrapper = mountToolbar([{ id: 'a', status: 'downloading' }])
    await wrapper.get('[data-test="queue-remove-selected"]').trigger('click')
    expect(wrapper.emitted('remove-selected')).toBeUndefined()

    wrapper.findComponent(ConfirmModal).vm.$emit('confirm')
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('remove-selected')).toHaveLength(1)
  })

  it('drops the pending action when the confirmation is cancelled', async () => {
    const wrapper = mountToolbar([{ id: 'a', status: 'downloading' }])
    await wrapper.get('[data-test="queue-remove-selected"]').trigger('click')
    wrapper.findComponent(ConfirmModal).vm.$emit('cancel')
    await wrapper.vm.$nextTick()
    expect(wrapper.findComponent(ConfirmModal).props('visible')).toBe(false)
    expect(wrapper.emitted('remove-selected')).toBeUndefined()
  })

  it('warns that clearing failed downloads also removes import blocked ones', async () => {
    const wrapper = mountToolbar()
    await wrapper.get('[data-test="queue-clear-failed"]').trigger('click')

    const message = wrapper.findComponent(ConfirmModal).props('message') as string
    expect(message.toLowerCase()).toContain('import blocked')

    wrapper.findComponent(ConfirmModal).vm.$emit('confirm')
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('clear-failed')).toHaveLength(1)
  })

  it('discloses the true counts for the two unbounded sweeps, not just the selection', async () => {
    const wrapper = mount(QueueToolbar, {
      props: { selected: [], completedCount: 0, failedCount: 1709, importBlockedCount: 33 },
    })

    expect(wrapper.get('[data-test="queue-clear-completed"]').text()).toContain('0')
    expect(wrapper.get('[data-test="queue-clear-failed"]').text()).toContain('1709')
    expect(wrapper.get('[data-test="queue-clear-failed"]').text()).toContain('33')

    await wrapper.get('[data-test="queue-clear-failed"]').trigger('click')
    const message = wrapper.findComponent(ConfirmModal).props('message') as string
    expect(message).toContain('1709')
    expect(message).toContain('33')
  })

  it('says the completed sweep is not limited to the selection', async () => {
    const wrapper = mountToolbar([{ id: 'a', status: 'completed' }])
    await wrapper.get('[data-test="queue-clear-completed"]').trigger('click')
    expect(
      (wrapper.findComponent(ConfirmModal).props('message') as string).toLowerCase(),
    ).toContain('every completed download')
  })

  it('closes the remove confirmation when the selection empties underneath it', async () => {
    const wrapper = mountToolbar([{ id: 'a', status: 'downloading' }])
    await wrapper.get('[data-test="queue-remove-selected"]').trigger('click')
    expect(wrapper.findComponent(ConfirmModal).props('visible')).toBe(true)

    await wrapper.setProps({ selected: [] })
    await nextTick()

    expect(wrapper.findComponent(ConfirmModal).props('visible')).toBe(false)
    expect(wrapper.emitted('remove-selected')).toBeUndefined()
  })

  it('disables every toolbar button while a bulk run is busy', () => {
    const wrapper = mount(QueueToolbar, {
      props: { selected: [{ id: 'a', status: 'importblocked' }], busy: true },
    })

    const buttons = wrapper.findAll('.toolbar-btn')
    expect(buttons).toHaveLength(5)
    for (const button of buttons) {
      expect(button.attributes('disabled')).toBeDefined()
    }
  })
})
