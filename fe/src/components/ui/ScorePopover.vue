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
  <div class="score-popover">
    <slot>
      <span class="score">{{ score }}</span>
    </slot>
  </div>
</template>

<script setup lang="ts">
withDefaults(defineProps<{ score?: number }>(), { score: 0 })
</script>

<style scoped>
.score-popover { display:inline-flex; align-items:center }
.score { background: rgba(255,255,255,0.04); padding: 0.15rem 0.5rem; border-radius:4px }
</style>
