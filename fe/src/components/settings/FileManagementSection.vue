<template>
  <div class="form-section">
    <h3>
      <PhFolder /> File Management
    </h3>
    <div class="form-body">
      <FormRow label="File Naming Pattern" help="Pattern for organizing audiobook files. Available variables: {Author}, {Series}, {Title}, {SeriesNumber}, {DiskNumber}, {ChapterNumber}, {Year}, {Quality}">
        <input :value="settings.fileNamingPattern" @input="e => updateField('fileNamingPattern', (e.target as HTMLInputElement).value)" type="text" placeholder="{Author}/{Series}/{Title}" />
        <div class="form-help mt-sm">Pattern for organizing audiobook files. Available variables:<br />
          <code>{Author}</code> - Author/narrator name<br />
          <code>{Series}</code> - Series name<br />
          <code>{Title}</code> - Book title<br />
          <code>{SeriesNumber}</code> - Position in series<br />
          <code>{DiskNumber}</code> or <code>{DiskNumber:00}</code> - Disk/part number (00 = zero-padded)<br />
          <code>{ChapterNumber}</code> or <code>{ChapterNumber:00}</code> - Chapter number (00 = zero-padded)<br />
          <code>{Year}</code> - Publication year<br />
          <code>{Quality}</code> - Audio quality
        </div>
      </FormRow>

      <FormRow label="Completed File Action" help="Choose whether completed downloads should be moved into the library output path or copied and left in the client's folder.">
        <select :value="settings.completedFileAction" @change="e => updateField('completedFileAction', (e.target as HTMLSelectElement).value)">
          <option value="Move">Move (default)</option>
          <option value="Copy">Copy</option>
        </select>
      </FormRow>
    </div>
  </div>
</template>

<script setup lang="ts">
import type { ApplicationSettings } from '@/types'
import { PhFolder } from '@phosphor-icons/vue'
import FormRow from '@/components/settings/FormRow.vue'

const props = defineProps<{ settings: Partial<ApplicationSettings> }>()
const emit = defineEmits<{
  'update:settings': [value: Partial<ApplicationSettings>]
}>()

function updateField(field: keyof ApplicationSettings, value: unknown) {
  const payload = { ...(props.settings || {}), [field]: value } as Partial<ApplicationSettings>
  emit('update:settings', payload)
}
</script>

<style scoped>
h3 {
  margin: 0 0 1.5rem 0;
  padding: 0;
  font-size: 1.1rem;
  font-weight: 500;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  color: #fff;
}

/* Modal-like card and local form styles for File Management section */
.form-body { padding: 1.25rem; border-radius: 6px; border: 1px solid #333; box-shadow: 0 4px 14px rgba(0,0,0,0.6); background-color: #232323; }

.form-group { margin-bottom: 1.25rem }
.form-group:last-child { margin-bottom: 0 }

.form-group label { margin-bottom: 0.5rem; font-weight: 500; color: #fff }

.form-group input[type='text'], .form-group select {
  width: 100%; padding: 0.9rem 0.85rem; border: 1px solid #444; border-radius: 6px; background-color: #1a1a1a; color: #fff; font-size: 0.95rem; transition: all 0.12s;
}
.form-group input::placeholder { color: #6c757d }

.form-group input:focus, .form-group select:focus {
  outline: none; border-color: var(--brand-500); box-shadow: 0 0 0 3px rgba(77,171,247,0.1);
}

.form-help { display: block; margin-top: 0.5rem; font-size: 0.85rem; color: #adb5bd; line-height: 1.5 }

.form-error { color: #f44336; font-size: 0.85rem; margin-top: 0.5rem }
</style>