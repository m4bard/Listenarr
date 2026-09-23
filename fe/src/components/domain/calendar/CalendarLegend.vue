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
<!--
  Names every state getCalendarEventStatus can resolve, the same way Readarr's own
  calendar legend does (frontend/src/Calendar/Legend/Legend.js:27-73): one entry per
  status, in the same precedence order the status resolver checks them in.
-->
<template>
  <div class="calendar-legend" data-testid="calendar-legend">
    <span v-for="status in CALENDAR_EVENT_STATUSES" :key="status" class="calendar-legend-item">
      <span :class="['calendar-legend-dot', `status-${status}`]"></span>
      {{ formatCalendarEventStatus(status) }}
    </span>
  </div>
</template>

<script setup lang="ts">
import { CALENDAR_EVENT_STATUSES, formatCalendarEventStatus } from '@/utils/calendarEventStatus'
</script>

<style scoped>
.calendar-legend {
  display: flex;
  flex-wrap: wrap;
  gap: 1rem;
  padding: 0.5rem 0 1rem;
  font-size: 0.8rem;
  color: #adb5bd;
}

.calendar-legend-item {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
}

.calendar-legend-dot {
  width: 10px;
  height: 10px;
  border-radius: 999px;
  display: inline-block;
}

/*
 * Same palette as the event chips' left-border accent (CalendarView.vue), and the same
 * downloaded/downloading/missing hues AudiobooksView.vue's own status legend already
 * uses, so a colour means the same thing everywhere in the app.
 */
.calendar-legend-dot.status-downloaded {
  background-color: #2ecc71;
}

.calendar-legend-dot.status-downloading {
  background-color: #3498db;
}

.calendar-legend-dot.status-unmonitored {
  background-color: #7f8c8d;
}

.calendar-legend-dot.status-missing {
  background-color: #e74c3c;
}

.calendar-legend-dot.status-unreleased {
  background-color: #9b59b6;
}
</style>
