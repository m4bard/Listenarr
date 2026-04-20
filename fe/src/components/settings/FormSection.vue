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
  <div class="form-section">
    <h3>
      <component :is="icon" v-if="icon" />
      {{ title }}
    </h3>
    <div class="section-card">
      <slot />
    </div>
  </div>
</template>

<script setup lang="ts">
defineProps<{
  title: string
  icon?: unknown // PhIcon component
}>()
</script>

<style scoped>
.form-section {
  margin-bottom: 2rem;
}

/* Card-like container for section content (follows the h3) */
.section-card {
  background-color: #232323;
  border: 1px solid #333;
  border-radius: 8px;
  padding: 1rem;
  margin-top: 0.75rem;
  box-shadow: 0 6px 18px rgba(0,0,0,0.25);
}

/* Reduce spacing for first/last children inside the card to keep alignment */
.section-card > *:first-child { margin-top: 0; }
.section-card > *:last-child { margin-bottom: 0; }
.form-section:last-of-type {
  margin-bottom: 0;
}

.form-section h3 {
  margin: 0 0 0.5rem 0;
  color: #fff;
  font-size: 1.2rem;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}
</style>