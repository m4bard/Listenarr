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
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import type { ScheduledTask, ScheduledTaskRun } from '@/types'

// The suite-wide mock in test-setup.ts does not carry these two exports, and this file's
// own mock replaces it for this module. Both tasks endpoints are stubbed here so that a
// request the component should never make shows up as a call on a spy.
const api = vi.hoisted(() => ({
  getScheduledTasks: vi.fn(),
  runScheduledTask: vi.fn(),
}))

vi.mock('@/services/api', () => api)

// Every field the row carries, so that an override in a test is the only thing that
// differs between two rows and an assertion cannot pass on an accidental default.
function makeTask(overrides: Partial<ScheduledTask> = {}): ScheduledTask {
  return {
    name: 'AutomaticSearchService',
    displayName: 'Automatic Search Service',
    intervalSeconds: 3600,
    registeredAt: '2026-09-22T09:00:00Z',
    isRegistered: true,
    isRunning: false,
    isManualRunAllowed: true,
    lastStartedAt: '2026-09-22T10:00:00Z',
    lastEndedAt: '2026-09-22T10:00:12Z',
    lastDurationSeconds: 12,
    lastOutcome: 'Succeeded',
    lastTrigger: 'Scheduled',
    nextExecution: '2026-09-22T11:00:00Z',
    ...overrides,
  }
}

function makeRun(triggered: ScheduledTaskRun['triggered'], task: ScheduledTask): ScheduledTaskRun {
  return { triggered, task }
}

// A non-2xx arrives from ApiService.request as an Error carrying the status and the raw
// body, so a test that wants to exercise the error sentence has to hand back that shape
// rather than a bare Error.
function makeApiError(status: number, body: unknown): Error {
  const serialized = JSON.stringify(body)
  return Object.assign(new Error(`API error: ${status} ${serialized}`), {
    status,
    body: serialized,
  })
}

const collapse = (value: string) => value.replace(/\s+/g, ' ').trim()

async function mountSection(tasks: ScheduledTask[]) {
  api.getScheduledTasks.mockResolvedValue(tasks)
  const { default: ScheduledTasksSection } =
    await import('@/components/system/ScheduledTasksSection.vue')
  const wrapper = mount(ScheduledTasksSection)
  await flushPromises()
  return wrapper
}

describe('ScheduledTasksSection', () => {
  beforeEach(() => {
    api.getScheduledTasks.mockReset()
    api.runScheduledTask.mockReset()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('enables Run only for a task the API would accept, and disables the refused one beside it', async () => {
    const wrapper = await mountSection([
      makeTask({
        name: 'AutomaticSearchService',
        displayName: 'Automatic Search Service',
        isManualRunAllowed: true,
      }),
      makeTask({
        name: 'ImageCacheCleanupService',
        displayName: 'Image Cache Cleanup Service',
        isManualRunAllowed: false,
      }),
    ])

    const allowed = wrapper.get('[data-testid="task-run-AutomaticSearchService"]')
    const refused = wrapper.get('[data-testid="task-run-ImageCacheCleanupService"]')

    // Both rows come from one mount, so the difference is the flag and not a component
    // that renders every button the same way.
    expect(allowed.attributes('disabled')).toBeUndefined()
    expect(refused.attributes('disabled')).toBeDefined()

    expect(allowed.attributes('aria-label')).toContain('Run Automatic Search Service now')
    expect(refused.attributes('aria-label')).toContain('runs on its schedule only')

    wrapper.unmount()
  })

  it('posts a run to the pressed task and to no other, and sends nothing for a refused row', async () => {
    const allowedTask = makeTask({
      name: 'AutomaticSearchService',
      displayName: 'Automatic Search Service',
      isManualRunAllowed: true,
    })
    const refusedTask = makeTask({
      name: 'ImageCacheCleanupService',
      displayName: 'Image Cache Cleanup Service',
      isManualRunAllowed: false,
    })

    const wrapper = await mountSection([allowedTask, refusedTask])
    api.runScheduledTask.mockResolvedValue(makeRun('started', allowedTask))

    await wrapper.get('[data-testid="task-run-AutomaticSearchService"]').trigger('click')
    await flushPromises()

    expect(api.runScheduledTask).toHaveBeenCalledTimes(1)
    expect(api.runScheduledTask).toHaveBeenCalledWith('AutomaticSearchService')

    // Control: the same gesture on the refused row reaches the network not at all.
    api.runScheduledTask.mockClear()
    await wrapper.get('[data-testid="task-run-ImageCacheCleanupService"]').trigger('click')
    await flushPromises()

    expect(api.runScheduledTask).not.toHaveBeenCalled()

    wrapper.unmount()
  })

  it('shows a refused interval as a dash with its reason, and a declared zero as a zero', async () => {
    const refusal =
      "'move.scan.handoff.recovery' declares an interval of 0.5s, and 'intervalSeconds' " +
      'carries whole, non-negative seconds.'

    const wrapper = await mountSection([
      makeTask({
        name: 'move.scan.handoff.recovery',
        displayName: 'Move Scan Handoff Recovery',
        intervalSeconds: null,
        intervalError: refusal,
      }),
      // Control: ScheduledTaskInterval.CanBeStated accepts zero, so a worker that really
      // declares a zero interval reaches the wire as 0 with no intervalError. The two
      // cases must not render the same, which is why null is tested for explicitly.
      makeTask({
        name: 'DirectDownloadService',
        displayName: 'Direct Download Service',
        intervalSeconds: 0,
      }),
    ])

    const refusedInterval = collapse(
      wrapper.get('[data-testid="task-interval-move.scan.handoff.recovery"]').text(),
    )
    expect(refusedInterval).toBe('-')
    expect(refusedInterval).not.toContain('0')

    expect(
      collapse(wrapper.get('[data-testid="task-row-move.scan.handoff.recovery"]').text()),
    ).toContain(refusal)

    const declaredZero = collapse(
      wrapper.get('[data-testid="task-interval-DirectDownloadService"]').text(),
    )
    expect(declaredZero).toBe('0 seconds')

    wrapper.unmount()
  })

  it('says something different for a run it started and a run that was already going', async () => {
    const task = makeTask({
      name: 'AutomaticSearchService',
      displayName: 'Automatic Search Service',
    })

    const startedWrapper = await mountSection([task])
    api.runScheduledTask.mockResolvedValue(makeRun('started', { ...task, isRunning: true }))
    await startedWrapper.get('[data-testid="task-run-AutomaticSearchService"]').trigger('click')
    await flushPromises()
    const startedNotice = collapse(startedWrapper.get('[data-testid="tasks-run-notice"]').text())
    startedWrapper.unmount()

    // Control: the identical row, differing only in the trigger result, which is the one
    // thing that tells the two 202 answers apart.
    const joinedWrapper = await mountSection([task])
    api.runScheduledTask.mockResolvedValue(makeRun('already-running', { ...task, isRunning: true }))
    await joinedWrapper.get('[data-testid="task-run-AutomaticSearchService"]').trigger('click')
    await flushPromises()
    const joinedNotice = collapse(joinedWrapper.get('[data-testid="tasks-run-notice"]').text())
    joinedWrapper.unmount()

    expect(startedNotice).not.toBe(joinedNotice)
    expect(startedNotice).toContain('started')
    expect(joinedNotice).toContain('already running')
  })

  it('surfaces the sentence a refusal carries, and shows no error when the run is accepted', async () => {
    const task = makeTask({
      name: 'AutomaticSearchService',
      displayName: 'Automatic Search Service',
    })
    const sentence =
      "'AutomaticSearchService' runs on its schedule only and cannot be started on demand."

    const refusedWrapper = await mountSection([task])
    api.runScheduledTask.mockRejectedValue(makeApiError(403, { error: sentence }))
    await refusedWrapper.get('[data-testid="task-run-AutomaticSearchService"]').trigger('click')
    await flushPromises()

    expect(collapse(refusedWrapper.get('[data-testid="tasks-run-error"]').text())).toContain(
      sentence,
    )
    refusedWrapper.unmount()

    // Control: the same button, the same row, an accepted run, and no error anywhere.
    const acceptedWrapper = await mountSection([task])
    api.runScheduledTask.mockResolvedValue(makeRun('started', { ...task, isRunning: true }))
    await acceptedWrapper.get('[data-testid="task-run-AutomaticSearchService"]').trigger('click')
    await flushPromises()

    expect(acceptedWrapper.find('[data-testid="tasks-run-error"]').exists()).toBe(false)
    acceptedWrapper.unmount()
  })

  it('marks a stopped worker and refuses its Run button even though the allowlist permits it', async () => {
    const wrapper = await mountSection([
      makeTask({
        name: 'MetadataRescanService',
        displayName: 'Metadata Rescan Service',
        isRegistered: false,
        isManualRunAllowed: true,
        lastOutcome: 'Failed',
        nextExecution: undefined,
      }),
      // Control: the same allowlist flag on a worker that is still running its loop.
      makeTask({
        name: 'AuthorMonitoringBackgroundService',
        displayName: 'Author Monitoring Background Service',
        isRegistered: true,
        isManualRunAllowed: true,
      }),
    ])

    const stoppedRow = wrapper.get('[data-testid="task-row-MetadataRescanService"]')
    expect(stoppedRow.classes()).toContain('worker-stopped')
    expect(collapse(wrapper.get('[data-testid="task-next-MetadataRescanService"]').text())).toBe(
      'Worker stopped',
    )

    const stoppedButton = wrapper.get('[data-testid="task-run-MetadataRescanService"]')
    expect(stoppedButton.attributes('disabled')).toBeDefined()
    expect(stoppedButton.attributes('aria-label')).toContain('its worker has stopped')

    const liveButton = wrapper.get('[data-testid="task-run-AuthorMonitoringBackgroundService"]')
    expect(liveButton.attributes('disabled')).toBeUndefined()

    wrapper.unmount()
  })
})
