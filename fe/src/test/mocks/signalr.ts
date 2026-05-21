import { vi } from 'vitest'

export function createSignalRServiceMock(overrides: Record<string, unknown> = {}) {
  const unsubscribe = () => {}

  return {
    connect: vi.fn(async () => undefined),
    onDownloadsList: vi.fn(() => unsubscribe),
    onSearchProgress: vi.fn(() => unsubscribe),
    onQueueUpdate: vi.fn(() => unsubscribe),
    onDownloadUpdate: vi.fn(() => unsubscribe),
    onFilesRemoved: vi.fn(() => unsubscribe),
    onAudiobookUpdate: vi.fn(() => unsubscribe),
    onNotification: vi.fn(() => unsubscribe),
    onToast: vi.fn(() => unsubscribe),
    onMoveJobUpdate: vi.fn(() => unsubscribe),
    ...overrides,
  }
}
