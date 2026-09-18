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
  <Modal :visible="visible" size="md" @close="close">
    <template #header>
      <ModalHeader title="Calendar Feed" :icon="PhCalendarBlank" @close="close" />
    </template>

    <template #default>
      <ModalBody>
        <p class="feed-intro">
          Subscribe a calendar client to upcoming and recent audiobook releases. The URL below
          carries this instance's API key, so treat it as a secret.
        </p>

        <div class="section-card">
          <FormField
            id="calendar-feed-unmonitored"
            label="Include Unmonitored"
            help="Include audiobooks that are not currently monitored."
          >
            <input
              id="calendar-feed-unmonitored"
              v-model="unmonitored"
              data-testid="calendar-feed-unmonitored"
              type="checkbox"
            />
          </FormField>

          <FormField
            id="calendar-feed-past-days"
            label="Past Days"
            help="How far back the feed reaches. The *arr default is 7."
          >
            <input
              id="calendar-feed-past-days"
              v-model.number="pastDays"
              data-testid="calendar-feed-past-days"
              class="form-control"
              type="number"
              min="0"
              :max="CALENDAR_FEED_MAX_DAYS"
            />
          </FormField>

          <FormField
            id="calendar-feed-future-days"
            label="Future Days"
            help="How far forward the feed reaches. The *arr default is 28."
          >
            <input
              id="calendar-feed-future-days"
              v-model.number="futureDays"
              data-testid="calendar-feed-future-days"
              class="form-control"
              type="number"
              min="0"
              :max="CALENDAR_FEED_MAX_DAYS"
            />
          </FormField>

          <FormField
            id="calendar-feed-tags"
            label="Tags"
            help="Comma separated tag names. Leave empty for every audiobook."
          >
            <input
              id="calendar-feed-tags"
              v-model="tags"
              data-testid="calendar-feed-tags"
              class="form-control"
              type="text"
              placeholder="tbr, classics"
            />
          </FormField>
        </div>

        <div v-if="loadError" class="feed-notice" data-testid="calendar-feed-error">
          <PhWarning />
          <span>
            Could not read this instance's API key, so no subscribe URL can be built. Check the
            General page under Settings.
          </span>
        </div>

        <div v-else-if="!apiKey && !loading" class="feed-notice" data-testid="calendar-feed-no-key">
          <PhWarning />
          <span>
            This instance has no API key yet. Generate one on the General page under Settings, then
            reopen this dialog.
          </span>
        </div>

        <div v-else-if="urls.httpUrl" class="section-card">
          <div v-if="isDevServer" class="feed-notice" data-testid="calendar-feed-dev">
            <PhWarning />
            <span>
              Running against the dev server, which does not forward the feed route, so this URL
              answers with the app's HTML rather than a calendar. Build the app to test a real
              subscription.
            </span>
          </div>

          <FormField
            id="calendar-feed-http-url"
            label="iCalendar Feed"
            help="Paste this into a calendar client that subscribes to a URL."
          >
            <div class="feed-url-row">
              <input
                id="calendar-feed-http-url"
                :value="urls.httpUrl"
                data-testid="calendar-feed-http-url"
                class="form-control feed-url"
                type="text"
                readonly
                @focus="selectAll"
              />
              <button
                type="button"
                class="btn btn-secondary"
                data-testid="calendar-feed-copy-http"
                :aria-label="copied === 'http' ? 'Copied' : 'Copy feed URL'"
                :title="copied === 'http' ? 'Copied' : 'Copy feed URL'"
                @click="copy('http')"
              >
                <PhCheck v-if="copied === 'http'" />
                <PhCopy v-else />
              </button>
            </div>
          </FormField>

          <FormField
            id="calendar-feed-webcal-url"
            label="webcal Link"
            help="Most desktop calendar apps register a handler for this scheme."
          >
            <div class="feed-url-row">
              <input
                id="calendar-feed-webcal-url"
                :value="urls.webcalUrl"
                data-testid="calendar-feed-webcal-url"
                class="form-control feed-url"
                type="text"
                readonly
                @focus="selectAll"
              />
              <button
                type="button"
                class="btn btn-secondary"
                data-testid="calendar-feed-copy-webcal"
                :aria-label="copied === 'webcal' ? 'Copied' : 'Copy webcal URL'"
                :title="copied === 'webcal' ? 'Copied' : 'Copy webcal URL'"
                @click="copy('webcal')"
              >
                <PhCheck v-if="copied === 'webcal'" />
                <PhCopy v-else />
              </button>
              <a
                class="btn btn-secondary"
                data-testid="calendar-feed-open-webcal"
                :href="urls.webcalUrl"
                target="_blank"
                rel="noopener"
                aria-label="Open in calendar application"
                title="Open in calendar application"
              >
                <PhCalendarPlus />
              </a>
            </div>
          </FormField>
        </div>
      </ModalBody>
    </template>

    <template #footer>
      <ModalFooter :showCancel="false">
        <template #default>
          <button
            type="button"
            class="btn btn-primary"
            data-testid="calendar-feed-close"
            @click="close"
          >
            <PhX /> Close
          </button>
        </template>
      </ModalFooter>
    </template>
  </Modal>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import {
  PhCalendarBlank,
  PhCalendarPlus,
  PhCheck,
  PhCopy,
  PhWarning,
  PhX,
} from '@phosphor-icons/vue'
import Modal from '@/components/feedback/Modal.vue'
import ModalHeader from '@/components/feedback/ModalHeader.vue'
import ModalBody from '@/components/feedback/ModalBody.vue'
import ModalFooter from '@/components/feedback/ModalFooter.vue'
import FormField from '@/components/base/FormField.vue'
import { apiService } from '@/services/api'
import { API_BASE_PATH, API_ORIGIN } from '@/services/apiBase'
import { logger } from '@/utils/logger'
import {
  CALENDAR_FEED_DEFAULT_FUTURE_DAYS,
  CALENDAR_FEED_DEFAULT_PAST_DAYS,
  CALENDAR_FEED_MAX_DAYS,
  buildCalendarFeedUrls,
  calendarFeedUrlBase,
  resolveCalendarFeedOrigin,
} from '@/utils/calendarFeedUrl'

const props = defineProps<{ visible: boolean }>()
const emit = defineEmits<{ close: [] }>()

const unmonitored = ref(false)
const pastDays = ref<number>(CALENDAR_FEED_DEFAULT_PAST_DAYS)
const futureDays = ref<number>(CALENDAR_FEED_DEFAULT_FUTURE_DAYS)
const tags = ref('')

const apiKey = ref('')
const loading = ref(false)
const loadError = ref(false)
const copied = ref<'http' | 'webcal' | null>(null)

// The dev server proxies /api and /hubs and not /feed (fe/vite.config.ts), so a URL generated
// under `npm run dev` falls through to the SPA and answers with HTML and a 200. That is the
// failure that looks like success, so the dialog says so rather than letting someone validate
// the feature against it.
const isDevServer = import.meta.env.DEV

const urls = computed(() =>
  buildCalendarFeedUrls(
    {
      unmonitored: unmonitored.value,
      pastDays: pastDays.value,
      futureDays: futureDays.value,
      tags: tags.value,
      apiKey: apiKey.value,
    },
    resolveCalendarFeedOrigin(
      API_ORIGIN,
      { protocol: window.location.protocol, host: window.location.host },
      isDevServer,
    ),
    calendarFeedUrlBase(API_BASE_PATH),
  ),
)

async function loadApiKey() {
  loading.value = true
  loadError.value = false
  try {
    const response = await apiService.getApiKey()
    apiKey.value = response?.apiKey ?? ''
  } catch (error) {
    apiKey.value = ''
    loadError.value = true
    logger.error('[CalendarFeedModal] Could not read the API key', error)
  } finally {
    loading.value = false
  }
}

watch(
  () => props.visible,
  (isVisible) => {
    if (isVisible && !apiKey.value) {
      void loadApiKey()
    }
    if (!isVisible) {
      copied.value = null
    }
  },
  { immediate: true },
)

function selectAll(event: FocusEvent) {
  ;(event.target as HTMLInputElement | null)?.select()
}

async function copy(which: 'http' | 'webcal') {
  const value = which === 'http' ? urls.value.httpUrl : urls.value.webcalUrl
  if (!value) return
  try {
    await navigator.clipboard.writeText(value)
    copied.value = which
    setTimeout(() => {
      if (copied.value === which) copied.value = null
    }, 2000)
  } catch (error) {
    logger.error('[CalendarFeedModal] Clipboard write failed', error)
  }
}

function close() {
  emit('close')
}
</script>

<style scoped>
.feed-intro {
  margin: 0 0 1rem;
  color: var(--text-secondary, #adb5bd);
}

.feed-url-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.feed-url {
  flex: 1;
  font-family: monospace;
  min-width: 0;
}

.feed-notice {
  display: flex;
  align-items: flex-start;
  gap: 0.5rem;
  margin-top: 1rem;
  padding: 0.75rem;
  border-radius: 6px;
  background: rgba(var(--brand-rgb, 255, 193, 7), 0.1);
}
</style>
