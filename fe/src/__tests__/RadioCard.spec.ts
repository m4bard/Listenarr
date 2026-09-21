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
import RadioCard from '@/components/settings/RadioCard.vue'

// This file pins the refusal where it lives. The section that uses it holds the same property a
// second time, so a test written only at the section level stays green while this layer is
// removed, and nothing tells anybody the redundancy has quietly become a single point.
function mountCard(disabled: boolean) {
  return mount(RadioCard, {
    props: { modelValue: 'other', value: 'mine', title: 'Mine', name: 'group', disabled },
  })
}

describe('RadioCard', () => {
  it('refuses both routes into a disabled card', async () => {
    const wrapper = mountCard(true)

    expect((wrapper.find('input[type="radio"]').element as HTMLInputElement).disabled).toBe(true)
    expect(wrapper.find('.radio-label').classes()).toContain('disabled')

    // The label click is the route the disabled attribute does not close, because the browser
    // still delivers a click to a label wrapping a disabled input.
    await wrapper.find('.radio-label').trigger('click')
    await wrapper.find('input[type="radio"]').trigger('change')

    expect(wrapper.emitted()['update:modelValue']).toBeUndefined()
  })

  it('takes the same two routes when the card is not disabled, which is the control', async () => {
    const wrapper = mountCard(false)

    expect((wrapper.find('input[type="radio"]').element as HTMLInputElement).disabled).toBe(false)
    expect(wrapper.find('.radio-label').classes()).not.toContain('disabled')

    await wrapper.find('.radio-label').trigger('click')

    const emitted = wrapper.emitted()['update:modelValue']
    expect(emitted?.length).toBeGreaterThan(0)
    expect(emitted![0][0]).toBe('mine')
  })

  it('leaves a card with no disabled prop selectable, which is how every other caller uses it', async () => {
    const wrapper = mount(RadioCard, {
      props: { modelValue: false, value: true, title: 'Monitored' },
    })

    expect(wrapper.find('input[type="radio"]').attributes('disabled')).toBeUndefined()
    await wrapper.find('input[type="radio"]').trigger('change')
    expect(wrapper.emitted()['update:modelValue']![0][0]).toBe(true)
  })
})
