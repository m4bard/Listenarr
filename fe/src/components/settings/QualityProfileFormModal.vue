<template>
  <Modal :visible="visible" size="lg" @close="closeModal">
    <template #header>
      <ModalHeader :title="profile ? 'Edit Quality Profile' : 'Create Quality Profile'" :icon="PhStar" @close="closeModal" />
    </template>

    <template #default>

      <ModalBody>
        <form @submit.prevent="handleSubmit">
          <!-- Basic Information -->
          <FormSection title="Basic Information" :icon="PhInfo">
            <FormRow label="Profile Name *" labelFor="name">
              <input id="name" v-model="formData.name" type="text" required placeholder="e.g., High Quality, Any Quality, Space Saver" />
            </FormRow>

            <FormRow label="Description" labelFor="description">
              <textarea id="description" v-model="formData.description" rows="2" placeholder="Optional description of this quality profile"></textarea>
            </FormRow>

            <CheckboxCard v-model="formData.isDefault" title="Set as default profile" />
            <small class="info-text">
              The default profile will be automatically assigned to new audiobooks
            </small>
          </FormSection>

          <!-- Quality Definitions -->
          <FormSection title="Quality Definitions" :icon="PhCheckSquare">
            <p class="section-description">
              Select which qualities to allow and set their priority (higher priority = preferred).
              The cutoff quality determines when to stop upgrading.
            </p>

            <div class="quality-list">
              <div v-for="quality in availableQualities" :key="quality" class="quality-item">
                <Checkbox :modelValue="isQualityAllowed(quality)" @update:modelValue="(val: boolean) => toggleQuality(quality, val)">
                  <span class="quality-name">{{ quality }}</span>
                </Checkbox> 

                <div v-if="isQualityAllowed(quality)" class="quality-controls">
                  <label class="priority-label">
                    Priority:
                    <input type="number" :value="getQualityPriority(quality)"
                      @input="updateQualityPriority(quality, $event)" min="0" max="100" class="priority-input" />
                  </label>

                  <label class="radio-label">
                    <input type="radio" :value="quality" v-model="formData.cutoffQuality"
                      :disabled="!isQualityAllowed(quality)" />
                    <span class="cutoff-text">Cutoff</span>
                  </label>
                </div>
              </div>
            </div>

            <small class="info-text">
              <PhInfo />
              Cutoff quality: Downloads will stop upgrading once this quality is reached
            </small>
          </FormSection>

          <!-- Format Preferences -->
          <FormSection title="Format Preferences" :icon="PhFileAudio">
            <p class="section-description">
              Preferred audio formats in order of preference (most preferred first).
            </p>

            <div class="tag-input-group">
              <div :class="['tags-list', { 'tags-list-empty': (formData.preferredFormats?.length || 0) === 0 }]">
                <div v-for="(format, index) in formData.preferredFormats" :key="index" class="tag removable">
                  {{ format }}
                  <button type="button" @click="removeFormat(index)" class="tag-remove">
                    <PhX />
                  </button>
                </div>
              </div>
              <div class="tag-input">
                <input v-model="newFormat" @keypress.enter.prevent="addFormat" type="text"
                  placeholder="e.g., M4B, MP3, M4A" />
                <button type="button" @click="addFormat" :disabled="!newFormat.trim()" :aria-disabled="!newFormat.trim()" class="btn icon-btn btn-primary btn-sm" title="Add format" aria-label="Add format">
                  <PhPlus />
                </button>
              </div>
            </div>
          </FormSection>

          <!-- Size Limits -->
          <FormSection title="Size Limits" :icon="PhRuler">
            <p class="section-description">
              Set minimum and maximum file sizes in megabytes (leave blank for no limit).
            </p>

            <div class="form-row">
              <FormRow label="Minimum Size (MB)" labelFor="minimumSize">
                <input id="minimumSize" v-model.number="formData.minimumSize" type="number" min="0" placeholder="No minimum" />
              </FormRow>

              <FormRow label="Maximum Size (MB)" labelFor="maximumSize">
                <input id="maximumSize" v-model.number="formData.maximumSize" type="number" min="0" placeholder="No maximum" />
              </FormRow>
            </div>
          </FormSection>

          <!-- Word Filters -->
          <FormSection title="Word Filters" :icon="PhTextAa">
            <!-- Preferred Words -->
            <div class="filter-group">
              <h4><PhSparkle /> Preferred Words (Bonus Points)</h4>
              <p class="section-description">
                Releases containing these words will receive bonus points in scoring.
              </p>
              <div class="tag-input-group">
                <div :class="['tags-list', { 'tags-list-empty': (formData.preferredWords?.length || 0) === 0 }]">
                  <div v-for="(word, index) in formData.preferredWords" :key="index" class="tag positive removable">
                    {{ word }}
                    <button type="button" @click="removePreferredWord(index)" class="tag-remove">
                      <PhX />
                    </button>
                  </div>
                </div>
                <div class="tag-input">
                  <input v-model="newPreferredWord" @keypress.enter.prevent="addPreferredWord" type="text"
                    placeholder="e.g., unabridged, complete" />
                  <button type="button" @click="addPreferredWord" :disabled="!newPreferredWord.trim()" :aria-disabled="!newPreferredWord.trim()" class="btn icon-btn btn-primary btn-sm" title="Add preferred word" aria-label="Add preferred word">
                    <PhPlus />
                  </button>
                </div>
              </div>
            </div>

            <!-- Must Contain -->
            <div class="filter-group">
              <h4><PhCheck /> Must Contain (Required)</h4>
              <p class="section-description">
                Releases MUST contain at least one of these words (case-insensitive).
              </p>
              <div class="tag-input-group">
                <div :class="['tags-list', { 'tags-list-empty': (formData.mustContain?.length || 0) === 0 }]">
                  <div v-for="(word, index) in formData.mustContain" :key="index" class="tag required removable">
                    {{ word }}
                    <button type="button" @click="removeMustContain(index)" class="tag-remove">
                      <PhX />
                    </button>
                  </div>
                </div>
                <div class="tag-input">
                  <input v-model="newMustContain" @keypress.enter.prevent="addMustContain" type="text"
                    placeholder="e.g., audiobook" />
                  <button type="button" @click="addMustContain" :disabled="!newMustContain.trim()" :aria-disabled="!newMustContain.trim()" class="btn icon-btn btn-primary btn-sm" title="Add required word" aria-label="Add required word">
                    <PhPlus />
                  </button>
                </div>
              </div>
            </div>

            <!-- Must Not Contain -->
            <div class="filter-group">
              <h4><PhX /> Must Not Contain (Forbidden)</h4>
              <p class="section-description">
                Releases containing any of these words will be rejected (case-insensitive).
              </p>
              <div class="tag-input-group">
                <div :class="['tags-list', { 'tags-list-empty': (formData.mustNotContain?.length || 0) === 0 }]">
                  <div v-for="(word, index) in formData.mustNotContain" :key="index" class="tag forbidden removable">
                    {{ word }}
                    <button type="button" @click="removeMustNotContain(index)" class="tag-remove">
                      <PhX />
                    </button>
                  </div>
                </div>
                <div class="tag-input">
                  <input v-model="newMustNotContain" @keypress.enter.prevent="addMustNotContain" type="text"
                    placeholder="e.g., abridged, radio" />
                  <button type="button" @click="addMustNotContain" :disabled="!newMustNotContain.trim()" :aria-disabled="!newMustNotContain.trim()" class="btn icon-btn btn-primary btn-sm" title="Add forbidden word" aria-label="Add forbidden word">
                    <PhPlus />
                  </button>
                </div>
              </div>
            </div>
          </FormSection>

          <!-- Language Preferences -->
          <FormSection title="Language Preferences" :icon="PhTranslate">
            <p class="section-description">Preferred languages in order of preference.</p>

            <div class="tag-input-group">
              <div :class="['tags-list', { 'tags-list-empty': (formData.preferredLanguages?.length || 0) === 0 }]">
                <div v-for="(lang, index) in formData.preferredLanguages" :key="index" class="tag removable">
                  {{ lang }}
                  <button type="button" @click="removeLanguage(index)" class="tag-remove">
                    <PhX />
                  </button>
                </div>
              </div>
              <div class="tag-input">
                <input v-model="newLanguage" @keypress.enter.prevent="addLanguage" type="text"
                  placeholder="e.g., English, Spanish" />
                <button type="button" @click="addLanguage" :disabled="!newLanguage.trim()" :aria-disabled="!newLanguage.trim()" class="btn icon-btn btn-primary btn-sm" title="Add language" aria-label="Add language">
                  <PhPlus />
                </button>
              </div>
            </div>
          </FormSection>

          <!-- Release Preferences -->
          <FormSection title="Release Preferences" :icon="PhClockCounterClockwise">
            <FormRow label="Minimum Seeders (Torrents)" labelFor="minimumSeeders">
              <input id="minimumSeeders" v-model.number="formData.minimumSeeders" type="number" min="0" placeholder="0 = no minimum" />
            </FormRow>

            <FormRow label="Minimum Score Threshold" labelFor="minimumScore">
              <input id="minimumScore" v-model.number="formData.minimumScore" type="number" min="0" max="100" placeholder="0 = allow any score" />
            </FormRow>

            <CheckboxCard v-model="formData.preferNewerReleases" title="Prefer newer releases" description="Give bonus points to more recent releases (torrent upload date)" />

            <FormRow v-if="formData.preferNewerReleases" label="Maximum Age (Days)" labelFor="maximumAge" help="Reject releases older than this many days (0 = no limit)">
              <input id="maximumAge" v-model.number="formData.maximumAge" type="number" min="0" placeholder="0 = no maximum" />
            </FormRow>
          </FormSection>
        </form>
      </ModalBody>
    </template>

    <template #footer>
      <ModalFooter
        :showCancel="true"
        :showSave="true"
        :saving="saving"
        :saveLabel="profile ? 'Save' : 'Create Profile'"
        @cancel="closeModal"
        @save="handleSubmit"
      />
    </template>
  </Modal>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { Modal, ModalHeader, ModalBody, ModalFooter } from '@/components/feedback'
import FormSection from './FormSection.vue'
import FormRow from '@/components/settings/FormRow.vue'
import CheckboxCard from '@/components/settings/CheckboxCard.vue'
import { PhX, PhStar, PhCheck, PhPlus, PhInfo, PhCheckSquare, PhFileAudio, PhRuler, PhSparkle, PhTextAa, PhTranslate, PhClockCounterClockwise } from '@phosphor-icons/vue' 
import type { QualityProfile } from '@/types'

const props = defineProps<{
  visible: boolean
  profile: QualityProfile | null
}>()

const emit = defineEmits<{
  close: []
  save: [profile: QualityProfile]
}>()

// Available quality options
const availableQualities = [
  'Unknown',
  'Low (64 kbps)',
  'Medium (128 kbps)',
  'High (192-256 kbps)',
  'Lossless (FLAC)',
]

// Form data
const formData = ref<QualityProfile>({
  name: '',
  description: '',
  qualities: [],
  cutoffQuality: '',
  minimumSize: undefined,
  maximumSize: undefined,
  preferredFormats: [],
  preferredWords: [],
  mustNotContain: [],
  mustContain: [],
  preferredLanguages: [],
  minimumSeeders: 0,
  isDefault: false,
  preferNewerReleases: false,
  maximumAge: 0,
})

// Tag input refs
const newFormat = ref('')
const newPreferredWord = ref('')
const newMustContain = ref('')
const newMustNotContain = ref('')
const newLanguage = ref('')

// Initialize form when profile changes
watch(
  () => props.profile,
  (newProfile) => {
    if (newProfile) {
      formData.value = JSON.parse(JSON.stringify(newProfile))
    } else {
      // Reset to defaults
      formData.value = {
        name: '',
        description: '',
        qualities: [],
        cutoffQuality: '',
        minimumSize: undefined,
        maximumSize: undefined,
        preferredFormats: [],
        preferredWords: [],
        mustNotContain: [],
        mustContain: [],
        preferredLanguages: [],
        minimumSeeders: 0,
        isDefault: false,
        preferNewerReleases: false,
        maximumAge: 0,
      }
    }
  },
  { immediate: true },
)

// Quality management
const isQualityAllowed = (quality: string): boolean => {
  return formData.value.qualities.some((q) => q.quality === quality && q.allowed)
}

const getQualityPriority = (quality: string): number => {
  const qual = formData.value.qualities.find((q) => q.quality === quality)
  return qual?.priority ?? 0
}

const toggleQuality = (quality: string, allowed: boolean) => {
  if (!formData.value.qualities) {
    formData.value.qualities = []
  }

  const existingIndex = formData.value.qualities.findIndex((q) => q.quality === quality)

  if (existingIndex !== -1) {
    const qualityDef = formData.value.qualities[existingIndex]
    if (qualityDef) {
      qualityDef.allowed = allowed
    }
  } else {
    formData.value.qualities.push({
      quality,
      allowed,
      priority: 50,
    })
  }

  // Clear cutoff if quality is disabled
  if (!allowed && formData.value.cutoffQuality === quality) {
    formData.value.cutoffQuality = ''
  }
}

const updateQualityPriority = (quality: string, event: Event) => {
  const target = event.target as HTMLInputElement
  const priority = parseInt(target.value)

  const qual = formData.value.qualities.find((q) => q.quality === quality)
  if (qual) {
    qual.priority = priority
  }
}

// Format management
const addFormat = () => {
  const format = newFormat.value.trim()
  if (format && !formData.value.preferredFormats?.includes(format)) {
    if (!formData.value.preferredFormats) {
      formData.value.preferredFormats = []
    }
    formData.value.preferredFormats.push(format)
    newFormat.value = ''
  }
}

const removeFormat = (index: number) => {
  formData.value.preferredFormats?.splice(index, 1)
}

// Preferred words management
const addPreferredWord = () => {
  const word = newPreferredWord.value.trim()
  if (word && !formData.value.preferredWords?.includes(word)) {
    if (!formData.value.preferredWords) {
      formData.value.preferredWords = []
    }
    formData.value.preferredWords.push(word)
    newPreferredWord.value = ''
  }
}

const removePreferredWord = (index: number) => {
  formData.value.preferredWords?.splice(index, 1)
}

// Must contain management
const addMustContain = () => {
  const word = newMustContain.value.trim()
  if (word && !formData.value.mustContain?.includes(word)) {
    if (!formData.value.mustContain) {
      formData.value.mustContain = []
    }
    formData.value.mustContain.push(word)
    newMustContain.value = ''
  }
}

const removeMustContain = (index: number) => {
  formData.value.mustContain?.splice(index, 1)
}

// Must not contain management
const addMustNotContain = () => {
  const word = newMustNotContain.value.trim()
  if (word && !formData.value.mustNotContain?.includes(word)) {
    if (!formData.value.mustNotContain) {
      formData.value.mustNotContain = []
    }
    formData.value.mustNotContain.push(word)
    newMustNotContain.value = ''
  }
}

const removeMustNotContain = (index: number) => {
  formData.value.mustNotContain?.splice(index, 1)
}

// Language management
const addLanguage = () => {
  const lang = newLanguage.value.trim()
  if (lang && !formData.value.preferredLanguages?.includes(lang)) {
    if (!formData.value.preferredLanguages) {
      formData.value.preferredLanguages = []
    }
    formData.value.preferredLanguages.push(lang)
    newLanguage.value = ''
  }
}

const removeLanguage = (index: number) => {
  formData.value.preferredLanguages?.splice(index, 1)
}

// Modal actions
const closeModal = () => {
  emit('close')
}

import { useToast } from '@/services/toastService'

const saving = ref(false)

const handleSubmit = () => {
  const toast = useToast()
  saving.value = true

  // Validate at least one quality is selected
  if (!formData.value.qualities.some((q) => q.allowed)) {
    toast.error('Validation', 'Please select at least one quality')
    saving.value = false
    return
  }

  // Validate cutoff quality is selected and allowed
  if (!formData.value.cutoffQuality) {
    toast.error('Validation', 'Please select a cutoff quality')
    saving.value = false
    return
  }

  if (
    !formData.value.qualities.some((q) => q.quality === formData.value.cutoffQuality && q.allowed)
  ) {
    toast.error('Validation', 'Cutoff quality must be one of the allowed qualities')
    saving.value = false
    return
  }

  emit('save', formData.value)
  saving.value = false
}
</script>

<style scoped>
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: rgba(0, 0, 0, 0.7);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  padding: 1rem;
}

.modal-content {
  background-color: #2a2a2a;
  border-radius: 6px;
  max-width: 800px;
  width: 100%;
  max-height: 90vh;
  display: flex;
  flex-direction: column;
  box-shadow: 0 4px 6px rgba(0, 0, 0, 0.3);
}

.quality-profile-modal {
  max-width: 900px;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1.5rem;
  border-bottom: 1px solid #444;
}

.modal-header h2 {
  margin: 0;
  color: #fff;
  font-size: 1.5rem;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.modal-body {
  padding: 1.5rem;
  overflow-y: auto;
  flex: 1;
}

.form-section h4 {
  margin: 1rem 0 0.5rem 0;
  color: #fff;
  font-size: 1rem;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.section-description {
  margin: 0.5rem 0 1rem 0;
  color: #999;
  font-size: 0.9rem;
  line-height: 1.4;
}

.form-group {
  margin-bottom: 1rem;
}

.form-group label {
  display: block;
  margin-bottom: 0.5rem;
  color: #ddd;
  font-weight: 500;
}

.form-group input[type='text'],
.form-group input[type='url'],
.form-group input[type='number'],
.form-group textarea,
.form-group select {
  width: 100%;
  padding: 0.75rem;
  background-color: #1a1a1a;
  border: 1px solid #444;
  border-radius: 6px;
  color: #fff;
  font-size: 1rem;
}

.form-group textarea {
  resize: vertical;
  font-family: inherit;
}

.form-row {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 1rem;
}

.checkbox-label {
  display: flex;
  align-items: center;
  gap: 1rem;
  color: #ddd;
  cursor: pointer;
  user-select: none;
  padding: 0.5rem 0;
  text-align: left;
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
  border-color: var(--brand-focus);
}

.checkbox-label input[type='checkbox']:checked {
  background-color: var(--brand-focus);
  border-color: var(--brand-focus);
} 

.checkbox-label input[type='checkbox']:checked::after {
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

.checkbox-label input[type='checkbox']:focus {
  outline: 2px solid rgba(var(--brand-rgb), 0.3);
  outline-offset: 2px;
}

.checkbox-label span {
  line-height: 1.4;
  font-size: 0.95rem;
  margin-left: 0.25rem;
}

.info-text {
  display: block;
  margin-top: 0.5rem;
  color: #999;
  font-size: 0.85rem;
  line-height: 1.4;
}

.info-text i {
  color: var(--brand-500);
}

/* Quality List */
.quality-list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  margin-bottom: 1rem;
}

.quality-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.75rem;
  background-color: #1a1a1a;
  border: 1px solid #444;
  border-radius: 6px;
}

.quality-name {
  flex: 1;
  color: #fff;
  font-weight: 500;
}

.quality-controls {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.priority-label {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  color: #ddd;
  font-size: 0.9rem;
}

.priority-input {
  width: 70px;
  padding: 0.4rem;
  background-color: #2a2a2a;
  border: 1px solid #555;
  border-radius: 6px;
  color: #fff;
}

.radio-label {
  display: flex;
  align-items: center;
  gap: 0.3rem;
  color: #ddd;
  font-size: 0.9rem;
  cursor: pointer;
}

.radio-label input[type='radio'] {
  cursor: pointer;
}

.cutoff-text {
  color: #ff9800;
}

/* Tag Input */
.tag-input-group {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.tags-list {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  min-height: 2rem;
}

/* Collapsed state when no tags are present */
.tags-list.tags-list-empty {
  min-height: 0;
  height: 0;
  padding: 0;
  margin: 0;
  overflow: hidden;
  transition: height 0.12s ease, opacity 0.12s ease;
  opacity: 0;
}

.tag {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.4rem 0.8rem;
  background-color: var(--brand-500);
  color: #fff;
  border-radius: var(--btn-radius);
  font-size: 0.9rem;
}

.tag.removable {
  padding-right: 0.4rem;
}

.tag.positive {
  background-color: #4caf50;
}

.tag.required {
  background-color: #ff9800;
}

.tag.forbidden {
  background-color: #f44336;
}

.tag-remove {
  background: none;
  border: none;
  color: #fff;
  cursor: pointer;
  padding: 0.2rem;
  display: flex;
  align-items: center;
  justify-content: center;
  opacity: 0.8;
  transition: opacity 0.2s;
}

.tag-remove:hover {
  opacity: 1;
}

.tag-input {
  display: flex;
  gap: 0.5rem;
}

.tag-input input {
  flex: 1;
  padding: 0.6rem;
  background-color: #1a1a1a;
  border: 1px solid #444;
  border-radius: 6px;
  color: #fff;
}

.add-button {
  padding: 0.6rem 1rem;
  background-color: var(--brand-500);
  color: #fff;
  border: none;
  border-radius: var(--btn-radius);
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 0.3rem;
  transition: background-color 0.2s;
}

.add-button:hover {
  background-color: var(--brand-600);
}

.filter-group {
  margin-bottom: 1.5rem;
}

.filter-group:last-child {
  margin-bottom: 0;
}

/* Form Actions */
.form-actions {
  display: flex;
  justify-content: flex-end;
  gap: 1rem;
  margin-top: 2rem;
  padding-top: 1.5rem;
  border-top: 1px solid #444;
}

/* Button styles for cancel/submit are centralized in src/assets/modals.css */
/* Modal-specific parts moved to shared modals.css */

.quality-profile-modal {
  max-width: 900px;
}

.modal-header h2 {
  margin: 0;
  color: #fff;
  font-size: 1.5rem;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.modal-body {
  padding: 1.5rem;
  overflow-y: auto;
  flex: 1;
}
</style>
