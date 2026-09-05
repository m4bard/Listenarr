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
  <div class="api-key-input">
    <input
      ref="keyField"
      class="api-key-field"
      :value="apiKey"
      :type="show ? 'text' : 'password'"
      readonly
    />
    <button
      type="button"
      class="api-key-icon visibility-icon"
      @click="toggleVisibility"
      :aria-pressed="show"
      :aria-label="show ? 'Hide API key' : 'Show API key'"
      :title="show ? 'Hide API key' : 'Show API key'"
    >
      <PhEye class="icon" v-if="!show" />
      <PhEyeSlash class="icon" v-else />
    </button>
    <button
      type="button"
      class="api-key-icon copy-icon copy-btn"
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
      class="api-key-icon regen-icon regen-btn"
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
import { PhCopy, PhCheck, PhArrowClockwise, PhEye, PhEyeSlash } from '@phosphor-icons/vue'
import { apiService } from '@/services/api'
import { showConfirm } from '@/composables/useConfirm'
import { useToast } from '@/services/toastService'
import { errorTracking } from '@/services/errorTracking'
import { copyTextToClipboard } from '@/utils/clipboard'

const props = withDefaults(defineProps<{ apiKey?: string }>(), { apiKey: '' })
const emit = defineEmits(['update:apiKey'])

const toast = useToast()
const copySuccess = ref(false)
const show = ref(false)
const keyField = ref<HTMLInputElement | null>(null)

function toggleVisibility() {
  show.value = !show.value
}

function markCopied() {
  copySuccess.value = true
  setTimeout(() => {
    copySuccess.value = false
  }, 2000)
}

/**
 * Last resort when the browser will not write to the clipboard at all: reveal the
 * key and select it, so the user can copy it with the keyboard instead of reading
 * it off the screen a character at a time.
 */
function offerKeyForManualCopy() {
  show.value = true
  const field = keyField.value
  if (!field) return
  field.focus()
  field.select?.()
}

/**
 * Copy a key and tell the user what happened either way. Returns whether the key
 * reached the clipboard.
 */
async function copyKeyToClipboard(key: string, operation: string): Promise<boolean> {
  let outcome: Awaited<ReturnType<typeof copyTextToClipboard>>
  try {
    outcome = await copyTextToClipboard(key)
  } catch (e) {
    errorTracking.captureException(e as Error, { component: 'ApiKeyControl', operation })
    outcome = 'failed'
  }

  if (outcome === 'failed') {
    toast.error(
      'Copy failed',
      'This browser would not let the page write to the clipboard. Browsers only allow that over HTTPS or on localhost. The key is selected below so you can copy it yourself.',
    )
    offerKeyForManualCopy()
    return false
  }

  markCopied()
  return true
}

async function onCopy() {
  if (!props.apiKey) return
  await copyKeyToClipboard(props.apiKey, 'onCopy')
}

async function onRegenerate() {
  const confirmed = await showConfirm(
    'Are you sure you want to regenerate the API key? This will invalidate the existing key.',
    'Regenerate API Key',
  )
  if (!confirmed) return

  let newKey = ''
  try {
    const res = props.apiKey
      ? await apiService.regenerateApiKey()
      : await apiService.generateInitialApiKey()
    newKey = res?.apiKey ?? ''
  } catch (e) {
    errorTracking.captureException(e as Error, {
      component: 'ApiKeyControl',
      operation: 'onRegenerate',
    })
    toast.error('Could not generate API key', 'The server did not return a new API key.')
    return
  }

  if (!newKey) {
    toast.error('Could not generate API key', 'The server did not return a new API key.')
    return
  }

  // The key is already live on the server, so publish it before touching the
  // clipboard. Copying is a convenience that runs afterwards and reports its own
  // failure: a clipboard error must never be reported as a failed regeneration,
  // or the user regenerates again and invalidates the key they were just given.
  emit('update:apiKey', newKey)
  await copyKeyToClipboard(newKey, 'onRegenerate')
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
  padding-right: 7.5rem; /* Make room for three icons */
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

.visibility-icon {
  right: 4.75rem;
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
