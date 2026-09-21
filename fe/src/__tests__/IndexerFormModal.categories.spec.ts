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
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import IndexerFormModal from '@/components/settings/IndexerFormModal.vue'
import { useToast } from '@/services/toastService'

const createIndexer = vi.fn(async (payload: unknown) => payload)
const updateIndexer = vi.fn(async (_id: number, payload: unknown) => payload)
const testIndexerDraft = vi.fn(async () => ({ success: true, message: 'ok' }))

vi.mock('@/services/api', () => ({
  createIndexer: (payload: unknown) => createIndexer(payload),
  updateIndexer: (id: number, payload: unknown) => updateIndexer(id, payload),
  testIndexerDraft: (payload: unknown) => testIndexerDraft(payload),
}))

const AUDIOBOOK_CATEGORY = '3030'

// Kept identical to IndexerCategoriesRequiredAttribute's message in the backend, so this
// fixture stays a faithful stand-in for what the server actually sends.
const VALIDATION_MESSAGE =
  'At least one category is required. Without one, Newznab and Torznab search every category, ' +
  'so unrelated releases can match.'

const mountModal = () =>
  mount(IndexerFormModal, {
    global: { plugins: [createPinia()] },
    props: { visible: true, editingIndexer: null },
  })

const categoriesInput = (wrapper: ReturnType<typeof mountModal>) =>
  wrapper.find('input#categories').element as HTMLInputElement

const formCategories = (wrapper: ReturnType<typeof mountModal>) =>
  (wrapper.vm as unknown as { formData: { categories: string } }).formData.categories

// The Test Connection button lives in a footer slot the mounted tree does not expose, so the
// handler it emits into is invoked directly. Same reach into the component as formCategories.
const testConnection = (wrapper: ReturnType<typeof mountModal>) =>
  (wrapper.vm as unknown as { testConnection: () => Promise<void> }).testConnection()

const toast = useToast()

const validationProblemError = () =>
  Object.assign(new Error(`API error: 400 {"errors":{"Categories":[]}}`), {
    status: 400,
    body: JSON.stringify({
      type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
      title: 'One or more validation errors occurred.',
      status: 400,
      errors: { Categories: [VALIDATION_MESSAGE] },
    }),
  })

describe('IndexerFormModal categories', () => {
  beforeEach(() => {
    createIndexer.mockReset()
    createIndexer.mockImplementation(async (payload: unknown) => payload)
    updateIndexer.mockReset()
    testIndexerDraft.mockReset()
    toast.toasts.splice(0, toast.toasts.length)
  })

  it('pre-fills the audiobook category for a new Newznab indexer', async () => {
    const wrapper = mountModal()

    await wrapper.find('select#implementation').setValue('Newznab')
    await wrapper.vm.$nextTick()

    expect(categoriesInput(wrapper).value).toBe(AUDIOBOOK_CATEGORY)
  })

  it('pre-fills the audiobook category for a new Torznab indexer', async () => {
    const wrapper = mountModal()
    await wrapper.vm.$nextTick()

    expect(categoriesInput(wrapper).value).toBe(AUDIOBOOK_CATEGORY)
  })

  it('keeps a category list the user typed when the implementation changes', async () => {
    const wrapper = mountModal()

    await wrapper.find('input#categories').setValue('3030,3040')
    await wrapper.find('select#implementation').setValue('Newznab')
    await wrapper.vm.$nextTick()

    expect(categoriesInput(wrapper).value).toBe('3030,3040')
  })

  it('does not force categories onto a new Internet Archive indexer', async () => {
    const wrapper = mountModal()

    await wrapper.find('input#name').setValue('Archive')
    await wrapper.find('select#implementation').setValue('InternetArchive')
    await wrapper.vm.$nextTick()

    // The field is hidden for Internet Archive, and the default must not be left behind it.
    expect(wrapper.find('input#categories').exists()).toBe(false)
    expect(formCategories(wrapper)).toBe('')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(createIndexer).toHaveBeenCalledTimes(1)
    const payload = createIndexer.mock.calls[0][0] as { categories?: string }
    expect(payload.categories).toBe('')
  })

  it('does not pre-fill categories when editing an existing indexer that has none', async () => {
    const wrapper = mountModal()

    await wrapper.setProps({
      editingIndexer: {
        id: 7,
        name: 'Legacy',
        type: 'Usenet',
        implementation: 'Newznab',
        url: 'https://example.test',
        // Stored before categories were required, so the API omits the property entirely.
      } as never,
    })
    await wrapper.vm.$nextTick()

    expect(categoriesInput(wrapper).value).toBe('')
  })

  it('surfaces the server validation message for a 400 keyed to Categories', async () => {
    createIndexer.mockRejectedValueOnce(validationProblemError())

    const wrapper = mountModal()
    await wrapper.find('input#name').setValue('Broken')
    await wrapper.find('input#categories').setValue('')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(toast.toasts[0]?.title).toBe('Save failed')
    expect(toast.toasts[0]?.message).toBe(VALIDATION_MESSAGE)
    expect(toast.toasts[0]?.message).not.toBe('Failed to save indexer')

    const fieldError = wrapper.find('.error-text')
    expect(fieldError.exists()).toBe(true)
    expect(fieldError.text()).toBe(VALIDATION_MESSAGE)
  })

  it('clears the field error once the categories are edited', async () => {
    createIndexer.mockRejectedValueOnce(validationProblemError())

    const wrapper = mountModal()
    await wrapper.find('input#name').setValue('Broken')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.find('.error-text').exists()).toBe(true)

    await wrapper.find('input#categories').setValue('3030,3040')
    await wrapper.vm.$nextTick()

    expect(wrapper.find('.error-text').exists()).toBe(false)
  })

  it('falls back to a readable toast when the error body is not JSON', async () => {
    createIndexer.mockRejectedValueOnce(
      Object.assign(new Error('API error: 502 '), {
        status: 502,
        body: '<html><body>Bad Gateway</body></html>',
      }),
    )

    const wrapper = mountModal()
    await wrapper.find('input#name').setValue('Gateway')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(toast.toasts[0]?.title).toBe('Save failed')
    expect(toast.toasts[0]?.message).toBe('<html><body>Bad Gateway</body></html>')
    expect(wrapper.find('.error-text').exists()).toBe(false)
  })

  it('falls back to the error message when the failure carries no body at all', async () => {
    createIndexer.mockRejectedValueOnce(new Error('Failed to fetch'))

    const wrapper = mountModal()
    await wrapper.find('input#name').setValue('Offline')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(toast.toasts[0]?.title).toBe('Save failed')
    expect(toast.toasts[0]?.message).toBe('Failed to fetch')
    expect(wrapper.find('.error-text').exists()).toBe(false)
  })

  it('falls back to the generic message when the failure carries nothing usable', async () => {
    createIndexer.mockRejectedValueOnce({ status: 500, body: '   ' })

    const wrapper = mountModal()
    await wrapper.find('input#name').setValue('Empty')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(toast.toasts[0]?.title).toBe('Save failed')
    expect(toast.toasts[0]?.message).toBe('Failed to save indexer')
  })

  // Test Connection posts the draft indexer, so the same validation that refuses a save refuses
  // it. That body carries neither `message` nor `error`, which was all this handler read.
  it('surfaces the server validation message when Test Connection is refused', async () => {
    testIndexerDraft.mockRejectedValueOnce(validationProblemError())

    const wrapper = mountModal()
    await wrapper.find('input#name').setValue('Broken')
    await wrapper.find('input#categories').setValue('')
    await testConnection(wrapper)
    await flushPromises()

    expect(toast.toasts[0]?.title).toBe('Test failed')
    expect(toast.toasts[0]?.message).toBe(VALIDATION_MESSAGE)
    expect(toast.toasts[0]?.message).not.toBe('Failed to test indexer connection')

    const fieldError = wrapper.find('.error-text')
    expect(fieldError.exists()).toBe(true)
    expect(fieldError.text()).toBe(VALIDATION_MESSAGE)
  })

  // The control for the test above: an ordinary connection failure still reports its own
  // message, so the new reader has not swallowed the shape this handler already understood.
  it('still surfaces an ordinary connection failure from Test Connection', async () => {
    testIndexerDraft.mockRejectedValueOnce(
      Object.assign(new Error('API error: 400'), {
        status: 400,
        body: JSON.stringify({
          success: false,
          message: 'Indexer test failed',
          error: 'No such host',
        }),
      }),
    )

    const wrapper = mountModal()
    await wrapper.find('input#name').setValue('Unreachable')
    await testConnection(wrapper)
    await flushPromises()

    expect(toast.toasts[0]?.title).toBe('Test failed')
    expect(toast.toasts[0]?.message).toBe('Indexer test failed')
    expect(wrapper.find('.error-text').exists()).toBe(false)
  })
})
