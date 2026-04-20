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
  <div class="rename-path-diff">
    <div class="rename-path-item">
      <span class="rename-path-label">Current</span>
      <span class="rename-path-value rename-path-value--current">{{ oldPath || 'No current path' }}</span>
    </div>

    <div class="rename-path-item rename-path-item--arrow" aria-hidden="true">
      <PhArrowRight class="arrow-icon" />
    </div>

    <div class="rename-path-item">
      <span class="rename-path-label">New</span>
      <span class="rename-path-value rename-path-value--new">{{ newPath || 'No new path' }}</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { PhArrowRight } from '@phosphor-icons/vue'

defineProps<{
  oldPath?: string
  newPath?: string
}>()
</script>

<style scoped>
.rename-path-diff {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);
  align-items: center;
  gap: 0.75rem;
  width: 100%;
}

.rename-path-item {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  min-width: 0;
}

.rename-path-item--arrow {
  align-items: center;
  justify-content: center;
}

.rename-path-label {
  font-size: 0.7rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--text-muted, #adb5bd);
}

.rename-path-value {
  font-size: 0.84rem;
  line-height: 1.45;
  overflow-wrap: anywhere;
  font-family: 'Courier New', monospace;
  padding: 0.65rem 0.75rem;
  border-radius: 6px;
  background: #1a1a1a;
  border: 1px solid #333;
}

.rename-path-value--current {
  color: #f0c2bf;
  border-color: rgba(239, 83, 80, 0.3);
}

.rename-path-value--new {
  color: #bfe3c0;
  border-color: rgba(102, 187, 106, 0.3);
}

.arrow-icon {
  flex-shrink: 0;
  color: var(--text-muted, #adb5bd);
  width: 16px;
  height: 16px;
}

@media (max-width: 768px) {
  .rename-path-diff {
    grid-template-columns: 1fr;
  }

  .rename-path-item--arrow {
    transform: rotate(90deg);
  }
}
</style>
