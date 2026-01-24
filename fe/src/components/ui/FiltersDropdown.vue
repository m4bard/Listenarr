<template>
  <div ref="root" class="filters-dropdown">
    <button class="trigger" @click="toggle" :aria-expanded="open">
      <span style="display:inline-flex; align-items:center; gap:8px">
        <span class="icon">Filters</span>
      </span>
      <span class="caret">▾</span>
    </button>

    <div v-if="open" class="dropdown">
      <div v-for="o in builtInOptions" :key="o.value" class="dropdown-item" @click="selectBuiltIn(o.value)">
        <div class="dropdown-item-main">
          <span>{{ o.label }}</span>
        </div>
        <div v-if="selectedBuiltIn === o.value" class="check">✓</div>
      </div>

      <div class="dropdown-divider"></div>

      <div v-if="customFilters.length === 0" class="dropdown-item">No custom filters</div>
      <div v-for="f in customFilters" :key="f.id" class="dropdown-item">
        <div class="dropdown-item-main" @click="selectCustom(f.id)">{{ f.label }}</div>
        <div class="dropdown-item-actions">
          <button class="icon-btn" @click.stop="emitEdit(f)">Edit</button>
          <button class="icon-btn delete" @click.stop="emitDelete(f)">Delete</button>
        </div>
      </div>

      <div class="dropdown-divider"></div>
      <div class="dropdown-item create" @click="emitCreate">Create filter</div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
interface CustomFilterRule {
  field: string
  operator: string
  value: string
}
interface CustomFilter {
  id: string
  label: string
  rules: CustomFilterRule[]
}

const props = withDefaults(
  defineProps<{
    customFilters: CustomFilter[]
    modelValue?: string | null
  }>(),
  {
    modelValue: null,
  },
)

const emit = defineEmits<{
  (e: 'update:modelValue', v: string | null): void
  (e: 'create'): void
  (e: 'edit', filter: CustomFilter): void
  (e: 'delete', filter: CustomFilter): void
}>()

const open = ref(false)
const root = ref<HTMLElement | null>(null)

const builtInOptions = [
  { value: 'all', label: 'All' },
  { value: 'monitored', label: 'Monitored Only' },
  { value: 'unmonitored', label: 'Unmonitored Only' },
  { value: 'missing', label: 'Missing' },
  { value: 'recent', label: 'Recently Added' },
]

const customFilters = computed(() => props.customFilters || [])

const selectedBuiltIn = computed(() => {
  const v = props.modelValue
  if (!v) return null
  if (builtInOptions.some((o) => o.value === v)) return v
  return null
})

const selectedCustom = computed(() => {
  const v = props.modelValue
  if (!v) return null
  if (customFilters.value.some((f: CustomFilter) => f.id === v)) return v
  return null
})

function toggle() {
  open.value = !open.value
}

function close() {
  open.value = false
}

function selectBuiltIn(val: string) {
  emit('update:modelValue', val)
  close()
}

function selectCustom(id: string) {
  emit('update:modelValue', id)
  close()
}

function emitCreate() {
  emit('create')
  close()
}

function emitEdit(f: CustomFilter) {
  emit('edit', f)
  close()
}

function emitDelete(f: CustomFilter) {
  emit('delete', f)
  close()
}

function handleClickOutside(e: MouseEvent) {
  if (!root.value) return
  if (!root.value.contains(e.target as Node)) close()
}

onMounted(() => document.addEventListener('click', handleClickOutside))
onUnmounted(() => document.removeEventListener('click', handleClickOutside))
</script>
<style scoped>
.filters-dropdown {
  position: relative;
  display: inline-block;
}
.trigger {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 6px 8px;
  background: #2a2a2a;
  border: 1px solid rgba(255, 255, 255, 0.06);
  border-radius: 6px;
  color: #e6eef8;
  cursor: pointer;
  font-size: 12px;
}
.dropdown {
  position: absolute;
  top: calc(100% + 6px);
  min-width: 220px;
  background: #2a2a2a;
  border: 1px solid rgba(255, 255, 255, 0.06);
  border-radius: 6px;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.6);
  z-index: 1100;
}

.dropdown-item {
  padding: 0.75rem 1rem;
  cursor: pointer;
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 12px;
}
.dropdown-item:hover {
  background: rgba(255, 255, 255, 0.03);
}
.dropdown-divider {
  height: 1px;
  background: rgba(255, 255, 255, 0.04);
  margin: 6px 0;
}
.dropdown-item.create {
  font-weight: 600;
  color: #fff;
}
.check {
  color: #4dabf7;
}
.dropdown-item-main {
  display: flex;
  align-items: center;
  gap: 8px;
  flex: 1;
}
.dropdown-item-actions {
  display: flex;
  gap: 6px;
  margin-left: 8px;
}
.icon-btn {
  background: transparent;
  border: 1px solid rgba(255, 255, 255, 0.04);
  color: #ddd;
  padding: 4px 6px;
  border-radius: 6px;
  cursor: pointer;
}
.icon-btn.delete {
  background: rgba(231, 76, 60, 0.9);
  border-color: rgba(192, 57, 43, 0.5);
}
.icon-btn.delete:hover {
  background: rgba(192, 57, 43, 1);
}

/* Mobile-friendly toolbar: hide text, show only icons on screens 1024px and below */
@media (max-width: 1024px) {
  .trigger {
    padding: 10px 6px;
    min-width: 36px;
    justify-content: center;
    gap: unset;
  }
  .trigger span {
    display: none;
  }
}
</style>
