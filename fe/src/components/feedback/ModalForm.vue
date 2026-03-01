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
