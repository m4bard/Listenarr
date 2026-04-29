<!--
  Listenarr - Audiobook Management System
  Copyright (C) 2024-2026 Listenarr Contributors

  This program is free software: you can redistribute it and/or modify
  it under the terms of the GNU Affero General Public License as published
  by the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  This program is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
  GNU Affero General Public License for more details.

  You should have received a copy of the GNU Affero General Public License
  along with this program. If not, see <https://www.gnu.org/licenses/>.
-->
<template>
  <div class="form-group radio-group">
    <label :class="['radio-label', { active: isActive }]" @click="select">
      <input
        type="radio"
        :name="name"
        :checked="isActive"
        @change="onChange"
        :aria-checked="isActive"
      />
      <div class="radio-content">
        <span class="radio-title">{{ title }}</span>
        <small v-if="description">{{ description }}</small>
        <slot />
      </div>
    </label>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  modelValue?: unknown
  value: unknown
  title: string
  description?: string
  name?: string
}>()
const emit = defineEmits(['update:modelValue'])

const isActive = computed(() => props.modelValue === props.value)

function onChange() {
  emit('update:modelValue', props.value)
}

function select() {
  // Clicking the label should select the radio
  emit('update:modelValue', props.value)
}
</script>

<style scoped>
/* Visuals moved to global modal stylesheet to ensure consistency across modals. Keep minimal layout helpers here. */
.radio-group {
  display: flex;
  flex-direction: column;
}
.radio-content {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  flex: 1;
}
</style>
