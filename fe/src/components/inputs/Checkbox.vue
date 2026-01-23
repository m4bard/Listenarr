<template>
  <label class="input-checkbox">
    <input type="checkbox" :checked="modelValue" @change="$emit('update:modelValue', ($event.target as HTMLInputElement)?.checked ?? false)" />
    <span class="checkbox-box" aria-hidden="true"></span>
    <span class="checkbox-label"><slot /></span>
  </label> 

</template>

<script setup lang="ts">
const props = defineProps({ modelValue: { type: Boolean, default: false } })
const emit = defineEmits(['update:modelValue'])
</script>

<style scoped>
.input-checkbox { display:inline-flex; align-items:center; gap:0.5rem; cursor:pointer; }
.input-checkbox input { position:absolute; opacity:0; width:0; height:0 }
.checkbox-box { width:16px; height:16px; border:1px solid rgba(255,255,255,0.12); border-radius:3px; background:transparent; display:inline-block }
.input-checkbox input:checked + .checkbox-box { background:#2196F3; border-color:#2196F3 }
.checkbox-label { color:var(--text, #e6eef8) }
</style>