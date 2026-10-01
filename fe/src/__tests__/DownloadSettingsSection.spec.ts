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

describe('DownloadSettingsSection', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('emits update:settings when numerical inputs change', async () => {
    const { default: DownloadSettingsSection } =
      await import('@/components/settings/DownloadSettingsSection.vue')
    const wrapper = mount(DownloadSettingsSection, {
      props: {
        settings: {
          maxConcurrentDownloads: 2,
          unmatchedScanConcurrency: 2,
          pollingIntervalSeconds: 30,
          downloadCompletionStabilitySeconds: 5,
          missingSourceRetryInitialDelaySeconds: 2,
          missingSourceMaxRetries: 3,
        },
      },
    })

    const inputs = wrapper.findAll('input[type="number"]')
    expect(inputs).toHaveLength(7)

    // Max concurrent
    await inputs[0].setValue('4')
    let last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.maxConcurrentDownloads).toBe(4)

    // Unmatched scan concurrency
    await inputs[1].setValue('3')
    last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.unmatchedScanConcurrency).toBe(3)

    // Polling interval
    await inputs[2].setValue('60')
    last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.pollingIntervalSeconds).toBe(60)

    // Stability
    await inputs[3].setValue('10')
    last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.downloadCompletionStabilitySeconds).toBe(10)

    // Missing-source delay
    await inputs[4].setValue('3')
    last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.missingSourceRetryInitialDelaySeconds).toBe(3)

    // Missing-source retries
    await inputs[5].setValue('5')
    last =
      wrapper.emitted()['update:settings']![wrapper.emitted()['update:settings']!.length - 1][0]
    expect(last.missingSourceMaxRetries).toBe(5)
  })

  async function mountStallSection(settings: Record<string, unknown>) {
    const { default: DownloadSettingsSection } =
      await import('@/components/settings/DownloadSettingsSection.vue')
    const wrapper = mount(DownloadSettingsSection, { props: { settings } })
    return { wrapper, input: wrapper.find('[data-testid="stalled-download-timeout-hours"]') }
  }

  function lastEmitted(wrapper: Awaited<ReturnType<typeof mountStallSection>>['wrapper']) {
    const emitted = wrapper.emitted()['update:settings']!
    return emitted[emitted.length - 1][0] as Record<string, unknown>
  }

  it('shows the stalled download timeout as off (0) when the setting is absent', async () => {
    const { input } = await mountStallSection({ failedDownloadHandlingEnabled: true })

    expect(input.exists()).toBe(true)
    expect((input.element as HTMLInputElement).value).toBe('0')
    expect(input.attributes('min')).toBe('0')
    expect(input.attributes('max')).toBe('720')
  })

  it('emits the stalled download timeout in whole hours, held to 0-720', async () => {
    const { wrapper, input } = await mountStallSection({
      failedDownloadHandlingEnabled: true,
      stalledDownloadTimeoutHours: 0,
    })

    await input.setValue('24')
    expect(lastEmitted(wrapper).stalledDownloadTimeoutHours).toBe(24)

    await input.setValue('5000')
    expect(lastEmitted(wrapper).stalledDownloadTimeoutHours).toBe(720)

    await input.setValue('-3')
    expect(lastEmitted(wrapper).stalledDownloadTimeoutHours).toBe(0)

    await input.setValue('')
    expect(lastEmitted(wrapper).stalledDownloadTimeoutHours).toBe(0)
  })

  it('disables the stalled download timeout while failed download handling is off', async () => {
    const off = await mountStallSection({
      failedDownloadHandlingEnabled: false,
      stalledDownloadTimeoutHours: 24,
    })
    expect(off.input.attributes('disabled')).toBeDefined()

    // Control: the same field is editable with handling on.
    const on = await mountStallSection({
      failedDownloadHandlingEnabled: true,
      stalledDownloadTimeoutHours: 24,
    })
    expect(on.input.attributes('disabled')).toBeUndefined()
    expect((on.input.element as HTMLInputElement).value).toBe('24')
  })
})
