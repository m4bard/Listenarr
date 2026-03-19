// Language to region code mapping for Audible/Audimeta API
// Maps user-friendly language names to Audible market region codes
// Supported regions: us, ca, uk, au, fr, de, jp, it, in, es, br

export const languageToRegion: Record<string, string> = {
  english: 'us',
  'english-uk': 'uk',
  'english-ca': 'ca',
  'english-au': 'au',
  'english-in': 'in',
  german: 'de',
  french: 'fr',
  spanish: 'es',
  italian: 'it',
  portuguese: 'br',
  japanese: 'jp',
}

export const regionToLanguage: Record<string, string> = {
  us: 'english',
  uk: 'english-uk',
  gb: 'english-uk',
  ca: 'english-ca',
  au: 'english-au',
  in: 'english-in',
  de: 'german',
  fr: 'french',
  es: 'spanish',
  it: 'italian',
  br: 'portuguese',
  jp: 'japanese',
}

export const searchRegionOptions = [
  { value: 'us', label: 'United States (US)' },
  { value: 'uk', label: 'United Kingdom (UK)' },
  { value: 'ca', label: 'Canada (CA)' },
  { value: 'au', label: 'Australia (AU)' },
  { value: 'in', label: 'India (IN)' },
  { value: 'de', label: 'Germany (DE)' },
  { value: 'fr', label: 'France (FR)' },
  { value: 'es', label: 'Spain (ES)' },
  { value: 'it', label: 'Italy (IT)' },
  { value: 'br', label: 'Brazil (BR)' },
  { value: 'jp', label: 'Japan (JP)' },
] as const

export const preferredSearchLanguageOptions = [
  { value: 'all', label: 'All' },
  { value: 'english', label: 'English' },
  { value: 'spanish', label: 'Spanish' },
  { value: 'german', label: 'German' },
  { value: 'hungarian', label: 'Hungarian' },
  { value: 'french', label: 'French' },
  { value: 'polish', label: 'Polish' },
  { value: 'italian', label: 'Italian' },
  { value: 'russian', label: 'Russian' },
] as const

const legacyRegionValueToRegion: Record<string, string> = {
  english: 'us',
  'english-uk': 'uk',
  'english-ca': 'ca',
  'english-au': 'au',
  'english-in': 'in',
  german: 'de',
  french: 'fr',
  spanish: 'es',
  italian: 'it',
  portuguese: 'br',
  japanese: 'jp',
  gb: 'uk',
}

const validSearchRegions = new Set<string>(searchRegionOptions.map((option) => option.value))
const validPreferredSearchLanguages = new Set<string>(
  preferredSearchLanguageOptions.map((option) => option.value),
)
const preferredSearchLanguageAliases: Record<string, string> = {
  all: 'all',
  any: 'all',
  english: 'english',
  en: 'english',
  'en-us': 'english',
  'en-uk': 'english',
  'en-gb': 'english',
  'en-ca': 'english',
  'en-au': 'english',
  'en-in': 'english',
  'english-uk': 'english',
  'english-ca': 'english',
  'english-au': 'english',
  'english-in': 'english',
  spanish: 'spanish',
  es: 'spanish',
  spa: 'spanish',
  'es-es': 'spanish',
  german: 'german',
  de: 'german',
  ger: 'german',
  deu: 'german',
  deutsch: 'german',
  'de-de': 'german',
  hungarian: 'hungarian',
  hu: 'hungarian',
  hun: 'hungarian',
  magyar: 'hungarian',
  french: 'french',
  fr: 'french',
  fre: 'french',
  fra: 'french',
  'fr-fr': 'french',
  polish: 'polish',
  pl: 'polish',
  pol: 'polish',
  'pl-pl': 'polish',
  italian: 'italian',
  it: 'italian',
  ita: 'italian',
  'it-it': 'italian',
  russian: 'russian',
  ru: 'russian',
  rus: 'russian',
  'ru-ru': 'russian',
}

function resolveSupportedPreferredSearchLanguage(
  value: string | undefined | null,
): string | undefined {
  const normalized = (value || '').trim().toLowerCase()
  if (!normalized) return undefined

  const alias = preferredSearchLanguageAliases[normalized]
  if (alias) return alias

  if (validPreferredSearchLanguages.has(normalized)) return normalized

  for (const option of preferredSearchLanguageOptions) {
    if (option.value !== 'all' && normalized.startsWith(option.value)) return option.value
  }

  return undefined
}

/**
 * Convert language name to region code
 * @param language - Language name (e.g., 'english', 'german')
 * @returns Region code (e.g., 'us', 'de') or 'us' as fallback
 */
export function getRegionFromLanguage(language: string): string {
  return languageToRegion[language.toLowerCase()] || 'us'
}

/**
 * Convert region code to language name
 * @param region - Region code (e.g., 'us', 'de')
 * @returns Language name (e.g., 'english', 'german') or 'english' as fallback
 */
export function getLanguageFromRegion(region: string): string {
  return regionToLanguage[region.toLowerCase()] || 'english'
}

export function normalizeSearchRegion(region: string | undefined | null): string {
  const normalized = (region || '').trim().toLowerCase()
  if (!normalized) return 'us'
  if (validSearchRegions.has(normalized)) return normalized
  return legacyRegionValueToRegion[normalized] || 'us'
}

export function normalizePreferredSearchLanguage(language: string | undefined | null): string {
  const normalized = (language || '').trim().toLowerCase()
  if (!normalized) return 'english'
  const directMatch = resolveSupportedPreferredSearchLanguage(normalized)
  if (directMatch) return directMatch
  if (validSearchRegions.has(normalized)) {
    return resolveSupportedPreferredSearchLanguage(getLanguageFromRegion(normalized)) || 'english'
  }
  const legacyRegion = legacyRegionValueToRegion[normalized]
  if (legacyRegion) {
    return resolveSupportedPreferredSearchLanguage(getLanguageFromRegion(legacyRegion)) || 'english'
  }
  if (normalized.startsWith('english')) return 'english'
  return 'english'
}

export function getPreferredSearchLanguageFilter(
  language: string | undefined | null,
): string | undefined {
  const normalized = normalizePreferredSearchLanguage(language)
  return normalized === 'all' ? undefined : normalized
}

export function normalizeSearchResultLanguage(
  language: string | undefined | null,
): string | undefined {
  const normalized = (language || '').trim().toLowerCase()
  if (!normalized) return undefined
  const directMatch = resolveSupportedPreferredSearchLanguage(normalized)
  if (directMatch && directMatch !== 'all') return directMatch
  if (validSearchRegions.has(normalized)) {
    const mapped = resolveSupportedPreferredSearchLanguage(getLanguageFromRegion(normalized))
    return mapped === 'all' ? undefined : mapped
  }
  const legacyRegion = legacyRegionValueToRegion[normalized]
  if (legacyRegion) {
    const mapped = resolveSupportedPreferredSearchLanguage(getLanguageFromRegion(legacyRegion))
    return mapped === 'all' ? undefined : mapped
  }
  return undefined
}
