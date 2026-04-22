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
  <form id="modal-form" @submit.prevent="onSubmit" ref="formEl" class="modal-form">
    <slot />
  </form>
</template>

<script setup lang="ts">
import { ref } from 'vue'
defineProps({
  submitting: { type: Boolean, default: false },
})
const emit = defineEmits(['submit'])
const formEl = ref<HTMLFormElement | null>(null)

function onSubmit() {
  emit('submit')
}

// Note: we intentionally do not call defineExpose here to avoid test-environment issues.
// If a programmatic submit is required by a caller, they can call the native form's submit via refs.
</script>

<style scoped>
.modal-form { display:flex; flex-direction:column; min-height: 100%; }
</style>
