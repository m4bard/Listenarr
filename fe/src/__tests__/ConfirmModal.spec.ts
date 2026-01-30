import { mount } from '@vue/test-utils'
import { describe, it, expect } from 'vitest'
import { ConfirmModal } from '@/components/modal' 

describe('ConfirmModal', () => {
  it('renders message and emits confirm', async () => {
    const wrapper = mount(ConfirmModal, { props: { visible: true, message: 'Are you sure?', confirmLabel: 'Yes' } })
    expect(wrapper.text()).toContain('Are you sure?')
    // find save/confirm button
    const btn = wrapper.find('button.btn-primary')
    expect(btn.exists()).toBe(true)
    await btn.trigger('click')
    expect(wrapper.emitted()).toHaveProperty('confirm')
  })
})