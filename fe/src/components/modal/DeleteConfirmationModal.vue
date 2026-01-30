<template>
  <Modal :visible="visible" size="sm" :title="title" @close="$emit('close')">
    <template #header>
      <div class="modal-title">
        <h3><slot name="icon"><PhWarningCircle /></slot> {{ title }}</h3>
      </div>
      <button class="close-btn" @click="$emit('close')">
        <slot name="close-icon"><PhX /></slot>
      </button>
    </template>

    <ModalBody>
      <slot>
        <p>Are you sure?</p>
      </slot>
    </ModalBody>

    <template #footer>
      <button @click="$emit('close')" class="cancel-button">Cancel</button>
      <button @click="$emit('confirm')" class="delete-button modal-delete-button"> <slot name="confirm-icon"><PhTrash /></slot> {{ confirmText }}</button>
    </template>
  </Modal>
</template>

<script setup lang="ts">
import { Modal, ModalBody } from '@/components/modal'
import { PhWarningCircle, PhTrash, PhX } from '@phosphor-icons/vue' 
const props = defineProps({
  visible: { type: Boolean, required: true },
  title: { type: String, default: 'Delete' },
  confirmText: { type: String, default: 'Delete' },
})
</script>

<style scoped>
/* small tweaks; main modal button styles live in the centralized stylesheet */
.modal-body p { margin: 0 0 1rem 0 }
</style>