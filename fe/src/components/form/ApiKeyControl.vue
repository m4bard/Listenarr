<template>
  <div class="api-key-control">
    <PasswordInput :value="apiKey" readonly />
    <div class="controls">
      <button class="copy-btn" @click="onCopy" :disabled="!apiKey">Copy</button>
      <button class="regen-btn" @click="onRegenerate">{{ apiKey ? 'Regenerate' : 'Generate' }}</button>
    </div>
  </div>
</template>

<script setup lang="ts">
import PasswordInput from '@/components/form/PasswordInput.vue'
import { apiService } from '@/services/api'
import { showConfirm } from '@/composables/useConfirm'

const props = withDefaults(defineProps<{ apiKey?: string }>(), { apiKey: '' })
const emit = defineEmits(['update:apiKey'])

async function onCopy() {
  if (!props.apiKey) return
  try {
    await navigator.clipboard.writeText(props.apiKey)
  } catch (e) {
    // swallow in tests
    console.error('Clipboard write failed', e)
  }
}

async function onRegenerate() {
  const confirmed = await showConfirm('Are you sure you want to regenerate the API key? This will invalidate the existing key.', 'Regenerate API Key')
  if (!confirmed) return
  try {
    if (!props.apiKey) {
      const res = await apiService.generateInitialApiKey()
      if (res?.apiKey) {
        emit('update:apiKey', res.apiKey)
        await navigator.clipboard.writeText(res.apiKey)
      }
    } else {
      const res = await apiService.regenerateApiKey()
      if (res?.apiKey) {
        emit('update:apiKey', res.apiKey)
        await navigator.clipboard.writeText(res.apiKey)
      }
    }
  } catch (e) {
    console.error('Failed to (re)generate API key', e)
  }
}
</script>

<style scoped>
.api-key-control {
  display: flex;
  gap: 0.5rem;
  align-items: center;
}
.controls {
  display: flex;
  gap: 0.5rem;
}
.copy-btn,
.regen-btn {
  padding: 0.5rem 0.75rem;
  border-radius: 6px;
  border: none;
  cursor: pointer;
}
.copy-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>