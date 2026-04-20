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
import type { Audiobook, AudiobookStatus, QualityProfile } from '@/types'

const AUDIOBOOK_STATUSES: AudiobookStatus[] = [
  'downloading',
  'no-file',
  'quality-mismatch',
  'quality-match',
]

const normalize = (value?: string): string => (value || '').toString().trim().toLowerCase()

function isAudiobookStatus(value: unknown): value is AudiobookStatus {
  return typeof value === 'string' && AUDIOBOOK_STATUSES.includes(value as AudiobookStatus)
}

export function formatAudiobookStatus(status: AudiobookStatus): string {
  switch (status) {
    case 'downloading':
      return 'Downloading'
    case 'no-file':
      return 'Missing'
    case 'quality-mismatch':
      return 'Below Cutoff'
    case 'quality-match':
      return 'Downloaded'
    default:
      return ''
  }
}

export function computeAudiobookStatus(
  audiobook: Audiobook,
  activeDownloadAudiobookIds: ReadonlySet<number>,
  qualityProfiles: QualityProfile[],
): AudiobookStatus {
  if (activeDownloadAudiobookIds.has(audiobook.id)) {
    return 'downloading'
  }

  const hasFiles = Array.isArray(audiobook.files) && audiobook.files.length > 0
  if (hasFiles) {
    const profile = qualityProfiles.find((item) => item.id === audiobook.qualityProfileId)

    if (!profile) {
      const hasFileSummary =
        !!(audiobook.filePath && audiobook.fileSize && audiobook.fileSize > 0) || hasFiles
      return hasFileSummary ? 'quality-match' : 'no-file'
    }

    const preferredFormats = (profile.preferredFormats || []).map((item) => normalize(item))
    const candidateFiles = audiobook.files!.filter((file) => {
      if (!file) return false
      const fileFormat = normalize(file.format) || normalize(file.container) || ''
      if (preferredFormats.length === 0) return true
      return (
        preferredFormats.includes(fileFormat) ||
        preferredFormats.some((preferredFormat) => fileFormat.includes(preferredFormat))
      )
    })

    if (candidateFiles.length === 0) {
      return 'quality-mismatch'
    }

    if (!profile.cutoffQuality || !profile.qualities || profile.qualities.length === 0) {
      return 'quality-match'
    }

    const qualityPriority = new Map<string, number>()
    for (const quality of profile.qualities) {
      if (!quality || !quality.quality) continue
      qualityPriority.set(normalize(quality.quality), quality.priority)
    }

    const cutoff = normalize(profile.cutoffQuality)
    const cutoffPriority = qualityPriority.has(cutoff)
      ? qualityPriority.get(cutoff)!
      : Number.POSITIVE_INFINITY

    for (const file of candidateFiles) {
      const derivedQuality = deriveQualityLabel(audiobook, file)
      if (!derivedQuality) continue

      const priority = qualityPriority.has(derivedQuality)
        ? qualityPriority.get(derivedQuality)!
        : Number.POSITIVE_INFINITY

      if (priority <= cutoffPriority) {
        return 'quality-match'
      }
    }

    return 'quality-mismatch'
  }

  if (isAudiobookStatus(audiobook.status)) {
    return audiobook.status
  }

  return 'no-file'
}

function deriveQualityLabel(
  audiobook: Audiobook,
  file:
    | {
        bitrate?: number
        container?: string
        codec?: string
        format?: string
      }
    | undefined,
): string {
  if (audiobook.quality) return normalize(audiobook.quality)

  if (file && file.bitrate) {
    const bitrate = Number(file.bitrate)
    if (!Number.isNaN(bitrate)) {
      const bitrateKbps = bitrate >= 1000 ? bitrate / 1000 : bitrate
      if (bitrateKbps >= 320) return '320kbps'
      if (bitrateKbps >= 256) return '256kbps'
      if (bitrateKbps >= 192) return '192kbps'
      return `${Math.round(bitrateKbps)}kbps`
    }
  }

  const container = normalize(file?.container)
  const codec = normalize(file?.codec)
  if (
    container.includes('flac') ||
    codec.includes('flac') ||
    container.includes('alac') ||
    codec.includes('alac') ||
    container.includes('aiff') ||
    codec.includes('aiff') ||
    container.includes('ape') ||
    codec.includes('ape') ||
    container.includes('dsd') ||
    codec.includes('dsd') ||
    container.includes('wv') ||
    codec.includes('wv') ||
    container.includes('wav') ||
    codec.includes('wav')
  ) {
    return 'lossless'
  }

  if (file?.format) return normalize(file.format)

  return ''
}
