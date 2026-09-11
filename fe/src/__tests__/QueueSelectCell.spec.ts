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
import QueueSelectCell from '@/components/domain/download/QueueSelectCell.vue'

describe('QueueSelectCell', () => {
  it('reports the new checked state when it is clicked', async () => {
    const wrapper = mount(QueueSelectCell, { props: { label: 'Select row' } })
    await wrapper.get('input').setValue(true)
    expect(wrapper.emitted('change')).toEqual([[true]])
  })

  it('carries the label for a screen reader and the test hook', () => {
    const wrapper = mount(QueueSelectCell, {
      props: { label: 'Select all downloads in view', dataTest: 'queue-select-all' },
    })
    const input = wrapper.get('input')
    expect(input.attributes('aria-label')).toBe('Select all downloads in view')
    expect(input.attributes('data-test')).toBe('queue-select-all')
  })

  it('shows a partial selection as indeterminate and a full one as checked', async () => {
    const wrapper = mount(QueueSelectCell, {
      props: { label: 'Select all', indeterminate: true, checked: false },
    })
    expect((wrapper.get('input').element as HTMLInputElement).indeterminate).toBe(true)

    await wrapper.setProps({ checked: true })
    expect((wrapper.get('input').element as HTMLInputElement).indeterminate).toBe(false)
  })

  it('reasserts its own checked prop after a click that does not change it', async () => {
    const wrapper = mount(QueueSelectCell, { props: { label: 'Select all', checked: false } })
    await wrapper.get('input').setValue(true)
    await nextTick()

    expect(wrapper.emitted('change')).toEqual([[true]])
    expect((wrapper.get('input').element as HTMLInputElement).checked).toBe(false)
  })

  it('renders the input disabled when the cell is disabled', () => {
    const wrapper = mount(QueueSelectCell, {
      props: { label: 'Select all downloads in view', disabled: true },
    })
    expect(wrapper.get('input').attributes('disabled')).toBeDefined()
  })
})
