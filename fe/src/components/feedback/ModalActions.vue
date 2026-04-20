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
  <div class="modal-actions">
    <div class="modal-actions-left"><slot name="left" /></div>
    <div class="modal-actions-right"><slot name="right" /></div>
  </div>
</template>

<script setup lang="ts">
// simple layout wrapper
</script>

<style scoped>
.modal-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}
.modal-actions-left { display:flex; gap:0.75rem; }
.modal-actions-right { display:flex; gap:0.75rem; }
@media (max-width:480px) {
  .modal-actions { flex-direction: column-reverse; }
}
</style>