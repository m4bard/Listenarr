<template>
  <div class="root-folder-select">
    <label class="form-label">Root Folder</label>

    <div v-if="store.loading" class="loading-row">
      <PhSpinner class="ph-spin" />
      <span>Loading root folders...</span>
    </div>

    <div v-else>
      <select class="form-select" :value="selectValue" @change="onChange">
        <option :value="NULL_VALUE">Use default</option>
        <option v-for="f in store.folders" :key="f.id" :value="String(f.id)">
          {{ f.name }} — {{ f.path }}
        </option>
        <option :value="CUSTOM_VALUE">Custom path</option>
      </select>

      <div v-if="isCustom" class="custom-path">
        <input type="text" class="form-input" placeholder="/path/to/folder" v-model="localCustomPath" @input="onCustomInput" />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { useRootFoldersStore } from '@/stores/rootFolders'
import { PhSpinner } from '@phosphor-icons/vue'

const NULL_VALUE = '__null__'
const CUSTOM_VALUE = '__custom__'

const props = withDefaults(
  defineProps<{ rootId?: number | null; customPath?: string | null }>(),
  { rootId: null, customPath: null },
)

const emit = defineEmits(['update:rootId', 'update:customPath'])

const store = useRootFoldersStore()
const localCustomPath = ref(props.customPath ?? '')

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
  if (props.rootId == null) return NULL_VALUE
  return String(props.rootId)
})

const selectValue = selectValueComputed

function onChange(e: Event) {
  const v = (e.target as HTMLSelectElement).value
  if (v === CUSTOM_VALUE) {
    // Switch to custom path mode
    emit('update:rootId', null)
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
  // ensure we're in custom mode
  emit('update:rootId', null)
  emit('update:customPath', localCustomPath.value || null)
}
</script>

<style scoped>
.root-folder-select {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}
.custom-path {
  margin-top: 0.5rem;
}

.form-select {
  padding: 0.75rem 1rem;
  background-color: #2a2a2a;
  border: 1px solid #3a3a3a;
  border-radius: 6px;
  color: white;
  font-size: 0.95rem;
  cursor: pointer;
  transition: all 0.2s;
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
