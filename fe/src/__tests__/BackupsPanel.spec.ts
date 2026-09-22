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
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import type { BackupArchive } from '@/types'

const getBackups = vi.fn()
const createBackup = vi.fn()

vi.mock('@/services/api', () => ({
  getBackups: (...args: unknown[]) => getBackups(...args),
  createBackup: (...args: unknown[]) => createBackup(...args),
}))

function archive(overrides: Partial<BackupArchive> = {}): BackupArchive {
  return {
    name: 'listenarr_backup_v1.3.4_2026.09.22_10.00.00.zip',
    trigger: 'Migration',
    sizeBytes: 2048,
    createdAtUtc: new Date().toISOString(),
    ...overrides,
  }
}

async function mountPanel() {
  const { default: BackupsPanel } = await import('@/components/system/BackupsPanel.vue')
  const wrapper = mount(BackupsPanel)
  await flushPromises()
  return wrapper
}

describe('BackupsPanel', () => {
  beforeEach(() => {
    getBackups.mockReset()
    createBackup.mockReset()
  })

  it('lists what the API returned', async () => {
    getBackups.mockResolvedValue([
      archive({ name: 'first.zip', trigger: 'Manual', sizeBytes: 5 * 1024 * 1024 }),
      archive({ name: 'second.zip' }),
    ])

    const wrapper = await mountPanel()

    expect(wrapper.findAll('.backup-entry')).toHaveLength(2)
    expect(wrapper.text()).toContain('first.zip')
    expect(wrapper.text()).toContain('5.0 MB')
    // The trigger is shown in operator words, not the enum name
    expect(wrapper.text()).toContain('Manual')
    expect(wrapper.text()).toContain('Automatic')
  })

  it('says so when there are none, rather than showing an empty list', async () => {
    // The control for the case above. An inert panel renders no entries either way, so the empty
    // state has to be distinguishable from a list that failed to render.
    getBackups.mockResolvedValue([])

    const wrapper = await mountPanel()

    expect(wrapper.findAll('.backup-entry')).toHaveLength(0)
    expect(wrapper.text()).toContain('No backups yet')
  })

  it('takes a backup and reloads the list', async () => {
    getBackups.mockResolvedValueOnce([]).mockResolvedValueOnce([archive({ name: 'fresh.zip' })])
    createBackup.mockResolvedValue(archive({ name: 'fresh.zip' }))

    const wrapper = await mountPanel()
    expect(wrapper.text()).toContain('No backups yet')

    await wrapper.find('[data-testid="create-backup"]').trigger('click')
    await flushPromises()

    expect(createBackup).toHaveBeenCalledOnce()
    expect(wrapper.text()).toContain('fresh.zip')
  })

  it('surfaces a failed backup instead of appearing to succeed', async () => {
    getBackups.mockResolvedValue([])
    createBackup.mockRejectedValue(new Error('500'))

    const wrapper = await mountPanel()
    await wrapper.find('[data-testid="create-backup"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('Backup failed')
  })

  it('shows the reason when a backup is refused rather than a generic failure', async () => {
    // A refusal at the manual limit is something the operator can act on, unlike a full disk, so
    // the wording has to reach them. The control is the case above, where a plain failure falls
    // back to the generic message.
    getBackups.mockResolvedValue([])
    const refused = Object.assign(new Error('Conflict'), {
      status: 409,
      body: JSON.stringify({
        error: 'There are already 20 manual backups, which is the most that are kept.',
      }),
    })
    createBackup.mockRejectedValue(refused)

    const wrapper = await mountPanel()
    await wrapper.find('[data-testid="create-backup"]').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('There are already 20 manual backups')
    expect(wrapper.text()).not.toContain('config directory is writable')
  })

  it('surfaces a failed listing', async () => {
    getBackups.mockRejectedValue(new Error('500'))

    const wrapper = await mountPanel()

    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
  })

  it('never renders a filesystem path, because the API does not send one', async () => {
    // An archive holds the API key and every stored credential. Showing where it lives would tell
    // anyone who can open this page where to go looking.
    getBackups.mockResolvedValue([archive()])

    const wrapper = await mountPanel()

    const row = wrapper.find('.backup-entry')
    expect(row.exists()).toBe(true)
    expect(row.text()).not.toContain('/')
    expect(row.text()).not.toContain('\\')
    expect(row.text()).toContain('listenarr_backup_v1.3.4_2026.09.22_10.00.00.zip')
  })
})
