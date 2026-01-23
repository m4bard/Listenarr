<template>
  <div id="global-toast" aria-live="polite" class="global-toast" />
</template>

<script setup lang="ts">
// Minimal global toast placeholder for tests and app bootstrapping.
// Real implementation can be expanded later; tests often mock toastService directly.
</script>

<style scoped>
.global-toast { position: fixed; bottom: 1rem; right: 1rem; z-index: 1200 }
</style>