<template>
  <div class="form-section">
    <h3>
      <PhGlobe /> External Requests / US Proxy
      <button type="button" class="info-inline" @click.prevent="openProxySecurityModal" title="Security recommendations">
        <PhInfo />
      </button>
    </h3>

    <div class="form-body">
      <CheckboxCard :modelValue="settings.preferUsDomain" @update:modelValue="v => updateField('preferUsDomain', v)" title="Prefer US (.com) domain for Audible/Amazon" description="When enabled, the server will attempt a retry using the US (.com) domain if a localized or redirect page is detected." />
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import type { ApplicationSettings } from '@/types'
import { PhGlobe, PhInfo } from '@phosphor-icons/vue'
import CheckboxCard from '@/components/settings/CheckboxCard.vue'

const props = defineProps<{ settings: Partial<ApplicationSettings> }>()
const emit = defineEmits<{
  'update:settings': [value: Partial<ApplicationSettings>]
}>()

const showProxySecurityModal = ref(false)
function openProxySecurityModal() {
  showProxySecurityModal.value = true
}
function updateField(field: keyof ApplicationSettings, value: unknown) {
  const payload = { ...(props.settings || {}), [field]: value } as Partial<ApplicationSettings>
  emit('update:settings', payload)
}
</script>

<style scoped>
/* Modal-like External Requests section */
.form-body { padding: 1.25rem; border-radius: 6px; border: 1px solid #333; box-shadow: 0 4px 14px rgba(0,0,0,0.6); background-color: #232323; }

.info-inline { background: none; border: none; color: #4dabf7; cursor: pointer; padding: 0.25rem; display: inline-flex; align-items: center; border-radius: 4px; transition: all 0.2s; font-size: 1rem; }
.info-inline:hover { background: rgba(33,150,243,0.1); }

.form-group input[type='text'], .form-group input[type='number'] { width:100%; padding:0.9rem 0.85rem; border: 1px solid #444; border-radius:6px; background-color:#1a1a1a; color:#fff }

.form-group input:focus { outline:none; border-color:var(--brand-500); box-shadow:0 0 0 3px rgba(77,171,247,0.08); }

.form-help { display:block; margin-top:0.5rem; font-size:0.85rem; color:#9aa3ad }
</style>