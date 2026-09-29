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
import { mount, flushPromises } from '@vue/test-utils'
import { describe, it, expect, vi, afterEach } from 'vitest'
import ManualSearchModal from '@/components/domain/search/ManualSearchModal.vue'
import { apiService } from '@/services/api'

// "No results found" used to cover both an indexer that answered with nothing and one that could
// not be asked. The case that matters most is the search with SOME results, which looks complete
// and is not, so the could-not-search line must appear whatever the result count. Names invented.

// The shared test setup replaces the api module; give it the members this modal calls so they can
// be spied on.
const api = apiService as unknown as Record<string, unknown>
for (const name of [
  'getEnabledIndexers',
  'searchByApi',
  'getDefaultQualityProfile',
  'scoreSearchResults',
]) {
  if (!api[name]) api[name] = async () => []
}

const stubs = {
  Modal: { template: '<div><slot name="header" /><slot /></div>' },
  ModalHeader: { template: '<div><slot /></div>' },
  ModalBody: { template: '<div><slot /></div>' },
  ScorePopover: { template: '<div><slot /></div>' },
}

const indexers = [
  { id: 1, name: 'Alderbrook', implementation: 'Torznab', additionalSettings: null },
  { id: 2, name: 'Birchfield', implementation: 'Torznab', additionalSettings: null },
  { id: 3, name: 'Cobblestone', implementation: 'Torznab', additionalSettings: null },
]

const hit = (guid: string) => ({
  guid,
  title: `Release ${guid}`,
  size: 1024,
  publishDate: new Date().toISOString(),
  indexer: 'Alderbrook',
  indexerId: 1,
})

// failureReason is left out of the JSON when null (the controllers ignore nulls when writing).
type Answer = unknown[] | { results: unknown[]; answered: boolean; failureReason?: string | null }

const searchWith = async (answers: Record<string, Answer | Error>) => {
  vi.spyOn(apiService, 'getEnabledIndexers').mockResolvedValue(indexers as never)
  // Mocked one level down, at searchByApi, so the modal's own call path (and the fallback for a
  // backend that returns the bare list) is what is under test.
  vi.spyOn(apiService, 'searchByApi').mockImplementation(async (apiId: string) => {
    const answer = answers[apiId]
    if (answer instanceof Error) throw answer
    return (answer ?? []) as never
  })
  vi.spyOn(apiService, 'getDefaultQualityProfile').mockResolvedValue({ id: 1 } as never)
  vi.spyOn(apiService, 'scoreSearchResults').mockResolvedValue([] as never)

  const wrapper = mount(ManualSearchModal, {
    props: { isOpen: false, audiobook: { id: 7, title: 'The Salt Road', authors: ['Imre Vale'] } },
    global: { stubs },
  })
  await wrapper.setProps({ isOpen: true })
  await flushPromises()
  await (wrapper.vm as unknown as { search: () => Promise<void> }).search()
  await flushPromises()
  return wrapper
}

describe('ManualSearchModal could-not-search reporting', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('names the indexer that timed out even when others returned results', async () => {
    const wrapper = await searchWith({
      '1': { results: [hit('a1')], answered: true, failureReason: null },
      '2': { results: [], answered: false, failureReason: 'Timeout' },
      '3': { results: [], answered: true, failureReason: null },
    })

    const line = wrapper.find('[data-testid="could-not-search"]')
    expect(line.exists()).toBe(true)
    expect(line.text()).toContain('Birchfield (Timeout)')
    expect(line.text()).toContain('1 of 3')
    // Control: an indexer that answered with nothing is not named as a failure
    expect(line.text()).not.toContain('Cobblestone')
    // The result that did come back is still shown
    expect(wrapper.text()).toContain('1 result found')
  })

  it('counts a request that errored as could not search', async () => {
    const wrapper = await searchWith({
      '1': { results: [hit('a1')], answered: true, failureReason: null },
      '2': new Error('API error: 500'),
      '3': { results: [], answered: true, failureReason: null },
    })

    expect(wrapper.find('[data-testid="could-not-search"]').text()).toContain('Birchfield')
  })

  it('says no indexer could be searched instead of no results when every one failed', async () => {
    const wrapper = await searchWith({
      '1': { results: [], answered: false, failureReason: 'Timeout' },
      '2': { results: [], answered: false, failureReason: 'HttpStatus' },
      '3': { results: [], answered: false, failureReason: 'NetworkError' },
    })

    expect(wrapper.text()).toContain('No indexer could be searched')
    expect(wrapper.text()).not.toContain('No results found')
  })

  it('keeps "No results found" when every indexer answered with nothing', async () => {
    // Known-good: a genuinely empty search must not be dressed up as a failure
    const wrapper = await searchWith({
      '1': { results: [], answered: true, failureReason: null },
      '2': { results: [], answered: true, failureReason: null },
      '3': { results: [], answered: true, failureReason: null },
    })

    expect(wrapper.text()).toContain('No results found')
    expect(wrapper.find('[data-testid="could-not-search"]').exists()).toBe(false)
  })

  it('names the indexer without a parenthetical when no reason came back', async () => {
    const wrapper = await searchWith({
      '1': { results: [hit('a1')], answered: true },
      '2': { results: [], answered: false },
      '3': { results: [], answered: true },
    })

    const text = wrapper.find('[data-testid="could-not-search"]').text()
    expect(text).toContain('Birchfield')
    expect(text).not.toContain('Birchfield (')
  })

  it('words an indexer that is not usable as configured for a person', async () => {
    const wrapper = await searchWith({
      '1': { results: [hit('a1')], answered: true },
      '2': { results: [], answered: false, failureReason: 'NotConfigured' },
      '3': { results: [], answered: true },
    })

    expect(wrapper.find('[data-testid="could-not-search"]').text()).toContain(
      'Birchfield (not configured)',
    )
  })

  it('counts an indexer once when handling its unanswered result also throws', async () => {
    // A null entry makes the result normalisation throw after the indexer was already recorded
    const wrapper = await searchWith({
      '1': { results: [hit('a1')], answered: true },
      '2': { results: [null], answered: false, failureReason: 'Timeout' },
      '3': { results: [], answered: true },
    })

    const text = wrapper.find('[data-testid="could-not-search"]').text()
    expect(text).toContain('1 of 3')
    expect(text.match(/Birchfield/g)).toHaveLength(1)
  })

  it('treats a bare list from an older backend as answered', async () => {
    const wrapper = await searchWith({ '1': [hit('a1')], '2': [], '3': [] })

    expect(wrapper.find('[data-testid="could-not-search"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('1 result found')
  })
})
