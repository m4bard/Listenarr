<!--
  Listenarr - Audiobook Management System
  Copyright (C) 2024-2026 Listenarr Contributors

  This program is free software: you can redistribute it and/or modify
  it under the terms of the GNU Affero General Public License as published
  by the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  This program is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
  GNU Affero General Public License for more details.

  You should have received a copy of the GNU Affero General Public License
  along with this program. If not, see <https://www.gnu.org/licenses/>.
-->
<template>
  <label class="queue-select" :title="label">
    <input
      ref="box"
      type="checkbox"
      :checked="checked"
      :disabled="disabled"
      :aria-label="label"
      :data-test="dataTest"
      @change="onChange"
    />
  </label>
</template>

<script setup lang="ts">
import { nextTick, onMounted, ref, watch } from 'vue'

const props = withDefaults(
  defineProps<{
    checked?: boolean
    indeterminate?: boolean
    disabled?: boolean
    label: string
    dataTest?: string
  }>(),
  { checked: false, indeterminate: false, disabled: false, dataTest: undefined },
)

const emit = defineEmits<{ (e: 'change', checked: boolean): void }>()

const box = ref<HTMLInputElement | null>(null)

// indeterminate is a DOM property, not an attribute, so it has to be written to the element.
const applyIndeterminate = () => {
  if (box.value) box.value.indeterminate = props.indeterminate && !props.checked
}

onMounted(applyIndeterminate)
watch(() => [props.indeterminate, props.checked], applyIndeterminate)

// The input is a controlled checkbox (:checked="checked"), but Vue only patches a dynamic
// prop binding when its value changes. If a click's emitted change does not alter the parent's
// checked prop (for example, selecting all of an empty list), the browser's own click-driven
// mutation is left standing. Restore the element from props on the next tick so it always
// reflects the prop, not whatever the last click happened to do to the DOM.
const onChange = (event: Event) => {
  const el = event.target as HTMLInputElement
  emit('change', el.checked)
  nextTick(() => {
    if (box.value) box.value.checked = props.checked
    applyIndeterminate()
  })
}
</script>

<style scoped>
.queue-select {
  display: inline-flex;
  align-items: center;
  margin-right: 0.5rem;
  cursor: pointer;
}

.queue-select input {
  cursor: pointer;
}
</style>
