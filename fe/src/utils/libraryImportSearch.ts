export interface LibraryImportSearchCandidate {
  fullPath: string
  folderName: string
  detectedTitle?: string
  detectedAuthor?: string
  detectedAsin?: string
}

export function buildLibraryImportFallbackTitle(item: Pick<LibraryImportSearchCandidate, 'fullPath' | 'folderName'>): string {
  const filenameStem = item.fullPath.replace(/\\/g, '/').split('/').pop()?.replace(/\.[^.]+$/, '') ?? ''
  const numericMatch = /\((\d+)\)\s*$/.exec(filenameStem)
  const stemBase = filenameStem.replace(/\s*\(\d+\)\s*$/, '').trim()
  const base = stemBase && stemBase.toLowerCase() !== item.folderName.toLowerCase()
    ? filenameStem
    : item.folderName

  if (numericMatch) {
    return `${base.replace(/\s*\(\d+\)\s*$/, '').trim()} ${numericMatch[1]}`
  }

  return base
}

export function buildLibraryImportInitialQuery(item: LibraryImportSearchCandidate): string {
  const asin = item.detectedAsin?.trim()
  if (asin) return asin

  const detectedTitle = item.detectedTitle?.trim()
  if (detectedTitle) return detectedTitle

  return buildLibraryImportFallbackTitle(item)
}

export function buildLibraryImportInitialAuthor(item: LibraryImportSearchCandidate): string {
  if (item.detectedAsin?.trim()) return ''
  return item.detectedAuthor?.trim() ?? ''
}

export function buildLibraryImportSearchParams(
  item: LibraryImportSearchCandidate,
  cap: number,
): {
  asin?: string
  title?: string
  author?: string
  cap: number
} {
  const asin = item.detectedAsin?.trim()
  if (asin) {
    return { asin, cap }
  }

  const title = buildLibraryImportInitialQuery(item)
  const author = buildLibraryImportInitialAuthor(item) || undefined

  return {
    title,
    ...(author ? { author } : {}),
    cap,
  }
}
