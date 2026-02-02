<script setup lang="ts">
import type { Component } from 'vue'

/**
 * InfoCard Component
 *
 * A semantic card component for displaying information items with:
 * - Icon + content flex layout
 * - Label and value structure
 * - Hover effects and transitions
 *
 * Usage:
 * <InfoCard icon="PhDesktopTower">
 *   <template #label>Operating System</template>
 *   <template #value>Windows 11</template>
 * </InfoCard>
 *
 * Or with multiple value spans:
 * <InfoCard icon="PhMemory">
 *   <template #label>Memory Usage</template>
 *   <div class="info-content">
 *     <span>8GB / 16GB</span>
 *     <span class="percentage">(50%)</span>
 *   </div>
 * </InfoCard>
 */

withDefaults(
  defineProps<{
    icon?: Component
  }>(),
  {
    icon: undefined,
  }
)
</script>

<template>
  <div class="info-card">
    <component v-if="icon" :is="icon" class="info-icon" />
    <div class="info-content">
      <slot name="label">
        <label></label>
      </slot>
      <slot />
    </div>
  </div>
</template>

<style scoped>
.info-card {
  display: flex;
  align-items: flex-start;
  gap: 1rem;
  padding: 1.25rem;
  background: #252525;
  border: 1px solid #333;
  border-radius: 6px;
  transition: all 0.2s;
}

.info-card:hover {
  border-color: #444;
  background: #2a2a2a;
}

.info-icon {
  font-size: 2rem;
  color: var(--brand-focus);
  flex-shrink: 0;
}

.info-content {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.info-content label {
  font-size: 0.875rem;
  color: #aaa;
  font-weight: 500;
}

.info-content span {
  color: #fff;
  font-size: 1rem;
}

.info-content .percentage {
  color: #888;
  font-size: 0.875rem;
}
</style>
