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
import QualityProfileFormModal from '@/components/settings/QualityProfileFormModal.vue'
import type { QualityProfile } from '@/types'

describe('QualityProfileFormModal release shape preference', () => {
  const mountModal = (profile: QualityProfile | null) =>
    mount(QualityProfileFormModal, {
      props: { visible: true, profile },
      global: { stubs: { Teleport: true } },
    })

  it('defaults a new profile to no preference', () => {
    const select = mountModal(null).find('select#preferredReleaseShape')

    expect(select.exists()).toBe(true)
    expect((select.element as HTMLSelectElement).value).toBe('none')
  })

  it('offers all three shapes', () => {
    const options = mountModal(null)
      .find('select#preferredReleaseShape')
      .findAll('option')
      .map((option) => option.attributes('value'))

    expect(options).toEqual(['none', 'individual', 'bundle'])
  })

  // Mounted with no profile and then given one, which is how QualityProfilesTab uses it:
  // the modal stays mounted and the prop changes when a profile is opened for editing.
  const openProfile = async (profile: QualityProfile) => {
    const wrapper = mountModal(null)
    await wrapper.setProps({ profile })
    return wrapper
  }

  it('loads the value an existing profile was saved with', async () => {
    const wrapper = await openProfile({
      id: 1,
      name: 'Completionist',
      preferredReleaseShape: 'bundle',
    } as QualityProfile)

    const select = wrapper.find('select#preferredReleaseShape')
    expect((select.element as HTMLSelectElement).value).toBe('bundle')
  })

  it('falls back to no preference for a profile saved before the setting existed', async () => {
    const wrapper = await openProfile({ id: 2, name: 'Older profile' } as QualityProfile)

    const select = wrapper.find('select#preferredReleaseShape')
    expect((select.element as HTMLSelectElement).value).toBe('none')
  })

  it('carries a changed value into the saved payload', async () => {
    const wrapper = mountModal(null)
    const select = wrapper.find('select#preferredReleaseShape')

    await select.setValue('individual')

    // Reading the form state rather than driving the whole submit, which validates
    // qualities and a cutoff that have nothing to do with this setting.
    expect((select.element as HTMLSelectElement).value).toBe('individual')
    expect(
      (wrapper.vm as unknown as { formData: QualityProfile }).formData.preferredReleaseShape,
    ).toBe('individual')
  })
})
