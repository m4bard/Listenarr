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
  <div class="form-section">
    <h3><PhGlobe /> External Requests</h3>

    <div class="form-body">
      <!-- External request options will be added here as needed -->
    </div>
  </div>
</template>

<script setup lang="ts">
import type { ApplicationSettings } from '@/types'
import { PhGlobe } from '@phosphor-icons/vue'

defineProps<{ settings: Partial<ApplicationSettings> }>()
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

/* Modal-like External Requests section */
.form-body {
  padding: 1.25rem;
  border-radius: 6px;
  border: 1px solid #333;
  box-shadow: 0 4px 14px rgba(0, 0, 0, 0.6);
  background-color: #232323;
}

.info-inline {
  background: none;
  border: none;
  color: #4dabf7;
  cursor: pointer;
  padding: 0.25rem;
  display: inline-flex;
  align-items: center;
  border-radius: 4px;
  transition: all 0.2s;
  font-size: 1rem;
}
.info-inline:hover {
  background: rgba(33, 150, 243, 0.1);
}

.form-group input[type='text'],
.form-group input[type='number'] {
  width: 100%;
  padding: 0.9rem 0.85rem;
  border: 1px solid #444;
  border-radius: 6px;
  background-color: #1a1a1a;
  color: #fff;
}

.form-group input:focus {
  outline: none;
  border-color: var(--brand-500);
  box-shadow: 0 0 0 3px rgba(77, 171, 247, 0.08);
}

.form-help {
  display: block;
  margin-top: 0.5rem;
  font-size: 0.85rem;
  color: #9aa3ad;
}
</style>
