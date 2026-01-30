<template>
  <div class="folder-browser browser-inline" ref="root" role="dialog" aria-label="Folder Browser">
    <div v-if="showInput" class="browser-input-group">
      <input
        v-model="localPath"
        class="browser-input"
        type="text"
        placeholder="Enter path or browse..."
        @keydown.enter.prevent="browseDirectory(localPath)"
        aria-label="Path"
      />
      <button class="browse-button" @click="handleBrowseClick" aria-label="Browse">Browse</button>
    </div>

    <div v-if="validationMessage" :class="['validation-message', isValid ? 'success' : 'error']">
      <span>{{ validationMessage }}</span>
    </div>

    <div v-if="isOpen" class="browser-content" role="region">
      <div class="browser-header">
        <h3 class="title"><PhFolder /> <span>Folder Browser</span></h3>
        <div class="header-actions">
          <button class="icon-btn" @click="navigateToParent" title="Parent" aria-label="Parent"><PhArrowLeft /></button>
          <button class="icon-btn close" @click="closeBrowser" title="Close" aria-label="Close"><PhX /></button>
        </div>
      </div>

      <div class="browser-body">
        <div class="current-path" title="Current path">
          <PhLink class="current-icon" />
          <code class="path-text">{{ currentPath || '—' }}</code>
        </div>

        <div v-if="isLoading" class="loading-state"><PhSpinner class="ph-spin" /><div class="loading-text">Loading…</div></div>

        <div v-else>
          <div v-if="items.length === 0" class="empty-state"><PhFolderOpen class="empty-icon" /><div>No items found</div></div>

          <div class="directory-list" role="list">
            <div v-if="parentPath" class="directory-item parent-item" role="listitem" @click="selectParentPath">
              <div class="item-icon">⬆</div>
              <div class="directory-item-main">.. <span class="muted">(parent)</span></div>
            </div>

            <div v-for="it in items" :key="it.path" :class="['directory-item', it.isDirectory ? '' : 'file-item']" role="listitem" @click="handleItemClick(it)">
              <div class="item-icon" :aria-hidden="true"><PhFolder v-if="it.isDirectory" style="color: #ffc857" /><PhFile v-else /></div>
              <div class="directory-item-main">
                <div class="item-name">{{ it.name }}</div>
                <small v-if="it.size" class="item-meta">{{ formatSize(it.size) }}</small>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { apiService } from '@/services/api'
import { PhFolder, PhArrowLeft, PhX, PhLink, PhFolderOpen, PhSpinner, PhFile } from '@phosphor-icons/vue'

interface FileSystemItem {
  name: string
  path: string
  isDirectory: boolean
  size?: number | null
}

const props = defineProps({
  showFiles: { type: Boolean, default: false },
  modelValue: { type: String, required: false },
  showInput: { type: Boolean, default: true },
  inline: { type: Boolean, default: false },
  autoBrowse: { type: Boolean, default: true },
})

const { showInput } = props
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

function formatSize(size?: number | null) {
  if (size == null) return ''
  const units = ['bytes', 'KB', 'MB', 'GB', 'TB']
  let v = size
  let i = 0
  while (v >= 1024 && i < units.length - 1) {
    v = v / 1024
    i++
  }
  return `${Math.round(v * 10) / 10} ${units[i]}`
}

function navigateToParent() {
  if (parentPath.value) browseDirectory(parentPath.value)
}

function selectParentPath() {
  if (!parentPath.value) return
  localPath.value = parentPath.value
  emit('update:modelValue', parentPath.value)
  if (props.inline) {
    isOpen.value = false
    emit('browser-closed')
    return
  }
  browseDirectory(parentPath.value)
}

function handleItemClick(item: FileSystemItem) {
  if (!item.isDirectory) return
  localPath.value = item.path
  emit('update:modelValue', item.path)
  browseDirectory(item.path)
}

const isOpen = ref(!props.inline)

function closeBrowser() {
  isOpen.value = false
  emit('browser-closed')
}

function handleBrowseClick() {
  // Always show base level (undefined) when Browse is clicked
  if (props.inline && !isOpen.value) {
    isOpen.value = true
    emit('browser-opened')
    // Always load root on user-initiated open (autoBrowse controls only mount-time behavior)
    browseDirectory(undefined)
    return
  }
  // if already open or not inline, show base level by default
  browseDirectory(undefined)
}

onMounted(() => {
  if (props.modelValue) localPath.value = props.modelValue
  // initial browse for convenience (only when open)
  if (isOpen.value && props.autoBrowse) {
    browseDirectory(localPath.value || undefined)
  }
})

// Keep in sync when the parent updates the modelValue (e.g., recent folder selection)
watch(() => props.modelValue, (v) => {
  localPath.value = v ?? ''
  // Validate the incoming path so the parent sees validation feedback immediately
  if (localPath.value) validatePath()
})
</script>

<style scoped>
/* Align with centralized modal palette */
.folder-browser { display:flex; flex-direction:column; gap:0.75rem; width:100% }
.browser-input-group { display:flex; gap:0.5rem }
.browser-input {
  flex:1;
  padding:0.6rem 0.75rem;
  background: #2a2a2a; /* match modal body */
  border: 1px solid #444;
  border-radius:6px;
  color:#eef2f8;
  font-size:0.95rem;
}
.browse-button {
  padding:0.45rem 0.85rem;
  background:#2196f3;
  color:#fff;
  border:none;
  border-radius:6px;
  font-weight:600;
}

.browser-content {
  background:#2a2a2a; /* modal-content background */
  border:1px solid #444; /* modal border */
  border-radius:8px;
  width:100%;
  overflow: hidden;
}

.browser-header {
  display:flex; justify-content:space-between; align-items:center; padding:0.8rem 1rem; border-bottom:1px solid #444;
}
.browser-header .title { display:flex; align-items:center; gap:0.5rem; color:#fff; font-weight:600 }
.header-actions { display:flex; gap:0.5rem }
.icon-btn { background:rgba(255,255,255,0.03); border:1px solid rgba(255,255,255,0.04); padding:0.3rem 0.45rem; border-radius:6px; display:inline-flex; align-items:center; justify-content:center; color:#fff; cursor:pointer; transition: all 0.2s; }
.icon-btn.close { background:none; border:none; color:#b3b3b3; padding:0.35rem; border-radius:6px; transition: all 0.2s; }
.icon-btn:hover { background:rgba(255,255,255,0.08); }
.icon-btn.close:hover { background:#333; color:#fff; }

.browser-body { padding:1rem }
.current-path { display:flex; align-items:center; gap:0.5rem; color:rgba(255,255,255,0.65); margin-bottom:0.5rem; font-size:0.9rem }
.current-path .path-text { background:#2d2d2d; padding:0.35rem 0.6rem; border-radius:6px; color:#e6eef8 }

.loading-state { display:flex; gap:0.6rem; align-items:center; color:rgba(255,255,255,0.65) }
.loading-text { font-size:0.95rem }
.empty-state { display:flex; gap:0.75rem; align-items:center; color:rgba(255,255,255,0.65) }
.empty-icon { color:#ffc857 }

.directory-list { display:flex; flex-direction:column; gap:0.5rem; max-height:320px; overflow:auto; padding-right:0.25rem }
.directory-list::-webkit-scrollbar { width:10px }
.directory-list::-webkit-scrollbar-thumb { background: rgba(255,255,255,0.04); border-radius:6px }

.directory-item { display:flex; gap:0.75rem; align-items:center; padding:0.9rem; background:#2c2c2c; border:1px solid #3a3a3a; border-radius:8px; color:#fff; cursor:pointer; transition: all 0.2s ease; }
.directory-item:hover { transform:translateY(-1px); box-shadow:0 2px 8px rgba(0,0,0,0.3); background:#2e2e2e; }
.directory-item .item-icon { width:36px; height:36px; display:inline-flex; align-items:center; justify-content:center; border-radius:6px; background:rgba(255,255,255,0.02); color:#fff; font-size:18px }
.directory-item .item-name { font-weight:600 }
.directory-item .item-meta { color:rgba(255,255,255,0.65); display:block; margin-top:4px; font-size:0.85rem }
.directory-item.parent-item { opacity:0.95; font-style:italic }
.file-item { opacity:0.85 }

.validation-message { padding:0.5rem; border-radius:6px; font-size:0.9rem }
.validation-message.error { background:rgba(231,76,60,0.1); color:#e74c3c }
.validation-message.success { background:rgba(46,204,113,0.08); color:#2ecc71 }

@media (max-width:720px) {
  .browser-content { max-width:100%; }
  .directory-list { max-height:220px }
}
</style>
