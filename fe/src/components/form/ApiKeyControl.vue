<template>
  <div class="api-key-input">
    <input class="api-key-field" :value="apiKey" type="password" readonly />
    <button
      type="button"
      class="api-key-icon copy-icon"
      @click="onCopy"
      :disabled="!apiKey"
      :aria-pressed="copySuccess"
      :aria-label="copySuccess ? 'Copied!' : 'Copy API key'"
      :title="copySuccess ? 'Copied!' : 'Copy API key'"
    >
      <PhCopy class="icon" v-if="!copySuccess" />
      <PhCheck class="icon" v-else />
    </button>
    <button
      type="button"
      class="api-key-icon regen-icon"
      @click="onRegenerate"
      :aria-label="apiKey ? 'Regenerate API key' : 'Generate API key'"
      :title="apiKey ? 'Regenerate API key' : 'Generate API key'"
    >
      <PhArrowClockwise class="icon" />
    </button>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { PhCopy, PhCheck, PhArrowClockwise } from '@phosphor-icons/vue'
import { apiService } from '@/services/api'
import { showConfirm } from '@/composables/useConfirm'

const props = withDefaults(defineProps<{ apiKey?: string }>(), { apiKey: '' })
const emit = defineEmits(['update:apiKey'])

const copySuccess = ref(false)

async function onCopy() {
  if (!props.apiKey) return
  try {
    await navigator.clipboard.writeText(props.apiKey)
    copySuccess.value = true
    setTimeout(() => {
      copySuccess.value = false
    }, 2000)
  } catch (e) {
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
        copySuccess.value = true
        setTimeout(() => {
          copySuccess.value = false
        }, 2000)
      }
    } else {
      const res = await apiService.regenerateApiKey()
      if (res?.apiKey) {
        emit('update:apiKey', res.apiKey)
        await navigator.clipboard.writeText(res.apiKey)
        copySuccess.value = true
        setTimeout(() => {
          copySuccess.value = false
        }, 2000)
      }
    }
  } catch (e) {
    console.error('Failed to (re)generate API key', e)
  }
}
</script>

<style scoped>
.api-key-input {
  position: relative;
  display: inline-block;
  width: 100%;
}

.api-key-field {
  padding: 0.75rem;
  padding-right: 6.5rem; /* Make room for both icons */
  border: 1px solid #3a3a3a;
  border-radius: 6px;
  background-color: #1a1a1a;
  color: white;
  font-size: 1rem;
  width: 100%;
  box-sizing: border-box;
  font-family: monospace;
}

.api-key-field:focus {
  outline: none;
  border-color: var(--brand-500);
}

.api-key-icon {
  position: absolute;
  top: 50%;
  transform: translateY(-50%);
  background: none;
  border: none;
  color: #adb5bd;
  cursor: pointer;
  padding: 0.5rem;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 4px;
  transition: all 0.2s;
  font-size: 1rem;
}

.api-key-icon:focus {
  outline: 2px solid rgba(var(--brand-rgb), 0.18);
  outline-offset: 2px;
}

.api-key-icon:hover:not(:disabled) {
  color: var(--brand-500);
  background: rgba(var(--brand-rgb), 0.1);
}

.api-key-icon:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.copy-icon {
  right: 2.75rem;
}

.regen-icon {
  right: 0.75rem;
}

.icon {
  width: 1.2rem;
  height: 1.2rem;
}
</style>