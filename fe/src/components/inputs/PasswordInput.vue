<template>
  <div class="password-input">
    <input v-bind="$attrs" :type="show ? 'text' : 'password'" :value="modelValue" @input="onInput" />
    <button type="button" class="password-toggle" @click="toggle">
      {{ show ? 'Hide' : 'Show' }}
    </button>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'

const props = withDefaults(defineProps<{ modelValue?: string }>(), { modelValue: '' })
const emit = defineEmits<{ (e: 'update:modelValue', v: string): void }>()

const show = ref(false)
function toggle() {
  show.value = !show.value
}

function onInput(e: Event) {
  const v = (e.target as HTMLInputElement).value
  emit('update:modelValue', v)
}
</script>

<style scoped>
.password-input { display:flex; gap:0.5rem; align-items:center }
.password-toggle { background:transparent; border:none; color:#4dabf7; cursor:pointer }
</style>
