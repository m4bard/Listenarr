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
import { mount } from '@vue/test-utils'
import Checkbox from '@/components/form/Checkbox.vue'
import RadioCard from '@/components/settings/RadioCard.vue'
import type { ApplicationSettings } from '@/types'

// Deliberately all different from each other and from the shipped defaults, so a control bound
// to the wrong key shows the wrong number instead of accidentally matching.
const previewing: Partial<ApplicationSettings> = {
  authorIdentityRepairEnabled: true,
  authorIdentityRepairDryRun: true,
  authorIdentityRepairIntervalHours: 6,
  authorIdentityRepairMaxRowsPerRun: 40,
  authorIdentityRepairRecheckAfterDays: 14,
}

const OFF = 0
const PREVIEW = 1
const REPAIR = 2

async function mountSection(overrides: Partial<ApplicationSettings> = {}) {
  const { default: AuthorIdentityRepairSection } =
    await import('@/components/settings/AuthorIdentityRepairSection.vue')
  return mount(AuthorIdentityRepairSection, {
    props: { settings: { ...previewing, ...overrides } },
    global: { components: { Checkbox } },
  })
}

type Wrapper = Awaited<ReturnType<typeof mountSection>>

function payloads(wrapper: Wrapper) {
  return (wrapper.emitted()['update:settings'] ?? []) as Array<[Partial<ApplicationSettings>]>
}

function lastPayload(wrapper: Wrapper) {
  const emitted = payloads(wrapper)
  return emitted[emitted.length - 1][0]
}

// The mount-time correction lands a tick after mount, so the section is behind the settings tab
// rather than inside its sync window. Anything asserting on it has to get past that tick.
const flushTicks = () => new Promise((r) => setTimeout(r, 0))

// Clicking the card, not the input. The label is the surface an operator actually hits, and it
// is the route a disabled attribute on the input does not close.
async function clickCard(wrapper: Wrapper, index: number) {
  await wrapper.findAll('.radio-label')[index].trigger('click')
}

describe('AuthorIdentityRepairSection', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('renders the stored state: previewing, with the three numbers it was given', async () => {
    const wrapper = await mountSection()

    const radios = wrapper.findAll('input[type="radio"]')
    expect(radios).toHaveLength(3)
    expect(radios.map((r) => (r.element as HTMLInputElement).checked)).toEqual([false, true, false])

    const inputs = wrapper.findAll('input[type="number"]')
    expect(inputs).toHaveLength(3)
    expect(inputs.map((i) => (i.element as HTMLInputElement).value)).toEqual(['6', '40', '14'])
  })

  it('renders the off state rather than forcing the pass on', async () => {
    const wrapper = await mountSection({ authorIdentityRepairEnabled: false })

    const radios = wrapper.findAll('input[type="radio"]')
    expect(radios.map((r) => (r.element as HTMLInputElement).checked)).toEqual([true, false, false])
  })

  it('renders the repair state when that is what is stored', async () => {
    const wrapper = await mountSection({ authorIdentityRepairDryRun: false })

    const radios = wrapper.findAll('input[type="radio"]')
    expect(radios.map((r) => (r.element as HTMLInputElement).checked)).toEqual([false, false, true])
  })

  it('reads a disabled pass as off even when the dry run switch is stored off', async () => {
    // The fourth stored combination of two switches. It behaves as off, because the cycle body
    // returns before it reads the dry run flag, so showing anything else would be a lie.
    const wrapper = await mountSection({
      authorIdentityRepairEnabled: false,
      authorIdentityRepairDryRun: false,
    })

    const radios = wrapper.findAll('input[type="radio"]')
    expect((radios[OFF].element as HTMLInputElement).checked).toBe(true)
    expect((radios[REPAIR].element as HTMLInputElement).checked).toBe(false)
  })

  it('writes both switches when the operator picks preview, not just the enable one', async () => {
    // Starting from the combination above. An implementation that only wrote the enable switch
    // would leave the dry run stored off, so the section would say preview while the next
    // cycle rewrote author records.
    const wrapper = await mountSection({
      authorIdentityRepairEnabled: false,
      authorIdentityRepairDryRun: false,
    })

    await clickCard(wrapper, PREVIEW)

    const payload = lastPayload(wrapper)
    expect(payload.authorIdentityRepairEnabled).toBe(true)
    expect(payload.authorIdentityRepairDryRun).toBe(true)
  })

  it('posts what the operator chose when the pass is switched off', async () => {
    const wrapper = await mountSection()

    await clickCard(wrapper, OFF)

    const payload = lastPayload(wrapper)
    expect(payload.authorIdentityRepairEnabled).toBe(false)
    expect(payload.authorIdentityRepairDryRun).toBe(true)
    // The rest of the section rides along untouched.
    expect(payload.authorIdentityRepairIntervalHours).toBe(6)
    expect(payload.authorIdentityRepairMaxRowsPerRun).toBe(40)
    expect(payload.authorIdentityRepairRecheckAfterDays).toBe(14)
  })

  it('does not let one click reach the repair state', async () => {
    const wrapper = await mountSection()

    const repairInput = wrapper.findAll('input[type="radio"]')[REPAIR].element as HTMLInputElement
    expect(repairInput.disabled).toBe(true)

    // Both routes into the option. Two guards stand behind this and either one alone is enough,
    // which is why the assertion is on the section emitting nothing rather than on whichever
    // guard caught it: RadioCard refuses a disabled card, and selectMode refuses a repair the
    // acknowledgement did not arm.
    await clickCard(wrapper, REPAIR)
    await wrapper.findAll('input[type="radio"]')[REPAIR].trigger('change')

    expect(payloads(wrapper)).toHaveLength(0)
  })

  it('refuses a repair that reached the section without passing the card', async () => {
    // Straight at the section's own handler, with RadioCard's two guards stepped over entirely.
    // Without this the suite cannot tell that the redundancy has decayed to a single guard:
    // removing either layer on its own leaves the click test green, because the other still
    // holds.
    const wrapper = await mountSection()
    const cards = wrapper.findAllComponents(RadioCard)

    cards[REPAIR].vm.$emit('update:modelValue', 'repair')
    await wrapper.vm.$nextTick()

    expect(payloads(wrapper)).toHaveLength(0)

    // The control, through the same back door: preview is not refused, so the section is
    // refusing repair specifically rather than ignoring the event.
    cards[PREVIEW].vm.$emit('update:modelValue', 'preview')
    await wrapper.vm.$nextTick()
    expect(payloads(wrapper)).toHaveLength(1)
    expect(lastPayload(wrapper).authorIdentityRepairDryRun).toBe(true)
  })

  it('corrects an out-of-range stored number once, so the row catches up with the display', async () => {
    // The display clamp alone would leave the row saying 9999 for ever: the section emits a
    // numeric field only when the operator types in it, and the save posts the settings object
    // verbatim, so nothing would ever heal the row it is displaying a different number for.
    const wrapper = await mountSection({
      authorIdentityRepairIntervalHours: 9999,
      authorIdentityRepairMaxRowsPerRun: 0,
    })
    await flushTicks()

    const emitted = payloads(wrapper)
    expect(emitted).toHaveLength(1)
    expect(emitted[0][0].authorIdentityRepairIntervalHours).toBe(168)
    expect(emitted[0][0].authorIdentityRepairMaxRowsPerRun).toBe(1)
    // The one that was already in range is not rewritten.
    expect(emitted[0][0].authorIdentityRepairRecheckAfterDays).toBe(14)
  })

  it('emits nothing on mount when every stored number is in range, which is the control', async () => {
    const wrapper = await mountSection()
    await flushTicks()

    expect(payloads(wrapper)).toHaveLength(0)
  })

  it('shows the repair card as unavailable and says how to make it available', async () => {
    const wrapper = await mountSection()

    expect(wrapper.text()).toContain('Tick the box below to make it selectable')

    // Gone once the option is armed, so the hint never contradicts the card beside it.
    await wrapper.find('input[type="checkbox"]').setValue(true)
    expect(wrapper.text()).not.toContain('Tick the box below to make it selectable')
  })

  it('leaves off and preview reachable in one click, which is the control', async () => {
    // One click on the card is enough for either of these. The count is not asserted because a
    // click on a label reaches the handler twice, once through the label and once through the
    // input the label activates; what matters is that the first click already wrote the mode.
    const fromPreview = await mountSection()
    await clickCard(fromPreview, OFF)
    expect(payloads(fromPreview).length).toBeGreaterThan(0)
    expect(lastPayload(fromPreview).authorIdentityRepairEnabled).toBe(false)

    const fromOff = await mountSection({ authorIdentityRepairEnabled: false })
    await clickCard(fromOff, PREVIEW)
    expect(payloads(fromOff).length).toBeGreaterThan(0)
    expect(lastPayload(fromOff).authorIdentityRepairEnabled).toBe(true)
  })

  it('changes no setting when the unlock box is ticked on its own', async () => {
    const wrapper = await mountSection()

    await wrapper.find('input[type="checkbox"]').setValue(true)

    expect(payloads(wrapper)).toHaveLength(0)
  })

  it('reaches the repair state on the second deliberate click', async () => {
    const wrapper = await mountSection()

    await wrapper.find('input[type="checkbox"]').setValue(true)
    expect(
      (wrapper.findAll('input[type="radio"]')[REPAIR].element as HTMLInputElement).disabled,
    ).toBe(false)

    await clickCard(wrapper, REPAIR)

    const payload = lastPayload(wrapper)
    expect(payload.authorIdentityRepairEnabled).toBe(true)
    expect(payload.authorIdentityRepairDryRun).toBe(false)
  })

  it('does not offer the unlock box once repair is the stored state', async () => {
    const wrapper = await mountSection({ authorIdentityRepairDryRun: false })

    expect(wrapper.findAll('input[type="checkbox"]')).toHaveLength(0)
  })

  it.each([
    [0, 'authorIdentityRepairIntervalHours', '12', 12],
    [1, 'authorIdentityRepairMaxRowsPerRun', '100', 100],
    [2, 'authorIdentityRepairRecheckAfterDays', '45', 45],
  ] as const)('writes numeric input %i to %s', async (index, key, typed, expected) => {
    const wrapper = await mountSection()

    await wrapper.findAll('input[type="number"]')[index].setValue(typed)

    const payload = lastPayload(wrapper)
    expect(payload[key]).toBe(expected)

    // Every other key in the section keeps the value it was mounted with, so a control wired to
    // a neighbour's key fails here instead of quietly passing.
    const untouched = (
      [
        'authorIdentityRepairIntervalHours',
        'authorIdentityRepairMaxRowsPerRun',
        'authorIdentityRepairRecheckAfterDays',
      ] as const
    ).filter((k) => k !== key)
    for (const other of untouched) {
      expect(payload[other]).toBe(previewing[other])
    }
    expect(payload.authorIdentityRepairEnabled).toBe(true)
    expect(payload.authorIdentityRepairDryRun).toBe(true)
  })

  it.each([
    [0, 'authorIdentityRepairIntervalHours', '9999', 168],
    [0, 'authorIdentityRepairIntervalHours', '0', 1],
    [1, 'authorIdentityRepairMaxRowsPerRun', '5000', 500],
    [1, 'authorIdentityRepairMaxRowsPerRun', '0', 1],
    [2, 'authorIdentityRepairRecheckAfterDays', '99999', 3650],
    [2, 'authorIdentityRepairRecheckAfterDays', '-5', 0],
  ] as const)('clamps input %i (%s) typed as %s to %i', async (index, key, typed, expected) => {
    const wrapper = await mountSection()

    await wrapper.findAll('input[type="number"]')[index].setValue(typed)

    // min and max on a number input are advisory: typing or pasting past them is allowed, and
    // the options loader clamps at use rather than refusing at save.
    expect(lastPayload(wrapper)[key]).toBe(expected)
  })

  it('shows the number the pass will use when the stored one is out of range', async () => {
    // GET /settings echoes back whatever is in the row, and the loader clamps when it reads it.
    // Displaying the row verbatim would show a daily pass as hourly and a ceiling of 5000 rows
    // that can never be reached.
    const wrapper = await mountSection({
      authorIdentityRepairIntervalHours: 9999,
      authorIdentityRepairMaxRowsPerRun: 0,
      authorIdentityRepairRecheckAfterDays: -5,
    })

    const inputs = wrapper.findAll('input[type="number"]')
    expect(inputs.map((i) => (i.element as HTMLInputElement).value)).toEqual(['168', '1', '0'])
  })

  it('shows an in-range stored number exactly as stored, which is the control', async () => {
    const wrapper = await mountSection()

    const inputs = wrapper.findAll('input[type="number"]')
    expect(inputs.map((i) => (i.element as HTMLInputElement).value)).toEqual(['6', '40', '14'])
  })

  it('falls back to the shipped defaults when the box is emptied', async () => {
    const wrapper = await mountSection()

    await wrapper.findAll('input[type="number"]')[2].setValue('')

    // Zero is a legal recheck age and means "ask about every author on every run", so an
    // emptied box must not quietly become that.
    expect(lastPayload(wrapper).authorIdentityRepairRecheckAfterDays).toBe(30)
  })

  it('falls back to the shipped defaults when the settings payload is empty', async () => {
    const { default: AuthorIdentityRepairSection } =
      await import('@/components/settings/AuthorIdentityRepairSection.vue')
    const wrapper = mount(AuthorIdentityRepairSection, {
      props: { settings: {} },
      global: { components: { Checkbox } },
    })

    const inputs = wrapper.findAll('input[type="number"]')
    expect(inputs.map((i) => (i.element as HTMLInputElement).value)).toEqual(['24', '25', '30'])

    // An absent payload is an off pass, not a repairing one.
    const radios = wrapper.findAll('input[type="radio"]')
    expect((radios[OFF].element as HTMLInputElement).checked).toBe(true)
  })

  it('leaves the numeric inputs live while the pass is on', async () => {
    const wrapper = await mountSection()

    const inputs = wrapper.findAll('input[type="number"]')
    expect(inputs.map((i) => (i.element as HTMLInputElement).disabled)).toEqual([
      false,
      false,
      false,
    ])
  })

  it('disables the numeric inputs when the pass is off', async () => {
    const wrapper = await mountSection({ authorIdentityRepairEnabled: false })

    // The neighbouring sections grey out what a disabled toggle governs. Leaving these live
    // invites an operator to tune an interval that nothing is going to read.
    const inputs = wrapper.findAll('input[type="number"]')
    expect(inputs.map((i) => (i.element as HTMLInputElement).disabled)).toEqual([true, true, true])
  })
})
