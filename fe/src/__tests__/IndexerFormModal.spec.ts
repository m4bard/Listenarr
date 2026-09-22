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
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import IndexerFormModal from '@/components/settings/IndexerFormModal.vue'

describe('IndexerFormModal', () => {
  it('renders API key input as PasswordInput for Newznab/Torznab', async () => {
    const wrapper = mount(IndexerFormModal, {
      global: { plugins: [createPinia()] },
      props: { visible: true, editingIndexer: null },
    })

    await wrapper.setProps({
      editingIndexer: {
        id: 1,
        name: 'Test Indexer',
        implementation: 'Newznab',
        url: 'https://example.test',
        apiKey: 'secret',
      } as unknown,
    })
    await wrapper.vm.$nextTick()

    // PasswordInput is a child component; assert it exists and its `modelValue` is populated
    const pwdComp = wrapper.findComponent({ name: 'PasswordInput' })
    expect(pwdComp.exists()).toBe(true)
    expect(pwdComp.props('modelValue')).toBe('secret')
  })

  it('shows seed criteria fields for a new (torrent) indexer', () => {
    const wrapper = mount(IndexerFormModal, {
      global: { plugins: [createPinia()] },
      props: { visible: true, editingIndexer: null },
    })

    expect(wrapper.find('#seedRatio').exists()).toBe(true)
    expect(wrapper.find('#seedTime').exists()).toBe(true)
  })

  it('hides seed criteria fields for a Usenet (Newznab) indexer', async () => {
    const wrapper = mount(IndexerFormModal, {
      global: { plugins: [createPinia()] },
      props: { visible: true, editingIndexer: null },
    })

    await wrapper.setProps({
      editingIndexer: {
        id: 1,
        name: 'Test Indexer',
        type: 'Usenet',
        implementation: 'Newznab',
        url: 'https://example.test',
        apiKey: 'secret',
      } as unknown,
    })
    await wrapper.vm.$nextTick()

    expect(wrapper.find('#seedRatio').exists()).toBe(false)
    expect(wrapper.find('#seedTime').exists()).toBe(false)
  })

  it('warns when seed ratio is set to exactly zero, and clears the warning once left empty', async () => {
    const wrapper = mount(IndexerFormModal, {
      global: { plugins: [createPinia()] },
      props: { visible: true, editingIndexer: null },
    })

    const seedRatioInput = wrapper.find('#seedRatio')
    await seedRatioInput.setValue('0')

    expect(wrapper.text()).toContain('Seed ratio should be greater than zero')

    await seedRatioInput.setValue('')

    expect(wrapper.text()).not.toContain('Seed ratio should be greater than zero')
  })
})
