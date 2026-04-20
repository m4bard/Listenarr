/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */
<template>
  <div class="form-group">
    <label v-if="label" :for="id" class="form-label">
      <span class="label-content">{{ label }}</span>
      <span v-if="required" class="required-indicator" aria-label="required">*</span>
    </label>
    <slot />
    <small v-if="help" class="help-text">{{ help }}</small>
    <small v-if="error" class="error-text">{{ error }}</small>
  </div>
</template>

<script setup lang="ts">
/**
 * FormField - Wrapper component for form inputs with label, help text, and error display
 *
 * Provides consistent styling and layout for form fields throughout the application.
 * Supports required field indicators and validation error messages.
 *
 * @example
 * <FormField label="Username" help="Enter your username" :required="true">
 *   <input type="text" v-model="username" />
 * </FormField>
 *
 * @example
 * <FormField label="Email" :error="emailError">
 *   <input type="email" v-model="email" />
 * </FormField>
 */
defineProps({
  /** Label text displayed above the input field */
  label: String,
  /** Optional help text displayed below the input */
  help: String,
  /** Error message displayed when validation fails */
  error: String,
  /** ID attribute for the label's 'for' attribute */
  id: String,
  /** Whether to display a required indicator (*) */
  required: { type: Boolean, default: false },
})
</script>

<style scoped>
.form-group {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin-bottom: 1rem;
}

.form-label {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  font-weight: 500;
  color: #fff;
  font-size: 0.95rem;
}

.required-indicator {
  color: #ff6b6b;
  font-weight: 500;
}

.help-text {
  display: block;
  margin-top: 0.25rem;
  color: #999;
  font-size: 0.85rem;
  line-height: 1.4;
}

.error-text {
  display: block;
  margin-top: 0.25rem;
  color: #ff6b6b;
  font-size: 0.85rem;
  line-height: 1.4;
}
</style>
