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
  <div v-if="active" class="modal-spinner-overlay" role="status" aria-live="polite">
    <div class="spinner-wrap">
      <PhSpinner class="ph-spin big" />
      <div v-if="message" class="spinner-message">{{ message }}</div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { PhSpinner } from '@phosphor-icons/vue'
defineProps({ active: { type: Boolean, default: false }, message: { type: String, required: false } })
</script>

<style scoped>
.modal-spinner-overlay {
  position: absolute;
  inset: 0;
  background: rgba(0,0,0,0.55);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 30;
}
.spinner-wrap { display:flex; flex-direction:column; align-items:center; gap:0.75rem; }
.ph-spin.big { width: 48px; height: 48px; }
.spinner-message { color:#fff; }
</style>
