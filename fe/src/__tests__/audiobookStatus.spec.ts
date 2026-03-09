import { describe, expect, it } from 'vitest'
import { computeAudiobookStatus } from '@/utils/audiobookStatus'
import type { Audiobook, QualityProfile } from '@/types'

describe('computeAudiobookStatus', () => {
  it('uses the server-provided slim status when files are not present', () => {
    const audiobook = {
      id: 1,
      title: 'Slim Book',
      status: 'quality-match',
      wanted: false,
    } as Audiobook

    expect(computeAudiobookStatus(audiobook, new Set(), [])).toBe('quality-match')
  })

  it('lets active downloads override the cached list status', () => {
    const audiobook = {
      id: 2,
      title: 'Downloading Book',
      status: 'quality-match',
      wanted: false,
    } as Audiobook

    expect(computeAudiobookStatus(audiobook, new Set([2]), [])).toBe('downloading')
  })

  it('recomputes from files when a richer audiobook payload is available', () => {
    const audiobook = {
      id: 3,
      title: 'Detailed Book',
      qualityProfileId: 10,
      files: [{ id: 100, format: 'm4b', bitrate: 320000 }],
    } as Audiobook

    const profiles: QualityProfile[] = [
      {
        id: 10,
        name: 'High Quality',
        cutoffQuality: '320kbps',
        preferredFormats: ['m4b'],
        qualities: [{ quality: '320kbps', allowed: true, priority: 0 }],
      },
    ]

    expect(computeAudiobookStatus(audiobook, new Set(), profiles)).toBe('quality-match')
  })

  it('handles bitrate values stored in bits per second', () => {
    const audiobook = {
      id: 4,
      title: 'Bitrate Book',
      qualityProfileId: 10,
      files: [{ id: 101, format: 'm4b', bitrate: 256000 }],
    } as Audiobook

    const profiles: QualityProfile[] = [
      {
        id: 10,
        name: 'High Quality',
        cutoffQuality: '256kbps',
        preferredFormats: ['m4b'],
        qualities: [{ quality: '256kbps', allowed: true, priority: 0 }],
      },
    ]

    expect(computeAudiobookStatus(audiobook, new Set(), profiles)).toBe('quality-match')
  })
})
