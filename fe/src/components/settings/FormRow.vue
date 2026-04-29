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
  <div class="form-group">
    <label v-if="label" :for="labelFor" class="form-row-label">{{ label }}</label>
    <div class="form-row-control">
      <slot />
    </div>
    <span v-if="help" class="form-help">{{ help }}</span>
  </div>
</template>

<script setup lang="ts">
defineProps<{ label?: string; help?: string; labelFor?: string }>()
</script>

<style scoped>
.form-row-label {
  display: block;
  margin-bottom: 0.5rem;
  font-weight: 500;
  color: #fff;
}
.form-row-control {
  display: block;
}
.form-help {
  display: block;
  margin-top: 0.5rem;
  font-size: 0.85rem;
  color: #adb5bd;
  line-height: 1.5;
}
</style>
