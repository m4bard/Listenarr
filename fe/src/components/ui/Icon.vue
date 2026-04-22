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
<script setup lang="ts">
// Lightweight Icon wrapper: prefers an explicitly passed component (via `component` prop)
// and otherwise falls back to the legacy webfont CSS markup. This file intentionally
// does NOT import many icons — import icons per-view to allow better chunking.
import { computed } from 'vue'
import type { Component } from 'vue'

// Use a multi-word component name to satisfy the vue/multi-word-component-names rule
defineOptions({ name: 'AppIcon' })

const props = defineProps<{
  name?: string
  component?: Component
  size?: number | string
  weight?: 'thin' | 'light' | 'regular' | 'bold' | 'fill' | 'duotone'
  class?: string
}>()

const hasComponent = computed(() => !!props.component)
</script>

<template>
  <component
    v-if="hasComponent"
    :is="props.component"
    :size="props.size || 20"
    :weight="props.weight || 'regular'"
    :class="props.class"
  />
  <span v-else class="icon-placeholder" aria-hidden="true" />
</template>

<style scoped>
/* Intentionally small: styling comes from parent components or global CSS. */
</style>