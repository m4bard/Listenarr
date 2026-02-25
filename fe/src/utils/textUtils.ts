/**
 * Decodes HTML entities in a string
 * @param text - The text containing HTML entities
 * @returns The decoded text
 */
export function decodeHtmlEntities(text: string): string {
  if (!text) return text

  // Create a temporary DOM element to decode entities
  const textarea = document.createElement('textarea')
  textarea.innerHTML = text
  return textarea.value
}

/**
 * Safely renders text that might contain HTML entities
 * @param text - The text to render
 * @returns The decoded text
 */
export function safeText(text: string | undefined | null): string {
  if (!text) return ''
  return decodeHtmlEntities(text)
}

/**
 * Strips HTML tags and normalizes whitespace/newlines into safe plain text.
 */
export function stripHtmlAndNormalize(text: string | undefined | null): string {
  if (!text) return ''

  const withBreaks = text
    .replace(/<\s*br\s*\/?>/gi, '\n')
    .replace(/<\/\s*p\s*>/gi, '\n')
    .replace(/<\/\s*div\s*>/gi, '\n')
    .replace(/<\/\s*li\s*>/gi, '\n')

  const container = document.createElement('div')
  container.innerHTML = withBreaks

  const raw = (container.textContent || container.innerText || '')
    .replace(/\r\n?/g, '\n')
    .replace(/[ \t\f\v]+/g, ' ')
    .replace(/\n{3,}/g, '\n\n')
    .trim()

  return decodeHtmlEntities(raw)
}
