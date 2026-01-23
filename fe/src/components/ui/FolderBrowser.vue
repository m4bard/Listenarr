<template>
  <div class="folder-browser browser-inline" ref="root">
    <div class="browser-input-group">
      <input v-model="localPath" class="browser-input" type="text" placeholder="Enter path or browse..." @keydown.enter.prevent="browseDirectory(localPath)" />
      <button class="browse-button" @click="browseDirectory(localPath)">Browse</button>
    </div>

    <div v-if="validationMessage" :class="['validation-message', isValid ? 'success' : 'error']">
      <span>{{ validationMessage }}</span>
    </div>

    <div class="browser-content">
      <div class="browser-header">
        <h3><i class="ph ph-folder"></i> Folder Browser</h3>
        <div>
          <button class="back-button" @click="navigateToParent" title="Parent"><i class="ph ph-arrow-left"></i></button>
          <button class="close-button" @click="closeBrowser" title="Close">✕</button>
        </div>
      </div>

      <div class="browser-body">
        <div class="current-path"><i class="ph ph-link"></i><span>{{ currentPath || '—' }}</span></div>

        <div v-if="isLoading" class="loading-state"><i>⏳</i><div>Loading…</div></div>

        <div v-else>
          <div v-if="items.length === 0" class="empty-state"><i>📁</i><div>No items found</div></div>

          <div class="directory-list">
            <div v-if="parentPath" class="directory-item parent-item" @click="browseDirectory(parentPath)">
              <i>⬆</i>
              <div class="directory-item-main">.. (parent)</div>
            </div>

            <div v-for="it in items" :key="it.path" :class="['directory-item', it.isDirectory ? '' : 'file-item']" @click="handleItemClick(it)">
              <i v-if="it.isDirectory">📁</i>
              <i v-else>📄</i>
              <div class="directory-item-main">{{ it.name }}</div>
              <div class="directory-item-actions"> <small>{{ it.size ? (it.size + ' bytes') : '' }}</small></div>
            </div>
          </div>
        </div>
      </div>

      <div class="browser-footer">
        <button class="cancel-button" @click="closeBrowser">Cancel</button>
        <button class="select-button" @click="selectCurrentPath" :disabled="!isValid">Select</button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { apiService } from '@/services/api'

interface FileSystemItem {
  name: string
  path: string
  isDirectory: boolean
  size?: number | null
}

const props = defineProps({
  showFiles: { type: Boolean, default: false },
  modelValue: { type: String, required: false },
})
const emit = defineEmits<{
  (e: 'update:modelValue', v: string | null): void
  (e: 'browser-opened'): void
  (e: 'browser-closed'): void
}>()

const root = ref<HTMLElement | null>(null)
const localPath = ref(props.modelValue ?? '')
const currentPath = ref<string | null>(null)
const parentPath = ref<string | null>(null)
const items = ref<FileSystemItem[]>([])
const isLoading = ref(false)
const validationMessage = ref('')
const isValid = ref(false)

async function browseDirectory(path?: string) {
  isLoading.value = true
  validationMessage.value = ''
  try {
    const p = path && path.length ? path : undefined
    const r = await apiService.browseDirectory(p)
    // Expect: { currentPath, parentPath, items }
    currentPath.value = r.currentPath
    parentPath.value = r.parentPath ?? null
    items.value = (r.items || []).filter((it: any) => (props.showFiles ? true : it.isDirectory))
    localPath.value = currentPath.value || localPath.value
    // Validate path after browsing
    await validatePath()
  } catch (err) {
    validationMessage.value = 'Failed to browse directory'
    console.error('browseDirectory error', err)
  } finally {
    isLoading.value = false
  }
}

async function validatePath() {
  if (!localPath.value) {
    isValid.value = false
    validationMessage.value = ''
    return
  }
  try {
    const res = await apiService.validatePath(localPath.value)
    isValid.value = !!(res && res.isValid)
    validationMessage.value = res?.message ?? (isValid.value ? 'Valid' : 'Invalid')
  } catch (err) {
    isValid.value = false
    validationMessage.value = 'Failed to validate path'
  }
}

function navigateToParent() {
  if (parentPath.value) browseDirectory(parentPath.value)
}

function handleItemClick(item: FileSystemItem) {
  if (item.isDirectory) browseDirectory(item.path)
}

function selectCurrentPath() {
  if (!currentPath.value) return
  localPath.value = currentPath.value
  emit('update:modelValue', currentPath.value)
  emit('browser-closed')
}

function closeBrowser() {
  emit('browser-closed')
}

onMounted(() => {
  if (props.modelValue) localPath.value = props.modelValue
  // initial browse for convenience
  browseDirectory(localPath.value || undefined)
})
</script>

<style scoped>
/* Reuse the existing styles from file (kept in place to preserve visual look) */
.folder-browser { display:flex; flex-direction:column; gap:0.5rem }
.browser-input-group { display:flex; gap:0.5rem }
.browser-input { flex:1; padding:0.75rem; background:#2a2a2a; border:1px solid #3a3a3a; border-radius:6px; color:#fff }
.browse-button { padding:0.5rem 1rem; background:#007acc; color:#fff; border:none; border-radius:6px }
.browser-content { background:#2a2a2a; border:1px solid #444; border-radius:6px; width:100%; max-width:700px }
.browser-header { display:flex; justify-content:space-between; align-items:center; padding:1rem; border-bottom:1px solid #444 }
.browser-body { padding:1rem }
.directory-list { display:flex; flex-direction:column; gap:0.5rem }
.directory-item { padding:1rem; background:#333; border:1px solid #444; border-radius:6px; color:#fff }
.browser-footer { display:flex; justify-content:flex-end; padding:1rem; border-top:1px solid #444 }
.cancel-button, .select-button { padding:0.5rem 1rem; border-radius:6px }
.cancel-button { background:#555; color:#fff }
.select-button { background:#007acc; color:#fff }
.validation-message { padding:0.5rem; border-radius:6px }
.validation-message.error { background:rgba(231,76,60,0.1); color:#e74c3c }
.validation-message.success { background:rgba(46,204,113,0.1); color:#2ecc71 }
</style>
