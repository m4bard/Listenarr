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
  <Modal :visible="visible" size="sm" :title="title" @close="$emit('close')">
    <template #header>
      <ModalHeader :title="title" @close="$emit('close')">
        <template #icon><slot name="icon"><PhWarningCircle /></slot></template>
      </ModalHeader>
    </template>

    <ModalBody>
      <slot>
        <p>Are you sure?</p>
      </slot>
    </ModalBody>

    <template #footer>
      <button @click="$emit('close')" class="cancel-button btn">Cancel</button>
      <button @click="$emit('confirm')" class="delete-button modal-delete-button"> <slot name="confirm-icon"><PhTrash /></slot> {{ confirmText }}</button>
    </template>
  </Modal>
</template>

<script setup lang="ts">
import { Modal, ModalHeader, ModalBody } from '@/components/feedback'
import { PhWarningCircle, PhTrash } from '@phosphor-icons/vue' 
defineProps({
  visible: { type: Boolean, required: true },
  title: { type: String, default: 'Delete' },
  confirmText: { type: String, default: 'Delete' },
})
</script>

<style scoped>
/* small tweaks; main modal button styles live in the centralized stylesheet */
.modal-body p { margin: 0 0 1rem 0 }
</style>
