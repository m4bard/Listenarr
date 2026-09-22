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
import QualityProfileFormModal from '@/components/settings/QualityProfileFormModal.vue'

describe('minimum score threshold', () => {
  // The help text beside this field is where an operator learns the range, because the score
  // itself is not on screen: the search views display the composite smart score, not this one.
  it('accepts a threshold above 100, because an accepted release scores above 100', () => {
    const wrapper = mount(QualityProfileFormModal, {
      global: { plugins: [createPinia()] },
      props: { visible: true, profile: null },
    })

    const input = wrapper.find('#minimumScore')
    expect(input.exists()).toBe(true)
    expect(input.attributes('max')).toBeUndefined()
    expect(input.attributes('min')).toBe('0')
  })
})
