<template>
  <div class="root-folder-select">
    <label v-if="!hideLabel" class="form-label">Root Folder</label>

    <div v-if="store.loading" class="loading-row">
      <PhSpinner class="ph-spin" />
      <span>Loading root folders...</span>
    </div>

    <div v-else>
      <div :class="['root-select-content', props.inline ? 'inline' : '']">
        <select class="form-select" :value="selectValue" @change="onChange">
          <option :value="NULL_VALUE">Use default</option>
          <option v-for="f in store.folders" :key="f.id" :value="String(f.id)">
            {{ f.name }} — {{ f.path }}
          </option>
          <option :value="CUSTOM_VALUE">Custom path</option>
        </select>

        <div v-if="isCustom" class="custom-path" :class="{ 'inline-mode': props.inline }">
          <div class="custom-path-row">
            <input ref="localInput" type="text" class="form-input custom-input" placeholder="Absolute path (e.g. C:\Audiobooks)" v-model="localCustomPath" @input="onCustomInput" @keydown.enter.prevent="onEnterSave" />
            <button v-if="!hideBrowse" type="button" class="btn-browse" @click="$emit('open-browser')" title="Browse for folder" aria-label="Browse for folder">
              <PhFolder />
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useRootFoldersStore } from '@/stores/rootFolders'
import { PhSpinner, PhFolder, PhInfo } from '@phosphor-icons/vue'

const NULL_VALUE = '__null__'
const CUSTOM_VALUE = '__custom__'

const props = withDefaults(
  defineProps<{ rootId?: number | null; customPath?: string | null; hideLabel?: boolean; hideBrowse?: boolean; autoFocusCustom?: boolean; inline?: boolean }>(),
  { rootId: null, customPath: null, hideLabel: false, hideBrowse: false, autoFocusCustom: false, inline: false },
)



const emit = defineEmits(['update:rootId', 'update:customPath'])

const store = useRootFoldersStore()
const localCustomPath = ref(props.customPath ?? '')
const localInput = ref<HTMLInputElement | null>(null)

function onEnterSave() {
  if (localCustomPath.value && localCustomPath.value.trim().length) {
    emit('update:customPath', localCustomPath.value.trim())
  }
}

onMounted(() => {
  // Ensure root folders are loaded for selection
  void store.load()
})

watch(
  () => props.customPath,
  (v) => {
    localCustomPath.value = v ?? ''
  },
)

const isCustom = computed(() => {
  return (props.customPath && props.customPath.length > 0) || selectValueComputed.value === CUSTOM_VALUE
})

const selectValueComputed = computed(() => {
  if (props.customPath && props.customPath.length > 0) return CUSTOM_VALUE
  // Treat explicit 0 as Custom sentinel
  if (props.rootId === 0) return CUSTOM_VALUE
  if (props.rootId == null) return NULL_VALUE
  return String(props.rootId)
})

// When the selection switches to Custom, optionally autofocus the custom input
watch(
  () => selectValueComputed.value,
  (v) => {
    if (v === CUSTOM_VALUE && props.autoFocusCustom) {
      // focus after DOM updates
      setTimeout(() => {
        localInput.value?.focus()
      }, 0)
    }
  },
)

const selectValue = selectValueComputed

function onChange(e: Event) {
  const v = (e.target as HTMLSelectElement).value
  if (v === CUSTOM_VALUE) {
    // Switch to custom path mode. Use 0 to explicitly denote 'custom' in parent.
    emit('update:rootId', 0)
    emit('update:customPath', localCustomPath.value || '')
  } else if (v === NULL_VALUE) {
    emit('update:rootId', null)
    emit('update:customPath', null)
  } else {
    const id = Number(v)
    if (!Number.isNaN(id)) {
      emit('update:rootId', id)
      emit('update:customPath', null)
    }
  }
}

function onCustomInput() {
  // ensure parent knows we're in custom mode when typing
  emit('update:rootId', 0)
  emit('update:customPath', localCustomPath.value || null)
}
</script>

<style scoped>
.root-folder-select {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}
.root-select-content.inline { display:flex; gap:0.5rem; align-items:center; flex-wrap:wrap }

.custom-path { 
  margin-top: 0.5rem;
}

.custom-path.inline-mode { margin-top: 0 }
.custom-path.inline-mode .custom-path-row { width:100%; display:flex; gap:0.5rem; align-items:center }
.custom-path-row { display:flex; gap:0.5rem; align-items:center }
.btn-browse { padding:0.45rem 0.8rem; background:#2196f3; color:#fff; border:none; border-radius:6px; display:inline-flex; align-items:center; justify-content:center; border: 1px solid rgba(0,0,0,0.15) }
.btn-browse:hover { background:#1976d2 }
.btn-browse svg { width:18px; height:18px }

.custom-input { min-width: 160px; flex: 1 }

/* Auto-focus visual hint */
.custom-path-row .form-input:focus { border-color: #007acc; box-shadow: 0 0 0 3px rgba(0,122,204,0.12) }

.form-select {
  padding: 0.75rem 1rem;
  height: 40px;
  box-sizing: border-box;
  background-color: #1a1a1a; /* match input background */
  border: 1px solid #333;
  border-radius: 6px;
  color: white;
  font-size: 0.95rem;
  cursor: pointer;
  transition: all 0.2s;
}

.form-select:focus {
  outline: none;
  border-color: #007acc;
  box-shadow: 0 0 0 3px rgba(0, 122, 204, 0.2);
} 

.form-input {
  padding: 0.6rem 0.75rem;
  background-color: #1a1a1a;
  border: 1px solid #333;
  color: white;
  border-radius: 6px;
}

.loading-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  color: #adb5bd;
}
</style>