<template>
  <Modal :visible="visible" size="lg" @close="closeModal">
    <template #header>
      <ModalHeader :title="'Audiobook Details'" @close="closeModal" />
    </template>

    <template #default>
      <ModalBody>
        <div class="book-layout">
          <!-- Book Image -->
          <div class="book-image">
            <img
              v-if="book.imageUrl"
              :src="apiService.getImageUrl(book.imageUrl)"
              :alt="book.title"
              loading="lazy"
              @error="handleImageError"
            />
            <div v-else class="placeholder-cover">
              <PhImage />
              <span>No Cover</span>
            </div>
          </div>

          <!-- Book Details -->
          <div class="book-details">
            <div class="detail-section">
              <h3>
                {{ book.title }}
                <span v-if="assignedProfileName" class="profile-badge">{{
                  assignedProfileName
                }}</span>
              </h3>
              <p v-if="book.authors?.length" class="authors">by {{ book.authors.join(', ') }}</p>
              <p v-if="book.narrators?.length" class="narrators">
                Narrated by {{ book.narrators.join(', ') }}
              </p>
            </div>

            <div v-if="book.description" class="detail-section">
              <h4>Description</h4>
              <div class="description" v-html="book.description"></div>
            </div>

            <div class="detail-section">
              <h4>Publication Information</h4>
              <div class="detail-grid">
                <div v-if="book.publisher" class="detail-item">
                  <span class="label">Publisher:</span>
                  <span class="value">{{ book.publisher }}</span>
                </div>
                <div v-if="book.publishYear" class="detail-item">
                  <span class="label">Published:</span>
                  <span class="value">{{ book.publishYear }}</span>
                </div>
                <div v-if="book.language" class="detail-item">
                  <span class="label">Language:</span>
                  <span class="value">{{ capitalizeFirst(book.language) }}</span>
                </div>
                <div v-if="book.runtime" class="detail-item">
                  <span class="label">Listening Length:</span>
                  <span class="value">{{ formatRuntime(book.runtime) }}</span>
                </div>
                <div v-if="book.version" class="detail-item">
                  <span class="label">Version:</span>
                  <span class="value">{{ book.version }}</span>
                </div>
              </div>
            </div>

            <div class="detail-section">
              <h4>Identifiers</h4>
              <div class="detail-grid">
                <div v-if="book.source" class="detail-item">
                  <span class="label">Metadata Source:</span>
                  <span class="value">{{ book.source }}</span>
                </div>
                <div v-if="book.asin" class="detail-item">
                  <span class="label">ASIN:</span>
                  <span class="value">{{ book.asin }}</span>
                </div>
                <div v-if="book.isbn" class="detail-item">
                  <span class="label">ISBN:</span>
                  <span class="value">{{ book.isbn }}</span>
                </div>
                <div v-if="book.openLibraryId" class="detail-item">
                  <span class="label">OpenLibrary ID:</span>
                  <span class="value">
                    <a
                      v-if="book.source === 'OpenLibrary'"
                      :href="`https://openlibrary.org/books/${book.openLibraryId}`"
                      target="_blank"
                      rel="noopener noreferrer"
                    >
                      {{ book.openLibraryId }}
                    </a>
                    <span v-else>{{ book.openLibraryId }}</span>
                  </span>
                </div>
              </div>
            </div>

            <div v-if="book.series || book.genres?.length" class="detail-section">
              <h4>Series & Genre Information</h4>
              <div class="detail-grid">
                <div v-if="book.series" class="detail-item">
                  <span class="label">Series:</span>
                  <span class="value">{{ book.series }}</span>
                </div>
                <div v-if="book.genres?.length" class="detail-item">
                  <span class="label">Genres:</span>
                  <span class="value">{{ book.genres.join(', ') }}</span>
                </div>
              </div>
            </div>

            <div v-if="hasFlags" class="detail-section">
              <h4>Content Flags</h4>
              <div class="flags">
                <span v-if="book.explicit" class="flag explicit">Explicit</span>
                <span v-if="book.abridged" class="flag abridged">Abridged</span>
              </div>
            </div>

            <div class="detail-section">
              <h4><PhStar /> Quality Profile</h4>
              <div class="quality-profile-selector">
                <select v-model="selectedQualityProfileId" class="profile-select">
                  <option :value="null">Use Default Profile</option>
                  <option v-for="profile in qualityProfiles" :key="profile.id" :value="profile.id">
                    {{ profile.name }}{{ profile.isDefault ? ' (Default)' : '' }}
                  </option>
                </select>
                <small class="profile-help">
                  Select the quality profile to use for this audiobook. The quality profile
                  determines which releases to download and prefer.
                </small>
              </div>
            </div>
          </div>
        </div>
      </ModalBody>
    </template>

    <template #footer>
      <button class="btn btn-secondary" @click="closeModal">
        <PhX />
        Close
      </button>
      <button
        :class="['btn', isAdded ? 'btn-success' : 'btn-primary']"
        @click="addToLibrary"
        :disabled="isAdded"
      >
        <component :is="isAdded ? PhCheck : PhPlus" />
        {{ isAdded ? 'Added' : 'Add to Library' }}
      </button>
    </template>
  </Modal>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import type { AudibleBookMetadata, QualityProfile } from '@/types'
import { getQualityProfiles, apiService } from '@/services/api'
import { handleImageError } from '@/utils/imageFallback'
import { useLibraryStore } from '@/stores/library'
import { PhX, PhImage, PhStar, PhCheck, PhPlus } from '@phosphor-icons/vue'
import { Modal, ModalBody } from '@/components/modal'

interface Props {
  visible: boolean
  book: AudibleBookMetadata
}

interface Emits {
  (e: 'close'): void
  (e: 'add-to-library', book: AudibleBookMetadata, qualityProfileId?: number): void
}

const props = defineProps<Props>()
const emit = defineEmits<Emits>()

const qualityProfiles = ref<QualityProfile[]>([])
const selectedQualityProfileId = ref<number | null>(null)
const libraryStore = useLibraryStore()

const isAdded = computed(() => {
  try {
    const asins = new Set(
      libraryStore.audiobooks.map((b) => b.asin).filter((x): x is string => !!x),
    )
    const olids = new Set(
      libraryStore.audiobooks.map((b) => b.openLibraryId).filter((x): x is string => !!x),
    )
    if (props.book?.asin && asins.has(props.book.asin)) return true
    if (props.book?.openLibraryId && olids.has(props.book.openLibraryId)) return true
    return false
  } catch {
    return false
  }
})

const assignedProfileName = computed(() => {
  const id = props.book?.qualityProfileId
  if (!id) return null
  const p = qualityProfiles.value.find((q) => q.id === id)
  return p ? p.name : 'Unknown'
})

const hasFlags = computed(() => {
  return props.book.explicit || props.book.abridged
})

const closeModal = () => {
  emit('close')
}

const addToLibrary = () => {
  emit('add-to-library', props.book, selectedQualityProfileId.value || undefined)
  closeModal()
}

const loadQualityProfiles = async () => {
  try {
    qualityProfiles.value = await getQualityProfiles()
    // Select the default profile
    const defaultProfile = qualityProfiles.value.find((p) => p.isDefault)
    if (defaultProfile) {
      selectedQualityProfileId.value = defaultProfile.id || null
    }
  } catch (error) {
    console.error('Failed to load quality profiles:', error)
  }
}

watch(
  () => props.visible,
  async (val) => {
    if (val) {
      await loadQualityProfiles()
      // If the audiobook has an assigned profile, reflect it in the selector
      selectedQualityProfileId.value = props.book?.qualityProfileId ?? null
    }
  },
)

const formatRuntime = (minutes: number): string => {
  if (!minutes) return 'Unknown'
  const hours = Math.floor(minutes / 60)
  const mins = minutes % 60
  return `${hours}h ${mins}m`
}

const capitalizeFirst = (str: string): string => {
  if (!str) return ''
  return str.charAt(0).toUpperCase() + str.slice(1).toLowerCase()
}
</script>

<style scoped>
/* Keep only layout and content-related styles; modal wrapper styles come from shared modal stylesheet */

.book-layout {
  display: grid;
  grid-template-columns: 200px 1fr;
  gap: 2rem;
  align-items: start;
}

.book-image {
  position: sticky;
  top: 0;
}

.book-image img {
  width: 100%;
  height: auto;
  border-radius: 6px;
  box-shadow: 0 4px 8px rgba(0, 0, 0, 0.3);
}

.placeholder-cover {
  width: 100%;
  aspect-ratio: 2/3;
  background-color: #333;
  border-radius: 6px;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  color: #666;
  text-align: center;
  padding: 1rem;
}

/* Destination input styling */
.destination-display {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.destination-readonly {
  display: flex;
  gap: 0.75rem;
  align-items: center;
}

.destination-readonly .readonly-input {
  flex: 1 1 auto;
  min-width: 0;
  padding: 0.6rem 0.75rem;
  background: #2a2a2a;
  border: 1px solid #444;
  border-radius: 6px;
  color: #eef2f8;
  font-size: 0.95rem;
  box-shadow: none;
}

.destination-readonly .readonly-input:focus,
.destination-readonly .readonly-input:active {
  outline: none;
  box-shadow: none;
  border-color: #444;
}

.destination-edit .destination-row {
  display: flex;
  gap: 0.75rem;
  align-items: center;
}

.destination-row .root-select {
  min-width: 200px;
  max-width: 260px;
  width: 100%;
}

.destination-row .relative-input {
  flex: 1 1 auto;
  min-width: 0;
  padding: 0.5rem 0.75rem;
  background: #1a1a1a;
  border: 1px solid #444;
  border-radius: 6px;
  color: #fff;
  font-size: 0.95rem;
}

.destination-row .relative-input:focus {
  outline: none;
  border-color: #2196f3;
  box-shadow: 0 0 0 3px rgba(33,150,243,0.06);
}

.destination-actions { display:flex; gap:0.5rem; }

@media (max-width: 720px) {
  .destination-row { flex-direction: column; align-items: stretch; }
  .destination-row .root-select { max-width: 100%; }
}

.placeholder-cover i {
  font-size: 3rem;
  margin-bottom: 0.5rem;
}

.placeholder-cover span {
  font-size: 0.9rem;
}

.book-details {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.detail-section h3 {
  margin: 0 0 0.5rem 0;
  color: white;
  font-size: 1.75rem;
  line-height: 1.2;
}

.authors {
  color: var(--brand-500);
  font-size: 1.1rem;
  font-weight: 500;
  margin: 0 0 0.25rem 0;
}

.narrators {
  color: #ccc;
  font-style: italic;
  margin: 0;
}

.detail-section h4 {
  margin: 0 0 1rem 0;
  color: white;
  font-size: 1.1rem;
  font-weight: 600;
  border-bottom: 1px solid #333;
  padding-bottom: 0.5rem;
}

.description {
  color: #ccc;
  line-height: 1.6;
  margin: 0;
}

.detail-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 1rem;
}

.detail-item {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.detail-item .label {
  color: #999;
  font-size: 0.9rem;
  font-weight: 500;
}

.detail-item .value {
  color: white;
  font-weight: 400;
}

.flags {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.flag {
  padding: 0.25rem 0.75rem;
  border-radius: 6px;
  font-size: 0.8rem;
  font-weight: 500;
}

.flag.explicit {
  background-color: rgba(231, 76, 60, 0.2);
  color: #e74c3c;
  border: 1px solid #e74c3c;
}

.flag.abridged {
  background-color: rgba(243, 156, 18, 0.2);
  color: #f39c12;
  border: 1px solid #f39c12;
}

/* Base modal-footer styles are centralized in src/assets/modals.css. Keep only this modal's responsive/footer-specific overrides. */
.modal-footer { display:flex; gap:0.75rem; justify-content:flex-end }

/* Buttons are centralized in `src/assets/buttons.css`. Use `.btn` and `.btn-primary` if needed. */

.btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.btn-primary:hover:not(:disabled) {
  background-color: var(--brand-700);
}

/* Button color variants centralized in `src/assets/modals.css` */

/* Responsive design */
@media (max-width: 768px) {
  .modal-content {
    margin: 0.5rem;
    max-height: 95vh;
  }

  .book-layout {
    grid-template-columns: 1fr;
    gap: 1.5rem;
  }

  .book-image {
    position: static;
    max-width: 200px;
    margin: 0 auto;
  }

  .detail-grid {
    grid-template-columns: 1fr;
  }

  .modal-footer {
    flex-direction: column-reverse;
  }

  .btn {
    justify-content: center;
  }
}

@media (max-width: 480px) {
  .modal-overlay {
    padding: 0.5rem;
  }

  .modal-header,
  .modal-body,
  .modal-footer {
    padding: 1rem;
  }

  .modal-header h2 {
    font-size: 1.25rem;
  }

  .detail-section h3 {
    font-size: 1.5rem;
  }
}

/* Quality Profile Selector */
.quality-profile-selector {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.profile-select {
  width: 100%;
  padding: 0.75rem;
  background-color: #1a1a1a;
  border: 1px solid #444;
  border-radius: 6px;
  color: #fff;
  font-size: 1rem;
  cursor: pointer;
  transition: border-color 0.2s;
}

.profile-select:hover {
  border-color: var(--brand-500);
}

.profile-select:focus {
  outline: none;
  border-color: var(--brand-focus);
  box-shadow: 0 0 0 2px rgba(var(--brand-rgb), 0.2);
}

.profile-help {
  color: #888;
  font-size: 0.875rem;
  line-height: 1.4;
}

/* Metadata Source Badge in Modal */
.metadata-source-item .metadata-source-badge {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  background: linear-gradient(135deg, var(--brand-500) 0%, var(--brand-600) 100%);
  color: white;
  font-weight: 500;
  padding: 0.375rem 0.75rem;
  border-radius: 6px;
  box-shadow: 0 2px 4px rgba(var(--brand-rgb), 0.2);
}

.detail-section h4 i {
  color: var(--brand-500);
  margin-right: 0.5rem;
}
</style>
