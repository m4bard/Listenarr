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
import { ref, computed } from 'vue'
import { useToast } from '@/services/toastService'

export function useFormState<T>(initialData: T) {
  const formData = ref<T>(initialData)
  const isSaving = ref(false)
  const saveError = ref<string | null>(null)
  const toast = useToast()

  const hasChanges = computed(() => {
    return JSON.stringify(formData.value) !== JSON.stringify(initialData)
  })

  const resetForm = () => {
    formData.value = JSON.parse(JSON.stringify(initialData))
    saveError.value = null
  }

  const handleSaveError = (error: unknown) => {
    const message = error instanceof Error ? error.message : 'Failed to save changes'
    saveError.value = message
    toast.error('Error', message)
  }

  const clearError = () => {
    saveError.value = null
  }

  return {
    formData,
    isSaving,
    saveError,
    hasChanges,
    resetForm,
    handleSaveError,
    clearError,
  }
}
