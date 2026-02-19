<template>
  <div class="form-group checkbox-group">
    <Checkbox :modelValue="modelValue" @update:modelValue="$emit('update:modelValue', $event)">
      <span>
        <strong>{{ title }}</strong>
        <small v-if="description">{{ description }}</small>
        <slot />
      </span>
    </Checkbox>
  </div>
</template>

<script setup lang="ts">
import Checkbox from '@/components/form/Checkbox.vue'

const props = defineProps<{ modelValue?: boolean; title: string; description?: string }>()
const emit = defineEmits(['update:modelValue'])
</script>

<style scoped>
/* Reuse .checkbox-group styles from global.css; no extra styles needed here */
</style>
