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
import { ref } from 'vue'

type Resolver = (v: boolean) => void

// Singleton app-level confirm service
const visible = ref(false)
const title = ref('')
const message = ref('')
const confirmText = ref('Confirm')
const cancelText = ref('Cancel')
const danger = ref(false)

let resolver: Resolver | null = null

export function showConfirm(
  msg: string,
  t?: string,
  options?: { confirmText?: string; cancelText?: string; danger?: boolean },
): Promise<boolean> {
  message.value = msg
  title.value = t || 'Confirm'
  confirmText.value = options?.confirmText ?? 'Confirm'
  cancelText.value = options?.cancelText ?? 'Cancel'
  danger.value = !!options?.danger
  visible.value = true
  return new Promise<boolean>((resolve) => {
    resolver = resolve
  })
}

export function confirm() {
  if (resolver) resolver(true)
  resolver = null
  visible.value = false
}

export function cancel() {
  if (resolver) resolver(false)
  resolver = null
  visible.value = false
}

export function useConfirmService() {
  return {
    visible,
    title,
    message,
    confirmText,
    cancelText,
    danger,
    showConfirm,
    confirm,
    cancel,
  }
}

// Default export convenience
export default {
  visible,
  title,
  message,
  confirmText,
  cancelText,
  danger,
  showConfirm,
  confirm,
  cancel,
  useConfirmService,
}
