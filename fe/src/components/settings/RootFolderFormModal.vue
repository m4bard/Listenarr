<template>
  <Modal :visible="true" size="md" @close="close">
    <template #header>
      <div class="modal-title">
        <h3><PhFolder /> {{ root?.id ? 'Edit Root Folder' : 'Add Root Folder' }}</h3>
      </div>
      <button class="close-btn" @click="close">
        <PhX />
      </button>
    </template>

    <template #default>
      <div class="form-row">
        <label>Name</label>
        <input v-model="form.name" placeholder="Enter a name for this root folder" />
      </div>

      <div class="form-row path-row">
        <label for="root-path">Path</label>
        <div class="path-input-row">
          <input id="root-path" v-model="form.path" placeholder="Select or enter a path..." />
          <button type="button" class="browse-inline-btn" @click="openBrowser">Browse</button>
        </div>

        <!-- Folder browser overlay shown only when browsing -->
        <div v-if="showBrowser" class="folder-browser-overlay">
          <FolderBrowser v-model="form.path" :show-input="false" @browser-closed="closeBrowser" />
        </div>
      </div>

      <div class="form-row">
        <label class="checkbox-label">
          <input type="checkbox" v-model="form.isDefault" />
          <span>Set as default root folder</span>
        </label>
      </div>
    </template>

    <template #footer>
      <button class="cancel-button" @click="close"><PhX /> Cancel</button>
      <button class="btn btn-primary" @click="save" :disabled="!form.name || !form.path"><PhCheck /> Save</button>
    </template>
  </Modal> 

      <!-- Rename confirmation modal (shared) -->
      <Modal :visible="showConfirm" size="md" @close="showConfirm = false">
        <template #header>
          <h3>Move audiobook files?</h3>
        </template>

        <template #default>
          <div class="confirm-body">
            <p>You're changing the root path and may move all files from:</p>
            <pre>{{ root?.path || '&lt;none&gt;' }}</pre>
            <p>to:</p>
            <pre>{{ form.path || '&lt;none&gt;' }}</pre>
            <div class="checkbox-row">
              <label>
                <input type="checkbox" v-model="modalMoveFiles" />
                <strong>Move files</strong> (recommended)
              </label>
            </div>
            <div class="checkbox-row" v-if="modalMoveFiles">
              <label>
                <input type="checkbox" v-model="modalDeleteEmpty" />
                Delete original folder if empty
              </label>
            </div>
          </div>
        </template>

        <template #footer>
          <button class="cancel-button" @click="showConfirm = false"><PhX /> Cancel</button>
          <button class="cancel-button" @click="confirmChange(false)">Change without moving</button>
          <button class="btn btn-primary" @click="confirmChange(true)">Move Files</button>
        </template>
      </Modal>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import FolderBrowser from '@/components/ui/FolderBrowser.vue'
import { Modal } from '@/components/modal'
import { PhFolder, PhX, PhCheck } from '@phosphor-icons/vue' 
import { useRootFoldersStore } from '@/stores/rootFolders'
import { useToast } from '@/services/toastService'
import type { RootFolder } from '@/types'

const { root } = defineProps<{ root?: RootFolder }>()
const emit = defineEmits(['close', 'saved'])

const store = useRootFoldersStore()
const toast = useToast()

const form = ref({ name: root?.name || '', path: root?.path || '', isDefault: !!root?.isDefault })

const showConfirm = ref(false)
const modalMoveFiles = ref(true)
const modalDeleteEmpty = ref(true)

// Local state for showing the inline folder browser
const showBrowser = ref(false)

function openBrowser() {
  showBrowser.value = true
}
function closeBrowser() {
  showBrowser.value = false
}
function close() {
  emit('close')
}

async function save() {
  if (!form.value.name || !form.value.path) {
    toast.error('Validation Error', 'Name and Path are required')
    return
  }
  try {
    if (root?.id) {
      // If path changed, show confirmation to choose whether to move files
      if (form.value.path !== root.path) {
        showConfirm.value = true
        return
      }
      await store.update(root.id, {
        id: root.id,
        name: form.value.name,
        path: form.value.path,
        isDefault: form.value.isDefault,
      })
      toast.success('Success', 'Root folder updated')
    } else {
      await store.create({
        name: form.value.name,
        path: form.value.path,
        isDefault: form.value.isDefault,
      })
      toast.success('Success', 'Root folder created')
    }
    emit('saved')
  } catch (e: unknown) {
    const error = e as Error
    toast.error('Error', error?.message || 'Failed to save root folder')
  }
}

async function confirmChange(moveFiles: boolean) {
  showConfirm.value = false
  try {
    await store.update(
      root!.id,
      {
        id: root!.id,
        name: form.value.name,
        path: form.value.path,
        isDefault: form.value.isDefault,
      },
      { moveFiles: moveFiles, deleteEmptySource: modalDeleteEmpty.value },
    )
    toast.success(
      'Success',
      moveFiles ? 'Root renamed and move jobs queued' : 'Root renamed (files unchanged)',
    )
    emit('saved')
  } catch (e: unknown) {
    const error = e as Error
    toast.error('Error', error?.message || 'Failed to save root folder')
  }
}
</script>

<style scoped>
/* Modal-specific styling is now provided by shared `modals.css` */
.path-row .path-input-row { display:flex; gap:0.5rem; align-items:center }
.path-row input#root-path { flex:1 }
.browse-inline-btn { padding:0.45rem 0.7rem; border-radius:6px; background:var(--accent, #2196f3); color:#fff; border:none }
.folder-browser-overlay { margin-top:0.75rem }

.modal-close {
  background: none;
  border: none;
  color: #adb5bd;
  cursor: pointer;
  padding: 0.5rem;
  border-radius: 4px;
  transition: all 0.2s ease;
  display: flex;
  align-items: center;
  justify-content: center;
}

.modal-close:hover {
  background: #444;
  color: #fff;
}

.modal-body {
  padding: 2rem;
  flex: 1;
  overflow-y: auto;
}

.form-row {
  margin-bottom: 1.5rem;
}

.form-row:last-child {
  margin-bottom: 0;
}

.form-row label {
  display: block;
  margin-bottom: 0.5rem;
  font-weight: 500;
  color: #fff;
}

.form-row input {
  width: 100%;
  padding: 0.75rem;
  background: #1a1a1a;
  border: 1px solid #444;
  border-radius: 6px;
  color: #fff;
  font-size: 0.95rem;
  transition: all 0.2s ease;
}

.form-row input:focus {
  outline: none;
  border-color: #2196f3;
  box-shadow: 0 0 0 3px rgba(33, 150, 243, 0.1);
}

.form-row input::placeholder {
  color: #666;
}

.checkbox-label {
  display: flex;
  align-items: center;
  gap: 1rem;
  color: #ddd;
  cursor: pointer;
  user-select: none;
  padding: 0.5rem 0;
}

.checkbox-label input[type='checkbox'] {
  width: 18px;
  height: 18px;
  margin: 0;
  cursor: pointer;
  flex-shrink: 0;
  -webkit-appearance: none;
  appearance: none;
  background-color: #1a1a1a;
  border: 2px solid #555;
  border-radius: 6px;
  position: relative;
  transition: all 0.2s ease;
  vertical-align: sub;
}

.checkbox-label input[type='checkbox']:hover {
  border-color: #007acc;
}

.checkbox-label input[type='checkbox']:checked {
  background-color: #007acc;
  border-color: #007acc;
}

.checkbox-label input[type='checkbox']:focus {
  outline: 2px solid rgba(0, 122, 204, 0.3);
  outline-offset: 2px;
}

.checkbox-label span {
  line-height: 1.4;
  font-size: 0.95rem;
  margin-left: 0.25rem;
}

/* modal-footer base styles are centralized in src/assets/modals.css; keep modal-specific background and spacing */
.modal-footer {
  justify-content: flex-end;
  gap: 0.75rem;
  background: #2a2a2a;
}

/* Base `.btn` styles are centralized in src/assets/modals.css; avoid duplicating them here. */

.btn:hover:not(:disabled) {
  background: #444;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn.primary {
  background: linear-gradient(135deg, #1e88e5 0%, #1565c0 100%);
  color: white;
}

.btn.primary:hover:not(:disabled) {
  background: linear-gradient(135deg, #1976d2 0%, #0d47a1 100%);
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(30, 136, 229, 0.4);
}

.confirm-modal {
  z-index: 1001;
}

.confirm-modal .modal-content {
  max-width: 560px;
}

.confirm-body {
  padding: 1rem;
  background: #1a1a1a;
  border-radius: 6px;
  border: 1px solid #444;
}

.confirm-body p {
  margin: 0 0 0.5rem 0;
  font-weight: 500;
  color: #fff;
}

.confirm-body pre {
  background: #2a2a2a;
  padding: 0.75rem;
  border-radius: 4px;
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 0.85rem;
  word-break: break-all;
  border: 1px solid #444;
  margin: 0.5rem 0;
  color: #adb5bd;
  white-space: pre-wrap;
}

.checkbox-row {
  margin-top: 1rem;
}

.checkbox-row label {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  color: #ddd;
  cursor: pointer;
  user-select: none;
  padding: 0.5rem 0;
}

.checkbox-row label input[type='checkbox'] {
  width: 18px;
  height: 18px;
  margin: 0;
  cursor: pointer;
  flex-shrink: 0;
  -webkit-appearance: none;
  appearance: none;
  background-color: #1a1a1a;
  border: 2px solid #555;
  border-radius: 6px;
  position: relative;
  transition: all 0.2s ease;
  vertical-align: sub;
}

.checkbox-row label input[type='checkbox']:hover {
  border-color: #007acc;
}

.checkbox-row label input[type='checkbox']:checked {
  background-color: #007acc;
  border-color: #007acc;
}

.checkbox-row label input[type='checkbox']:checked::after {
  content: '';
  position: absolute;
  left: 5px;
  top: 2px;
  width: 4px;
  height: 8px;
  border: solid white;
  border-width: 0 2px 2px 0;
  transform: rotate(45deg);
}

.checkbox-row label input[type='checkbox']:focus {
  outline: 2px solid rgba(0, 122, 204, 0.3);
  outline-offset: 2px;
}
</style>
