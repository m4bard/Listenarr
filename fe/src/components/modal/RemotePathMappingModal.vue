<template>
  <Modal :visible="visible" size="md" @close="closeModal">
    <template #header>
      <ModalHeader :title="(editingMapping ? 'Edit' : 'Add') + ' Remote Path Mapping'" :icon="PhLink" @close="closeModal" />
    </template>

    <template #default>
      <ModalForm @submit="handleSubmit">
        <ModalBody>
          <h3 class="modal-section-title"><PhLink /> Configuration</h3>

          <!-- Use modal body styling for these fields rather than the FormSection card -->
          <div class="modal-section-body">
            <div class="section-card">
              <div class="form-group">
                <label for="mapping-name">Mapping Name (optional)</label>
                <input
                  id="mapping-name"
                  v-model="formData.name"
                  type="text"
                  placeholder="Friendly name for this mapping"
                  class="form-control"
                />
                <small class="help-text">Friendly name to identify this mapping</small>
              </div>

              <div class="form-group">
                <label for="download-client">Download Client</label>
                <select id="download-client" v-model="formData.downloadClientId" class="form-control">
                  <option v-for="c in downloadClients" :key="c.id" :value="c.id">{{ c.name }}</option>
                </select>
              </div>

              <div class="form-group">
                <label for="remote-path" class="required">Remote Path</label>
                <div class="input-with-icon">
                  <i class="ph ph-desktop"></i>
                  <input
                    id="remote-path"
                    v-model="formData.remotePath"
                    type="text"
                    placeholder="/path/to/complete/downloads"
                    class="form-control"
                    required
                  />
                </div>
                <small class="help-text">
                  Path as seen by the download client (in its Docker container or remote system)
                </small>
              </div>

              <div class="form-group">
                <label for="local-path" class="required">Local Path</label>
                <FolderBrowser v-model="formData.localPath" :inline="true" @open-modal="showBrowser = true" />
                <FolderBrowserModal v-model:visible="showBrowser" v-model:modelValue="formData.localPath" :show-input="true" :show-files="false" @close="showBrowser = false" />
                <small class="help-text">
                  Path as seen by Listenarr (on this system where Listenarr is running)
                </small>
              </div>
            </div>
          </div>
        </ModalBody>
      </ModalForm>
    </template>

    <template #footer>
      <ModalFooter :showCancel="false">
        <template #left>
          <button class="cancel-button btn" @click="closeModal()"><PhX /> Cancel</button>
        </template>
        <template #default>
          <button type="submit" form="modal-form" class="btn btn-primary"><PhCheck /> Save</button>
        </template>
      </ModalFooter>
    </template>
  </Modal>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { PhX, PhCheck, PhLink } from '@phosphor-icons/vue'
import { Modal, ModalHeader, ModalFooter, ModalForm, ModalBody } from '@/components/modal'
import FormSection from '@/components/settings/FormSection.vue'
import FolderBrowser from '@/components/ui/FolderBrowser.vue'
import FolderBrowserModal from '@/components/modal/FolderBrowserModal.vue'
import type { DownloadClientConfiguration, RemotePathMapping } from '@/types'

interface Props {
  visible: boolean
  editingMapping: RemotePathMapping | null
  downloadClients: DownloadClientConfiguration[]
}

interface Emits {
  (e: 'close'): void
  (e: 'save', mapping: Omit<RemotePathMapping, 'id' | 'createdAt' | 'updatedAt'>): void
}

const props = defineProps<Props>()
const emit = defineEmits<Emits>()

const formData = ref({
  name: '',
  downloadClientId: '',
  remotePath: '',
  localPath: '',
})

const showBrowser = ref(false)

// Watch for prop changes to populate form data
watch(() => props.editingMapping, (newMapping) => {
  if (newMapping) {
    formData.value = {
      name: newMapping.name || '',
      downloadClientId: newMapping.downloadClientId || '',
      remotePath: newMapping.remotePath || '',
      localPath: newMapping.localPath || '',
    }
  } else {
    // Reset form for new mapping
    formData.value = {
      name: '',
      downloadClientId: '',
      remotePath: '',
      localPath: '',
    }
  }
}, { immediate: true })

const handleSubmit = () => {
  emit('save', formData.value)
}

const closeModal = () => {
  emit('close')
}
</script>