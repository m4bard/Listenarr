<template>
  <div class="custom-select" ref="root">
    <button
      type="button"
      class="select-trigger"
      @click="toggle"
      :aria-expanded="open ? 'true' : 'false'"
      :aria-haspopup="'listbox'"
    >
      <span class="label">{{ selectedLabel }}</span>
      <i class="caret">▾</i>
    </button>

    <ul v-if="open" class="select-dropdown" role="listbox">
      <li
        v-for="opt in options"
        :key="opt.value"
        class="select-item"
        role="option"
        @click="select(opt.value)"
      >
        <span class="item-label">{{ opt.label }}</span>
      </li>
    </ul>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'

const props = withDefaults(
  defineProps<{
    modelValue?: string | null
    options?: Array<{ value: string; label: string; icon?: any }>
  }>(),
  { modelValue: null, options: () => [] },
)

const emit = defineEmits<{ (e: 'update:modelValue', v: string | null): void }>()

const open = ref(false)
const root = ref<HTMLElement | null>(null)

const options = computed(() => props.options || [])

const selectedLabel = computed(() => {
  const found = options.value.find((o) => o.value === props.modelValue)
  return found ? found.label : options.value[0]?.label ?? ''
})

function toggle() {
  open.value = !open.value
}

function close() {
  open.value = false
}

function select(v: string) {
  emit('update:modelValue', v)
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
.custom-select { position: relative; display: inline-block }
.select-trigger {
  background: #2a2a2a;
  color: #e6eef8;
  border: 1px solid rgba(255,255,255,0.06);
  padding: 8px 10px;
  border-radius: 6px;
  display: inline-flex;
  align-items: center;
  gap: 8px;
}
.select-dropdown {
  position: absolute;
  top: calc(100% + 6px);
  left: 0;
  min-width: 160px;
  background: #2a2a2a;
  border: 1px solid rgba(255,255,255,0.06);
  border-radius: 6px;
  box-shadow: 0 8px 24px rgba(0,0,0,0.6);
  z-index: 1100;
  list-style: none;
  padding: 6px 0;
  margin: 0;
}
.select-item { padding: 8px 12px; cursor: pointer; color: #ddd }
.select-item:hover { background: rgba(255,255,255,0.02); color: #fff }
.caret { margin-left: auto }
</style>