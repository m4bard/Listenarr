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

/**
 * Reads the URL sub-path the backend injected into index.html.
 *
 * The build emits document-relative asset references and the backend resolves them, so there is
 * no `<base href>` for the app to fall back on and nothing usable in `import.meta.env.BASE_URL`.
 * Every root-absolute URL the app builds has to be prefixed with this value instead.
 *
 * @returns a path with a leading and no trailing slash, or the empty string at the site root.
 */
export function getUrlBase(): string {
  if (typeof window === 'undefined') return ''

  const injected = window.__listenarrUrlBase
  if (typeof injected !== 'string') return ''

  const trimmed = injected.trim().replace(/\/+$/, '')
  if (!trimmed) return ''

  return trimmed.startsWith('/') ? trimmed : `/${trimmed}`
}

/**
 * Prefixes a root-absolute application path with the URL base.
 */
export function withUrlBase(path: string): string {
  const suffix = path.startsWith('/') ? path : `/${path}`
  return `${getUrlBase()}${suffix}`
}
