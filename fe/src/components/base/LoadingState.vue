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
  <div class="loading-state">
    <div class="spinner">
      <PhSpinner :size="size" />
    </div>
    <p v-if="message" class="loading-message">{{ message }}</p>
  </div>
</template>

<script setup lang="ts">
import { PhSpinner } from '@phosphor-icons/vue'

/**
 * LoadingState - Display a loading indicator with optional message
 *
 * Shows a spinning icon and customizable message while data is being fetched
 * or processed. Provides consistent loading UX across the application.
 *
 * @example
 * <LoadingState />
 *
 * @example
 * <LoadingState message="Loading audiobooks..." />
 */
defineProps({
  /** Loading message to display (default: 'Loading...') */
  message: { type: String, default: 'Loading...' },
  /** Size of the spinner icon in pixels */
  size: { type: Number, default: 32 },
})
</script>

<style scoped>
.loading-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 2rem;
  gap: 1rem;
  min-height: 200px;
}

.spinner {
  display: flex;
  align-items: center;
  justify-content: center;
}

.spinner :deep(svg) {
  animation: spin 1s linear infinite;
  color: var(--brand-500, #2196f3);
}

.loading-message {
  margin: 0;
  color: #ccc;
  font-size: 0.95rem;
}

/* @keyframes spin is centralized in src/assets/animations.css */
</style>
