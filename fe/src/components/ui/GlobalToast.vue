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
<template>
  <div id="global-toast" aria-live="polite" class="global-toast" :style="{ top: toastTop }">
    <transition-group name="toast" tag="div">
      <div
        v-for="t in toasts"
        :key="t.id"
        :class="['toast-item', `toast-${t.level}`]"
        role="status"
        aria-atomic="true"
      >
        <div class="toast-content">
          <div class="toast-title">{{ t.title }}</div>
          <div class="toast-message">{{ t.message }}</div>
        </div>
        <button class="toast-close" @click="dismiss(t.id)" aria-label="Dismiss">×</button>
      </div>
    </transition-group>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, onMounted, onUnmounted } from 'vue'
import { useToast } from '@/services/toastService'

const toast = useToast()

// `toast.toasts` is reactive; use it directly in template
const toasts = computed(() => toast.toasts)

// Compute a dynamic top offset so the toast sits below the fixed top nav.
const toastTop = ref('72px')
function computeTopOffset() {
  try {
    const topNav = document.querySelector('.top-nav') as HTMLElement | null
    toastTop.value = topNav ? `${topNav.offsetHeight + 12}px` : '72px'
  } catch {
    toastTop.value = '72px'
  }
}

onMounted(() => {
  computeTopOffset()
  window.addEventListener('resize', computeTopOffset)
})

onUnmounted(() => {
  window.removeEventListener('resize', computeTopOffset)
})

function dismiss(id: string) {
  try {
    toast.dismiss(id)
  } catch {}
}
</script>
