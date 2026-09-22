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

const POLL_INTERVAL_MS = 12000
const RUN_MESSAGE_TIMEOUT_MS = 15000

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

// A row from a server that does not keep the contract's promise to always send the key.
// The type says it is always there, so removing it has to go round the type.
function withoutIntervalKey(task: ScheduledTask): ScheduledTask {
  const copy: Record<string, unknown> = { ...task }
  delete copy.intervalSeconds
  return copy as unknown as ScheduledTask
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

// Loads the component asks for that the test decides the fate of, one at a time. Needed
// wherever the question is what happens while a read is still open, which is where both
// of the in-flight defects lived.
const pendingLoads: Array<{
  resolve: (tasks: ScheduledTask[]) => void
  reject: (error: unknown) => void
}> = []

function deferLoads() {
  api.getScheduledTasks.mockImplementation(
    () =>
      new Promise<ScheduledTask[]>((resolve, reject) => {
        pendingLoads.push({ resolve, reject })
      }),
  )
}

async function settleOldestLoad(tasks: ScheduledTask[]) {
  const pending = pendingLoads.shift()
  if (pending === undefined) {
    throw new Error('No load was in flight to settle')
  }
  pending.resolve(tasks)
  await flushPromises()
}

async function failOldestLoad(error: unknown) {
  const pending = pendingLoads.shift()
  if (pending === undefined) {
    throw new Error('No load was in flight to fail')
  }
  pending.reject(error)
  await flushPromises()
}

async function importSection() {
  const { default: ScheduledTasksSection } =
    await import('@/components/system/ScheduledTasksSection.vue')
  return ScheduledTasksSection
}

async function mountSection(tasks: ScheduledTask[]) {
  api.getScheduledTasks.mockResolvedValue(tasks)
  const wrapper = mount(await importSection())
  await flushPromises()
  return wrapper
}

async function mountDeferred() {
  deferLoads()
  const wrapper = mount(await importSection())
  await flushPromises()
  return wrapper
}

function isRefused(button: { attributes: (name: string) => string | undefined }) {
  return button.attributes('aria-disabled') === 'true'
}

describe('ScheduledTasksSection', () => {
  beforeEach(() => {
    // Installed before any mount, because fake timers do not capture a setInterval that
    // was registered while the real ones were in place. A rig broken that way is silent:
    // no poll fires, the call count stays where it was, and that reads exactly like a
    // guard doing its job. Every timer test below therefore carries a control tick that
    // has to move the count. setImmediate is left alone so flushPromises still resolves.
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'setTimeout', 'clearTimeout'] })
    pendingLoads.length = 0
    api.getScheduledTasks.mockReset()
    api.runScheduledTask.mockReset()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('enables Run only for a task the API would accept, and refuses the one beside it', async () => {
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
    expect(isRefused(allowed)).toBe(false)
    expect(isRefused(refused)).toBe(true)

    // aria-disabled and not disabled, on purpose. A disabled button is not focusable, so
    // the sentence explaining the refusal could not be reached by the people who most
    // need it. Asserted here because if this ever goes back to `disabled`, the control in
    // the next test quietly stops proving anything.
    expect(refused.attributes('disabled')).toBeUndefined()

    expect(allowed.attributes('aria-label')).toContain('Run Automatic Search Service now')
    expect(refused.attributes('aria-label')).toContain('runs on its schedule only')
    // The visible label has to be contained in the accessible name.
    expect(refused.attributes('aria-label')).toContain('Run Image Cache Cleanup Service')

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

    // Control: the same gesture on the refused row reaches the network not at all. The
    // click really is dispatched, since the element carries no disabled attribute for the
    // test runner to refuse on, so what refuses it is the component's own guard.
    api.runScheduledTask.mockClear()
    await wrapper.get('[data-testid="task-run-ImageCacheCleanupService"]').trigger('click')
    await flushPromises()

    expect(api.runScheduledTask).not.toHaveBeenCalled()

    wrapper.unmount()
  })

  it('sends one run when the button is pressed twice while the first is still in flight', async () => {
    const task = makeTask({ name: 'AutomaticSearchService' })
    const wrapper = await mountSection([task])

    let releaseRun: (value: ScheduledTaskRun) => void = () => {}
    api.runScheduledTask.mockImplementation(
      () =>
        new Promise<ScheduledTaskRun>((resolve) => {
          releaseRun = resolve
        }),
    )

    const button = wrapper.get('[data-testid="task-run-AutomaticSearchService"]')
    await button.trigger('click')
    await button.trigger('click')
    await flushPromises()

    expect(api.runScheduledTask).toHaveBeenCalledTimes(1)
    expect(isRefused(wrapper.get('[data-testid="task-run-AutomaticSearchService"]'))).toBe(true)

    // Control: once the first run has answered, the button takes a press again.
    releaseRun(makeRun('started', task))
    await flushPromises()
    await wrapper.get('[data-testid="task-run-AutomaticSearchService"]').trigger('click')
    await flushPromises()

    expect(api.runScheduledTask).toHaveBeenCalledTimes(2)

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
      // And a server that drops the key altogether, against the contract, still gets a
      // dash rather than the words "undefined seconds".
      withoutIntervalKey(
        makeTask({
          name: 'LegacyService',
          displayName: 'Legacy Service',
          intervalError: undefined,
        }),
      ),
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

    const absentKey = collapse(wrapper.get('[data-testid="task-interval-LegacyService"]').text())
    expect(absentKey).toBe('-')
    expect(absentKey).not.toContain('undefined')

    wrapper.unmount()
  })

  it('says something different for a run it started, one already going, and a bodyless accept', async () => {
    const task = makeTask({
      name: 'AutomaticSearchService',
      displayName: 'Automatic Search Service',
    })

    async function noticeFor(run: ScheduledTaskRun) {
      const wrapper = await mountSection([task])
      api.runScheduledTask.mockResolvedValue(run)
      await wrapper.get('[data-testid="task-run-AutomaticSearchService"]').trigger('click')
      await flushPromises()
      const notice = collapse(wrapper.get('[data-testid="tasks-run-notice"]').text())
      wrapper.unmount()
      return notice
    }

    const startedNotice = await noticeFor(makeRun('started', { ...task, isRunning: true }))
    // Control: the identical row, differing only in the trigger result, which is the one
    // thing that tells the two 202 answers apart.
    const joinedNotice = await noticeFor(makeRun('already-running', { ...task, isRunning: true }))
    // And the controller branch that answers 202 with no body at all, where neither claim
    // can be made and the page must not invent one.
    const bodylessNotice = await noticeFor({ task } as unknown as ScheduledTaskRun)

    expect(startedNotice).not.toBe(joinedNotice)
    expect(startedNotice).toContain('started')
    expect(joinedNotice).toContain('already running')

    expect(bodylessNotice).not.toBe(startedNotice)
    expect(bodylessNotice).not.toBe(joinedNotice)
    expect(bodylessNotice).toContain('was accepted')
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
    expect(isRefused(stoppedButton)).toBe(true)
    expect(stoppedButton.attributes('aria-label')).toContain('its worker has stopped')

    const liveButton = wrapper.get('[data-testid="task-run-AuthorMonitoringBackgroundService"]')
    expect(isRefused(liveButton)).toBe(false)

    wrapper.unmount()
  })

  it('says the worker has stopped, not that it is scheduled only, when both are true', async () => {
    const wrapper = await mountSection([
      makeTask({
        name: 'ImageCacheCleanupService',
        displayName: 'Image Cache Cleanup Service',
        isRegistered: false,
        isManualRunAllowed: false,
      }),
      // Control: the same allowlist refusal on a worker whose loop is still alive, which
      // is the sentence the row above must not borrow. Without this pair the two reasons
      // could be given in either order and no test would notice.
      makeTask({
        name: 'DirectDownloadService',
        displayName: 'Direct Download Service',
        isRegistered: true,
        isManualRunAllowed: false,
      }),
    ])

    const stopped = wrapper.get('[data-testid="task-run-ImageCacheCleanupService"]')
    expect(stopped.attributes('aria-label')).toContain('its worker has stopped')
    expect(stopped.attributes('aria-label')).not.toContain('runs on its schedule only')

    const scheduledOnly = wrapper.get('[data-testid="task-run-DirectDownloadService"]')
    expect(scheduledOnly.attributes('aria-label')).toContain('runs on its schedule only')
    expect(scheduledOnly.attributes('aria-label')).not.toContain('its worker has stopped')

    wrapper.unmount()
  })

  it('refuses a run on a task already running, whatever the allowlist says', async () => {
    const wrapper = await mountSection([
      makeTask({
        name: 'AutomaticSearchService',
        displayName: 'Automatic Search Service',
        isManualRunAllowed: true,
        isRunning: true,
      }),
      // Control: the same allowlist flag on the same worker between cycles.
      makeTask({
        name: 'MetadataRescanService',
        displayName: 'Metadata Rescan Service',
        isManualRunAllowed: true,
        isRunning: false,
      }),
    ])

    const running = wrapper.get('[data-testid="task-run-AutomaticSearchService"]')
    expect(isRefused(running)).toBe(true)
    expect(running.attributes('aria-label')).toContain('already running')
    expect(collapse(wrapper.get('[data-testid="task-next-AutomaticSearchService"]').text())).toBe(
      'Running now',
    )

    expect(isRefused(wrapper.get('[data-testid="task-run-MetadataRescanService"]'))).toBe(false)

    wrapper.unmount()
  })

  it('keeps polling while mounted and stops once unmounted', async () => {
    const wrapper = await mountSection([makeTask()])
    expect(api.getScheduledTasks).toHaveBeenCalledTimes(1)

    // Control for the rig itself: if the fake timers had not captured the interval, this
    // would still read 1 and every assertion below would pass for the wrong reason.
    await vi.advanceTimersByTimeAsync(POLL_INTERVAL_MS)
    await flushPromises()
    expect(api.getScheduledTasks).toHaveBeenCalledTimes(2)

    await vi.advanceTimersByTimeAsync(POLL_INTERVAL_MS)
    await flushPromises()
    expect(api.getScheduledTasks).toHaveBeenCalledTimes(3)

    wrapper.unmount()
    await vi.advanceTimersByTimeAsync(POLL_INTERVAL_MS * 4)
    await flushPromises()

    expect(api.getScheduledTasks).toHaveBeenCalledTimes(3)
  })

  it('keeps the rows and keeps polling when a poll fails', async () => {
    const wrapper = await mountSection([
      makeTask({ name: 'AutomaticSearchService', displayName: 'Automatic Search Service' }),
    ])
    expect(wrapper.find('[data-testid="tasks-load-error"]').exists()).toBe(false)

    api.getScheduledTasks.mockRejectedValueOnce(
      makeApiError(500, { error: 'The registry is down' }),
    )
    await vi.advanceTimersByTimeAsync(POLL_INTERVAL_MS)
    await flushPromises()

    expect(collapse(wrapper.get('[data-testid="tasks-load-error"]').text())).toContain(
      'The registry is down',
    )
    // The rows the last good read produced are still there, and so is the table.
    expect(wrapper.find('[data-testid="task-row-AutomaticSearchService"]').exists()).toBe(true)

    // Control: the loop survived the failure, and the next good poll clears the error.
    await vi.advanceTimersByTimeAsync(POLL_INTERVAL_MS)
    await flushPromises()
    expect(wrapper.find('[data-testid="tasks-load-error"]').exists()).toBe(false)

    wrapper.unmount()
  })

  it('does not claim there are no workers when the first load failed', async () => {
    const wrapper = await mountDeferred()
    await failOldestLoad(makeApiError(500, { error: 'The registry is down' }))

    expect(collapse(wrapper.get('[data-testid="tasks-load-error"]').text())).toContain(
      'The registry is down',
    )
    expect(wrapper.find('.empty-message').exists()).toBe(false)

    // Control: a load that really does come back with nothing says so.
    await vi.advanceTimersByTimeAsync(POLL_INTERVAL_MS)
    await settleOldestLoad([])
    expect(wrapper.find('.empty-message').exists()).toBe(true)

    wrapper.unmount()
  })

  it('does not stack a second read on top of an open one', async () => {
    const wrapper = await mountDeferred()
    expect(api.getScheduledTasks).toHaveBeenCalledTimes(1)

    // The first read is still open, so the poll must not open a second.
    await vi.advanceTimersByTimeAsync(POLL_INTERVAL_MS)
    await flushPromises()
    expect(api.getScheduledTasks).toHaveBeenCalledTimes(1)

    // Control: once it has answered, the very next tick does open one, which is what
    // proves the assertion above was the guard and not a dead timer.
    await settleOldestLoad([makeTask()])
    await vi.advanceTimersByTimeAsync(POLL_INTERVAL_MS)
    await flushPromises()
    expect(api.getScheduledTasks).toHaveBeenCalledTimes(2)

    wrapper.unmount()
  })

  it('re-reads after a run even when a poll is still open', async () => {
    const idle = makeTask({
      name: 'AutomaticSearchService',
      displayName: 'Automatic Search Service',
      isRunning: false,
    })

    const wrapper = await mountDeferred()
    await settleOldestLoad([idle])
    expect(api.getScheduledTasks).toHaveBeenCalledTimes(1)

    // Control: the tick really does open a read, and that read is left hanging, which is
    // the state in which the run's own refresh used to be dropped without a trace.
    await vi.advanceTimersByTimeAsync(POLL_INTERVAL_MS)
    await flushPromises()
    expect(api.getScheduledTasks).toHaveBeenCalledTimes(2)

    api.runScheduledTask.mockResolvedValue(makeRun('started', { ...idle, isRunning: true }))
    await wrapper.get('[data-testid="task-run-AutomaticSearchService"]').trigger('click')
    await flushPromises()

    expect(api.getScheduledTasks).toHaveBeenCalledTimes(3)
    await settleOldestLoad([{ ...idle, isRunning: true }])

    // The row the user is looking at now agrees with the button they can press.
    expect(collapse(wrapper.get('[data-testid="task-next-AutomaticSearchService"]').text())).toBe(
      'Running now',
    )
    expect(isRefused(wrapper.get('[data-testid="task-run-AutomaticSearchService"]'))).toBe(true)

    wrapper.unmount()
  })

  it('takes a run message off the page instead of leaving it there all session', async () => {
    const task = makeTask({ name: 'AutomaticSearchService' })
    const wrapper = await mountSection([task])
    api.runScheduledTask.mockResolvedValue(makeRun('started', { ...task, isRunning: true }))

    await wrapper.get('[data-testid="task-run-AutomaticSearchService"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="tasks-run-notice"]').exists()).toBe(true)

    // Control: still there one tick short of the timeout, gone once it passes.
    await vi.advanceTimersByTimeAsync(RUN_MESSAGE_TIMEOUT_MS - 1)
    await flushPromises()
    expect(wrapper.find('[data-testid="tasks-run-notice"]').exists()).toBe(true)

    await vi.advanceTimersByTimeAsync(1)
    await flushPromises()
    expect(wrapper.find('[data-testid="tasks-run-notice"]').exists()).toBe(false)

    wrapper.unmount()
  })
})
