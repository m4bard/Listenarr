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
  <div
    data-modal-body="true"
    :class="[
      'modal-body',
      { 'no-padding': noPadding, 'modal-body--compact': compact, 'modal-body--tight': tight },
    ]"
    :style="maxHeightStyle"
  >
    <slot />
  </div>
</template>

<script setup lang="ts">
import type { CSSProperties } from 'vue'

const props = defineProps({
  noPadding: { type: Boolean, default: false },
  compact: { type: Boolean, default: false },
  tight: { type: Boolean, default: false },
  maxHeight: { type: String, required: false },
})

const maxHeightStyle = props.maxHeight
  ? ({ maxHeight: props.maxHeight, overflowY: 'auto' } as CSSProperties)
  : undefined
</script>

<style scoped>
.modal-body {
  padding: 2rem;
  overflow-y: auto;
  flex: 1;
}

.no-padding {
  padding: 0;
}
</style>
