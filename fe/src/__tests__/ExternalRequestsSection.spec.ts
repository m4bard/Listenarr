import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'

import Checkbox from '@/components/form/Checkbox.vue'
import PasswordInput from '@/components/form/PasswordInput.vue'
import { Modal, ModalHeader, ModalFooter } from '@/components/feedback'

describe('ExternalRequestsSection', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('emits update:settings when toggling useUsProxy and setting host/port', async () => {
    const { default: ExternalRequestsSection } = await import('@/components/settings/ExternalRequestsSection.vue')
    const wrapper = mount(ExternalRequestsSection, {
      props: { settings: { preferUsDomain: false } },
      global: { components: { Checkbox, Modal } },
    })

    const checkbox = wrapper.find('input[type="checkbox"]')
    await checkbox.setValue(true)

    expect(wrapper.emitted()['update:settings']).toBeTruthy()
    const last = wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.preferUsDomain).toBe(true)
  })
})
