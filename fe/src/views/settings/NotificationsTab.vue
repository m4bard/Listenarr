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
  <div class="tab-content">
    <div class="notifications-tab">
      <div class="section-header">
        <h3>
          Notifications
          <PhSpinner v-if="loading" class="ph-spin small-inline-spinner" />
        </h3>
      </div>

      <LoadingState v-if="loading && webhooks.length === 0" message="Loading notifications..." />

      <div v-else-if="webhooks.length === 0" class="empty-state">
        <PhBellSlash class="empty-icon" />
        <h3>No webhooks configured</h3>
        <p>Webhooks allow you to receive real-time notifications when important events occur.</p>
        <p class="empty-help">
          Supported services include Slack, Discord, Telegram, Pushover, and more.
        </p>
      </div>

      <div v-else class="webhooks-grid">
        <div
          v-for="webhook in webhooks"
          :key="webhook.id"
          class="webhook-card"
          :class="{ disabled: !webhook.isEnabled }"
        >
          <div class="webhook-header">
            <div class="webhook-title-row">
              <div class="webhook-info">
                <h4 class="webhook-title">
                  <component
                    :is="getWebhookTypeIcon(webhook.type)"
                    class="webhook-type-icon"
                    :title="webhook.type"
                    :aria-label="webhook.type"
                    role="img"
                  />
                  <span class="webhook-name">{{ webhook.name }}</span>
                </h4>
                <div class="webhook-meta">
                  <div class="triggers-preview">
                    <span
                      v-for="trigger in orderedTriggers(webhook.triggers)"
                      :key="trigger"
                      class="trigger-badge-small"
                      :class="getTriggerClass(trigger)"
                      :title="formatTriggerName(trigger)"
                    >
                      <component :is="getTriggerIcon(trigger)" />
                    </span>
                  </div>
                </div>
              </div>
            </div>
            <div class="webhook-header-actions">
              <button
                class="icon-button action-secondary action-toggle"
                :class="{ active: webhook.isEnabled }"
                :title="webhook.isEnabled ? 'Disable webhook' : 'Enable webhook'"
                @click.stop="toggleWebhook(webhook)"
              >
                <component :is="webhook.isEnabled ? PhToggleRight : PhToggleLeft" />
              </button>

              <button
                class="icon-button action-secondary"
                :class="{
                  'test-success': lastWebhookTestResults[webhook.id] === 'success',
                  'test-fail': lastWebhookTestResults[webhook.id] === 'fail',
                }"
                :title="!webhook.isEnabled ? 'Enable webhook to test' : 'Send test notification'"
                @click.stop="testWebhook(webhook)"
                :disabled="testingWebhook === webhook.id || !webhook.isEnabled"
              >
                <PhSpinner v-if="testingWebhook === webhook.id" class="ph-spin" />
                <template v-else-if="lastWebhookTestResults[webhook.id] === 'success'">
                  <PhCheckCircle />
                </template>
                <template v-else-if="lastWebhookTestResults[webhook.id] === 'fail'">
                  <PhXCircle />
                </template>
                <template v-else>
                  <PhPaperPlaneTilt />
                </template>
              </button>

              <button
                class="icon-button action-edit"
                title="Edit webhook"
                @click.stop="editWebhook(webhook)"
              >
                <PhPencil />
              </button>

              <button
                class="icon-button danger action-delete"
                title="Delete webhook"
                @click.stop="confirmDeleteWebhook(webhook)"
              >
                <PhTrash />
              </button>
            </div>
          </div>

          <div class="webhook-body">
            <div class="webhook-url-container">
              <PhLink class="url-icon" />
              <span class="webhook-url">{{ webhook.url }}</span>
            </div>
          </div>
        </div>
      </div>

      <!-- Webhook Configuration Modal (shared Modal component) -->
      <Modal
        class="webhook-modal"
        :visible="showWebhookForm"
        size="md"
        :title="editingWebhook ? 'Edit Webhook' : 'Add Webhook'"
        @close="closeWebhookForm"
      >
        <template #header>
          <ModalHeader
            :title="(editingWebhook ? 'Edit' : 'Add') + ' Webhook'"
            :icon="PhLink"
            @close="closeWebhookForm"
          />
        </template>

        <form @submit.prevent="saveWebhook">
          <!-- Delete Webhook Confirmation Modal (shared) -->
          <DeleteConfirmationModal
            :visible="!!webhookToDelete"
            title="Delete Webhook"
            @close="webhookToDelete = null"
            @confirm="executeDeleteWebhook"
          >
            <template v-slot>
              <p>
                Are you sure you want to delete the webhook
                <strong>{{ webhookToDelete?.name }}</strong
                >?
              </p>
              <p>This action cannot be undone.</p>
            </template>
          </DeleteConfirmationModal>

          <!-- Activation -->
          <FormSection title="Activation" :icon="PhToggleRight">
            <CheckboxCard
              v-model="webhookForm.isEnabled"
              title="Enable"
              description="Enable this webhook to start receiving notifications"
            />
          </FormSection>

          <!-- Basic Configuration Section -->
          <FormSection title="Basic" :icon="PhInfo">
            <FormRow label="Name *" labelFor="webhook-name">
              <input
                id="webhook-name"
                v-model="webhookForm.name"
                type="text"
                placeholder="e.g., Production Slack Channel"
                required
                @blur="validateWebhookField('name')"
              />
              <small v-if="webhookFormErrors.name" class="error-text">{{
                webhookFormErrors.name
              }}</small>
            </FormRow>

            <FormRow label="Type *" labelFor="webhook-type">
              <select
                id="webhook-type"
                v-model="webhookForm.type"
                required
                @change="onServiceTypeChange"
                @blur="validateWebhookField('type')"
              >
                <option value="" disabled>Select type...</option>
                <option value="Slack">Slack</option>
                <option value="Discord">Discord</option>
                <option value="Telegram">Telegram</option>
                <option value="Pushover">Pushover</option>
                <option value="Pushbullet">Pushbullet</option>
                <option value="NTFY">NTFY</option>
                <option value="Zapier">Zapier / Generic</option>
              </select>
              <small v-if="webhookFormErrors.type" class="error-text">{{
                webhookFormErrors.type
              }}</small>
              <small v-else-if="getServiceHelp()">{{ getServiceHelp() }}</small>
            </FormRow>

            <FormRow
              v-if="
                webhookForm.type !== 'Telegram' &&
                webhookForm.type !== 'Pushover' &&
                webhookForm.type !== 'Pushbullet'
              "
              label="Webhook URL *"
              labelFor="webhook-url"
            >
              <input
                id="webhook-url"
                v-model="webhookForm.url"
                type="url"
                placeholder="https://hooks.example.com/services/your-webhook-url"
                required
                @blur="validateWebhookField('url')"
              />
              <small v-if="webhookFormErrors.url" class="error-text">{{
                webhookFormErrors.url
              }}</small>
            </FormRow>

            <FormRow
              v-if="webhookForm.type === 'Telegram'"
              label="Bot Token *"
              labelFor="telegram-bot-token"
            >
              <input
                id="telegram-bot-token"
                v-model="webhookForm.telegramBotToken"
                type="text"
                placeholder="123456:ABCdefGhIJklMNopqRst_uvwxYZ"
                required
                @blur="validateWebhookField('url')"
              />
              <small v-if="webhookFormErrors.url" class="error-text">{{
                webhookFormErrors.url
              }}</small>
            </FormRow>
            <FormRow
              v-if="webhookForm.type === 'Pushover'"
              label="Pushover User Key"
              labelFor="pushover-user-key"
            >
              <input
                id="pushover-user-key"
                v-model="webhookForm.pushoverUserKey"
                type="text"
                placeholder="User key (e.g., uQiRzpo4DXghDmr9QzzfQu27cmVRsG)"
              />
            </FormRow>

            <FormRow
              v-if="webhookForm.type === 'Pushover'"
              label="Pushover API Token"
              labelFor="pushover-api-token"
            >
              <input
                id="pushover-api-token"
                v-model="webhookForm.pushoverApiToken"
                type="text"
                placeholder="Application API token (keep secret)"
              />
              <small v-if="webhookForm.type === 'Pushover'" class="help-text"
                >You can provide both keys instead of a full webhook URL; they'll be composed on
                save.</small
              >
            </FormRow>
            <FormRow
              v-if="webhookForm.type === 'Pushbullet'"
              label="Pushbullet Access Token"
              labelFor="pushbullet-access-token"
            >
              <input
                id="pushbullet-access-token"
                v-model="webhookForm.pushbulletAccessToken"
                type="text"
                placeholder="Access token (keep secret)"
                required
                @blur="validateWebhookField('url')"
              />
              <small v-if="webhookForm.type === 'Pushbullet'" class="help-text"
                >Get your Access Token from Pushbullet → Settings → Account → Access Tokens</small
              >
            </FormRow>
            <FormRow
              v-if="webhookForm.type === 'Telegram'"
              label="Chat ID (optional)"
              labelFor="telegram-chat-id"
            >
              <input
                id="telegram-chat-id"
                v-model="webhookForm.telegramChatId"
                type="text"
                placeholder="e.g., 123456789 or @channelusername"
              />
              <small class="help-text"
                >Provide a chat ID to target messages. If left blank, include chat_id in the
                URL.</small
              >
            </FormRow>
          </FormSection>

          <!-- Triggers Section -->
          <FormSection title="Triggers" :icon="PhBell">
            <div class="webhook-triggers triggers-grid">
              <CheckboxCard
                v-for="t in ['book-added', 'book-downloading', 'book-available', 'book-completed']"
                :key="t"
                :modelValue="webhookForm.triggers.includes(t)"
                @update:modelValue="onToggleTriggerValue(t, $event)"
                :title="formatTriggerName(t)"
              >
                <template #default>
                  <component :is="getTriggerIcon(t)" class="trigger-icon" />
                </template>
              </CheckboxCard>
            </div>
            <small v-if="webhookFormErrors.triggers" class="error-text">{{
              webhookFormErrors.triggers
            }}</small>
          </FormSection>
        </form>
        <template #footer>
          <ModalFooter :showCancel="false">
            <template #left>
              <button @click="closeWebhookForm" class="cancel-button btn" type="button">
                <PhX /> Cancel
              </button>
            </template>
            <template #default>
              <button
                v-if="webhookForm.type && !editingWebhook"
                @click="testWebhookConfig"
                class="btn btn-info"
                type="button"
                :disabled="testingWebhookConfig"
              >
                <PhSpinner v-if="testingWebhookConfig" class="ph-spin" />
                {{ testingWebhookConfig ? 'Testing...' : 'Test' }}
              </button>
              <button
                @click="saveWebhook"
                class="btn btn-primary"
                type="button"
                :disabled="!isWebhookFormValid || savingWebhook"
              >
                <PhSpinner v-if="savingWebhook" class="ph-spin" />
                {{ savingWebhook ? 'Saving...' : editingWebhook ? 'Update' : 'Save' }}
              </button>
            </template>
          </ModalFooter>
        </template>
      </Modal>

      <div class="section-header custom-scripts-header">
        <h3>Custom Scripts</h3>
      </div>

      <div v-if="customScripts.length === 0" class="empty-state">
        <PhTerminalWindow class="empty-icon" />
        <h3>No custom scripts configured</h3>
        <p>
          A custom script runs your own executable when a notification event fires, for anything
          Listenarr does not integrate with directly (a media server rescan, an OPDS feed refresh,
          a backup trigger).
        </p>
      </div>

      <div v-else class="scripts-grid">
        <div
          v-for="script in customScripts"
          :key="script.id"
          class="script-card"
          :class="{ disabled: !script.isEnabled }"
        >
          <div class="webhook-header">
            <div class="webhook-title-row">
              <div class="webhook-info">
                <h4 class="webhook-title">
                  <PhTerminalWindow class="webhook-type-icon" />
                  <span class="webhook-name">{{ script.name }}</span>
                </h4>
                <div class="webhook-meta">
                  <span class="script-channel-count">
                    {{ script.channels.length }} event{{ script.channels.length === 1 ? '' : 's' }}
                  </span>
                </div>
              </div>
            </div>
            <div class="webhook-header-actions">
              <button
                class="icon-button action-secondary action-toggle"
                :class="{ active: script.isEnabled }"
                :title="script.isEnabled ? 'Disable script' : 'Enable script'"
                @click.stop="toggleScript(script)"
              >
                <component :is="script.isEnabled ? PhToggleRight : PhToggleLeft" />
              </button>

              <button
                class="icon-button action-secondary"
                :class="{
                  'test-success': lastScriptTestResults[script.id] === 'success',
                  'test-fail': lastScriptTestResults[script.id] === 'fail',
                }"
                title="Run this script now with a test event"
                @click.stop="testScript(script)"
                :disabled="testingScript === script.id"
              >
                <PhSpinner v-if="testingScript === script.id" class="ph-spin" />
                <template v-else-if="lastScriptTestResults[script.id] === 'success'">
                  <PhCheckCircle />
                </template>
                <template v-else-if="lastScriptTestResults[script.id] === 'fail'">
                  <PhXCircle />
                </template>
                <template v-else>
                  <PhPaperPlaneTilt />
                </template>
              </button>

              <button
                class="icon-button action-edit"
                title="Edit script"
                @click.stop="editScript(script)"
              >
                <PhPencil />
              </button>

              <button
                class="icon-button danger action-delete"
                title="Delete script"
                @click.stop="confirmDeleteScript(script)"
              >
                <PhTrash />
              </button>
            </div>
          </div>

          <div class="webhook-body">
            <div class="webhook-url-container">
              <PhTerminalWindow class="url-icon" />
              <span class="webhook-url">{{ script.path }}</span>
            </div>
            <p
              v-if="lastScriptTestMessages[script.id]"
              class="script-test-message"
              :class="lastScriptTestResults[script.id]"
            >
              {{ lastScriptTestMessages[script.id] }}
            </p>
          </div>
        </div>
      </div>

      <!-- Custom Script Configuration Modal (shared Modal component) -->
      <Modal
        class="script-modal"
        :visible="showScriptForm"
        size="md"
        :title="editingScript ? 'Edit Custom Script' : 'Add Custom Script'"
        @close="closeScriptForm"
      >
        <template #header>
          <ModalHeader
            :title="(editingScript ? 'Edit' : 'Add') + ' Custom Script'"
            :icon="PhTerminalWindow"
            @close="closeScriptForm"
          />
        </template>

        <form @submit.prevent="saveScript">
          <DeleteConfirmationModal
            :visible="!!scriptToDelete"
            title="Delete Custom Script"
            @close="scriptToDelete = null"
            @confirm="executeDeleteScript"
          >
            <template v-slot>
              <p>
                Are you sure you want to delete the custom script
                <strong>{{ scriptToDelete?.name }}</strong
                >?
              </p>
              <p>This action cannot be undone.</p>
            </template>
          </DeleteConfirmationModal>

          <div class="script-warning">
            <PhWarning />
            <p>
              Listenarr runs this path as a local process, with the same permissions Listenarr
              itself has: on every event checked below, and whenever you press Test (with
              <code>Listenarr_EventType=Test</code>). Point it only at a script you trust. A run
              that has not finished after 60 seconds is stopped.
            </p>
          </div>

          <FormSection title="Activation" :icon="PhToggleRight">
            <CheckboxCard
              v-model="scriptForm.isEnabled"
              title="Enable"
              description="Enable this script to run on its selected events"
            />
          </FormSection>

          <FormSection title="Basic" :icon="PhInfo">
            <FormRow label="Name *" labelFor="script-name">
              <input
                id="script-name"
                v-model="scriptForm.name"
                type="text"
                placeholder="e.g., Audiobookshelf rescan"
                required
                @blur="validateScriptField('name')"
              />
              <small v-if="scriptFormErrors.name" class="error-text">{{
                scriptFormErrors.name
              }}</small>
            </FormRow>

            <FormRow label="Path *" labelFor="script-path">
              <input
                id="script-path"
                v-model="scriptForm.path"
                type="text"
                placeholder="/opt/scripts/on-event.sh"
                required
                @blur="validateScriptField('path')"
              />
              <small v-if="scriptFormErrors.path" class="error-text">{{
                scriptFormErrors.path
              }}</small>
              <small v-else class="help-text"
                >Absolute path to an executable file. Listenarr runs it directly, with no shell in
                between, and checks it exists when the script runs or is tested.</small
              >
            </FormRow>
          </FormSection>

          <FormSection title="Events" :icon="PhBell">
            <div class="webhook-triggers script-channels triggers-grid">
              <CheckboxCard
                v-for="c in scriptChannelOptions"
                :key="c.value"
                :modelValue="scriptForm.channels.includes(c.value)"
                @update:modelValue="onToggleScriptChannel(c.value, $event)"
                :title="c.label"
                :description="c.description"
              />
            </div>
            <small v-if="scriptFormErrors.channels" class="error-text">{{
              scriptFormErrors.channels
            }}</small>
          </FormSection>

          <FormSection title="Environment Variables" :icon="PhCode">
            <button
              type="button"
              class="env-vars-toggle"
              @click="showEnvVars = !showEnvVars"
              :aria-expanded="showEnvVars"
            >
              <component :is="showEnvVars ? PhCaretUp : PhCaretDown" />
              {{ showEnvVars ? 'Hide' : 'Show' }} the {{ environmentVariables.length }} variables
              your script receives
            </button>
            <div v-if="showEnvVars" class="env-vars-list">
              <div v-for="v in environmentVariables" :key="v.name" class="env-var-row">
                <code>{{ v.name }}</code>
                <span>{{ v.description }}</span>
              </div>
            </div>
          </FormSection>

          <p
            v-if="scriptFormTestMessage"
            class="script-test-message"
            :class="scriptFormTestSuccess ? 'success' : 'fail'"
          >
            {{ scriptFormTestMessage }}
          </p>
        </form>
        <template #footer>
          <ModalFooter :showCancel="false">
            <template #left>
              <button @click="closeScriptForm" class="cancel-button btn" type="button">
                <PhX /> Cancel
              </button>
            </template>
            <template #default>
              <button
                v-if="editingScript"
                @click="testExistingScript"
                class="btn btn-info"
                type="button"
                :disabled="testingScriptForm"
                title="Runs the last saved version of this script, not your unsaved edits"
              >
                <PhSpinner v-if="testingScriptForm" class="ph-spin" />
                {{ testingScriptForm ? 'Testing...' : 'Test' }}
              </button>
              <button
                @click="saveScript"
                class="btn btn-primary"
                type="button"
                :disabled="!isScriptFormValid || savingScript"
              >
                <PhSpinner v-if="savingScript" class="ph-spin" />
                {{ savingScript ? 'Saving...' : editingScript ? 'Update' : 'Save' }}
              </button>
            </template>
          </ModalFooter>
        </template>
      </Modal>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted, nextTick } from 'vue'
import {
  PhBell,
  PhBellSlash,
  PhPlus,
  PhCheckCircle,
  PhXCircle,
  PhCircleWavyCheck,
  PhToggleRight,
  PhToggleLeft,
  PhSpinner,
  PhPaperPlaneTilt,
  PhPencil,
  PhTrash,
  PhLink,
  PhX,
  PhDownloadSimple,
  PhSlackLogo,
  PhDiscordLogo,
  PhTelegramLogo,
  PhPushPinSimple,
  PhInfo,
  PhTerminalWindow,
  PhWarning,
  PhCode,
  PhCaretDown,
  PhCaretUp,
} from '@phosphor-icons/vue'
import { Modal, ModalHeader, ModalFooter } from '@/components/feedback'
import DeleteConfirmationModal from '@/components/feedback/DeleteConfirmationModal.vue'
// Checkbox not used directly here; CheckboxCard wraps checkbox UI
import FormSection from '@/components/settings/FormSection.vue'
import FormRow from '@/components/settings/FormRow.vue'
import CheckboxCard from '@/components/settings/CheckboxCard.vue'
import { LoadingState } from '@/components/base'
import { errorTracking } from '@/services/errorTracking'
import { useToast } from '@/services/toastService'
import { useConfigurationStore } from '@/stores/configuration'
import type { ApplicationSettings, CustomScriptConfiguration, NotificationChannel } from '@/types'
import { apiService } from '@/services/api'

// Props
const props = defineProps<{
  settings: ApplicationSettings | null
}>()
const emit = defineEmits<{
  'update:settings': [value: ApplicationSettings]
}>()

const toast = useToast()
const configStore = useConfigurationStore()
const loading = computed(() => configStore.isLoading || !props.settings)

// Helper function to format API errors
const formatApiError = (err: unknown): string => {
  if (err && typeof err === 'object' && 'message' in err) {
    return String((err as { message: string }).message)
  }
  return 'An unknown error occurred'
}

/* Triggers grid styles */

// State
const showWebhookForm = ref(false)
const editingWebhook = ref<{
  id: string
  name: string
  url: string
  type: 'Pushbullet' | 'Telegram' | 'Slack' | 'Discord' | 'Pushover' | 'NTFY' | 'Zapier'
  triggers: string[]
  isEnabled: boolean
} | null>(null)
const testingWebhook = ref<string | null>(null)
// Per-webhook ephemeral test results
const lastWebhookTestResults = reactive<Record<string, 'success' | 'fail' | undefined>>({})
const webhooks = ref<
  Array<{
    id: string
    name: string
    url: string
    type: 'Pushbullet' | 'Telegram' | 'Slack' | 'Discord' | 'Pushover' | 'NTFY' | 'Zapier'
    triggers: string[]
    isEnabled: boolean
  }>
>([])

const webhookForm = reactive({
  id: '',
  name: '',
  url: '',
  type: '' as 'Pushbullet' | 'Telegram' | 'Slack' | 'Discord' | 'Pushover' | 'NTFY' | 'Zapier' | '',
  triggers: [] as string[],
  isEnabled: true,
  telegramChatId: '',
  telegramBotToken: '',
  pushoverUserKey: '',
  pushoverApiToken: '',
  pushbulletAccessToken: '',
})

const webhookFormErrors = reactive({
  name: '',
  url: '',
  type: '',
  triggers: '',
})

const testingWebhookConfig = ref(false)
const savingWebhook = ref(false)

// Computed
const isWebhookFormValid = computed(() => {
  if (!webhookForm.name.trim() || webhookForm.type === '') return false
  if (webhookFormErrors.name || webhookFormErrors.url || webhookFormErrors.type) return false

  // Service-specific required fields
  if (webhookForm.type === 'Telegram') {
    return !!(webhookForm.telegramBotToken && webhookForm.telegramBotToken.trim().length > 0)
  }

  if (webhookForm.type === 'Pushover') {
    return !!(
      webhookForm.pushoverApiToken &&
      webhookForm.pushoverUserKey &&
      webhookForm.pushoverApiToken.trim().length > 0 &&
      webhookForm.pushoverUserKey.trim().length > 0
    )
  }

  if (webhookForm.type === 'Pushbullet') {
    return !!(
      webhookForm.pushbulletAccessToken && webhookForm.pushbulletAccessToken.trim().length > 0
    )
  }

  // Default: require URL
  return !!(webhookForm.url && webhookForm.url.trim().length > 0)
})

// Helper functions
function generateUUID(): string {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
    const r = (Math.random() * 16) | 0
    const v = c === 'x' ? r : (r & 0x3) | 0x8
    return v.toString(16)
  })
}

const getTriggerIcon = (trigger: string) => {
  const iconMap: Record<string, unknown> = {
    'book-added': PhPlus,
    'book-downloading': PhDownloadSimple,
    'book-available': PhCheckCircle,
    'book-completed': PhCircleWavyCheck,
  }
  return iconMap[trigger] || PhBell
}

const getWebhookTypeIcon = (type: string) => {
  const t = (type || '').toLowerCase()
  const map: Record<string, unknown> = {
    slack: PhSlackLogo,
    discord: PhDiscordLogo,
    telegram: PhTelegramLogo,
    pushover: PhBell,
    pushbullet: PhPushPinSimple,
    ntfy: PhBell,
    zapier: PhPaperPlaneTilt,
  }
  return map[t] || PhLink
}

const getTriggerClass = (trigger: string): string => {
  const classMap: Record<string, string> = {
    'book-added': 'trigger-added',
    'book-downloading': 'trigger-downloading',
    'book-available': 'trigger-available',
    'book-completed': 'trigger-completed',
  }
  return classMap[trigger] || ''
}

// Return triggers in a consistent display order
const orderedTriggerList = ['book-added', 'book-downloading', 'book-available', 'book-completed']

const orderedTriggers = (triggers: string[] | undefined) => {
  if (!triggers || triggers.length === 0) return []
  return orderedTriggerList.filter((t) => triggers.includes(t))
}

const formatTriggerName = (trigger: string): string => {
  const nameMap: Record<string, string> = {
    'book-added': 'Book Added',
    'book-downloading': 'Download Started',
    'book-available': 'Download Complete',
    'book-completed': 'Processing Complete',
  }
  return nameMap[trigger] || trigger
}

const isValidUrl = (url: string): boolean => {
  try {
    const urlObj = new URL(url)
    return urlObj.protocol === 'https:' || urlObj.protocol === 'http:'
  } catch {
    return false
  }
}

const validateWebhookField = (field: 'name' | 'url' | 'type' | 'triggers') => {
  switch (field) {
    case 'name':
      if (!webhookForm.name || webhookForm.name.trim().length === 0) {
        webhookFormErrors.name = 'Webhook name is required'
      } else if (webhookForm.name.trim().length < 3) {
        webhookFormErrors.name = 'Name must be at least 3 characters'
      } else {
        webhookFormErrors.name = ''
      }
      break
    case 'url':
      // Validation differs by service type
      if (webhookForm.type === 'Telegram') {
        if (!webhookForm.telegramBotToken || webhookForm.telegramBotToken.trim().length === 0) {
          webhookFormErrors.url = 'Bot token is required for Telegram'
        } else if (!/^[0-9]+:[A-Za-z0-9_-]+$/.test(webhookForm.telegramBotToken.trim())) {
          webhookFormErrors.url = 'Please enter a valid Telegram bot token (e.g. 123456:ABC...)'
        } else {
          webhookFormErrors.url = ''
        }
      } else if (webhookForm.type === 'Pushover') {
        if (!webhookForm.pushoverApiToken || !webhookForm.pushoverUserKey) {
          webhookFormErrors.url = 'Pushover API Token and User Key are required'
        } else {
          webhookFormErrors.url = ''
        }
      } else if (webhookForm.type === 'Pushbullet') {
        if (
          !webhookForm.pushbulletAccessToken ||
          webhookForm.pushbulletAccessToken.trim().length === 0
        ) {
          webhookFormErrors.url = 'Pushbullet Access Token is required'
        } else {
          webhookFormErrors.url = ''
        }
      } else {
        if (!webhookForm.url || webhookForm.url.trim().length === 0) {
          webhookFormErrors.url = 'Webhook URL is required'
        } else if (!isValidUrl(webhookForm.url)) {
          webhookFormErrors.url = 'Please enter a valid URL'
        } else {
          webhookFormErrors.url = ''
        }
      }
      break
    case 'type':
      if (!webhookForm.type) {
        webhookFormErrors.type = 'Please select a service type'
      } else {
        webhookFormErrors.type = ''
      }
      break
    case 'triggers':
      if (webhookForm.triggers.length === 0) {
        webhookFormErrors.triggers = 'Please select at least one trigger'
      } else {
        webhookFormErrors.triggers = ''
      }
      break
  }
}

const resetWebhookFormErrors = () => {
  webhookFormErrors.name = ''
  webhookFormErrors.url = ''
  webhookFormErrors.type = ''
  webhookFormErrors.triggers = ''
}

const onToggleTrigger = (trigger: string, enabled: boolean) => {
  const idx = webhookForm.triggers.indexOf(trigger)
  if (enabled && idx === -1) webhookForm.triggers.push(trigger)
  if (!enabled && idx !== -1) webhookForm.triggers.splice(idx, 1)
}

const onToggleTriggerValue = (trigger: string, value: boolean) => {
  onToggleTrigger(trigger, value)
}

const onServiceTypeChange = () => {
  validateWebhookField('type')
}

const getServiceHelp = (): string => {
  const helpText: Record<string, string> = {
    Slack:
      'Get your webhook URL from Slack: Settings & administration → Manage apps → Incoming Webhooks',
    Discord: 'Server Settings → Integrations → Webhooks → New Webhook → Copy Webhook URL',
    Telegram:
      'Create a bot with @BotFather. Enter the bot token (e.g. 123456:ABC...) or the full webhook URL (https://api.telegram.org/bot{token}/sendMessage). Optionally provide a Chat ID below.',
    Pushover: 'Get your User Key and API Token from pushover.net/apps/build',
    Pushbullet: 'Get your Access Token from Settings → Account → Access Tokens',
    NTFY: 'Use format: https://ntfy.sh/{topic} or your self-hosted instance URL',
    Zapier: 'Create a Zap with "Webhooks by Zapier" and copy the webhook URL',
  }
  return webhookForm.type ? helpText[webhookForm.type] || '' : ''
}

// Webhook CRUD operations
const openWebhookForm = () => {
  editingWebhook.value = null
  webhookForm.id = ''
  webhookForm.name = ''
  webhookForm.url = ''
  webhookForm.type = ''
  webhookForm.triggers = []
  webhookForm.isEnabled = true
  webhookForm.telegramChatId = ''
  webhookForm.telegramBotToken = ''
  webhookForm.pushoverUserKey = ''
  webhookForm.pushoverApiToken = ''
  resetWebhookFormErrors()
  showWebhookForm.value = true
}

const closeWebhookForm = () => {
  showWebhookForm.value = false
  editingWebhook.value = null
  webhookForm.id = ''
  webhookForm.name = ''
  webhookForm.url = ''
  webhookForm.type = ''
  webhookForm.triggers = []
  webhookForm.isEnabled = true
  webhookForm.telegramChatId = ''
  resetWebhookFormErrors()
}

const editWebhook = (webhook: (typeof webhooks.value)[0]) => {
  editingWebhook.value = webhook
  webhookForm.id = webhook.id
  webhookForm.name = webhook.name
  webhookForm.url = webhook.url
  webhookForm.type = webhook.type
  webhookForm.triggers = [...webhook.triggers]
  webhookForm.isEnabled = webhook.isEnabled
  // If stored URL contains a Telegram chat_id query param, extract it for editing
  try {
    if (webhook.type === 'Telegram' && webhook.url) {
      try {
        const u = new URL(webhook.url)
        // path is like /bot<TOKEN>/sendMessage
        const segments = u.pathname.split('/')
        const botSegment = segments.find((s) => s.startsWith('bot')) || ''
        const token = botSegment.startsWith('bot') ? botSegment.substring(3) : ''
        const tokenVal = token || ''
        const params = u.searchParams
        const cid = params.get('chat_id')
        webhookForm.telegramChatId = cid || ''
        webhookForm.telegramBotToken = tokenVal
        // clear URL field for token-based editing
        webhookForm.url = ''
      } catch {
        webhookForm.telegramChatId = ''
        webhookForm.telegramBotToken = ''
      }
    } else if (webhook.type === 'Pushover' && webhook.url) {
      try {
        const u = new URL(webhook.url)
        const token = u.searchParams.get('token')
        const user = u.searchParams.get('user')
        webhookForm.pushoverApiToken = token || ''
        webhookForm.pushoverUserKey = user || ''
        // keep only base path in the url field
        webhookForm.url = u.origin + u.pathname
      } catch {
        webhookForm.pushoverApiToken = ''
        webhookForm.pushoverUserKey = ''
      }
    } else {
      webhookForm.telegramChatId = ''
    }

    // Extract Pushbullet access token if stored in query string
    try {
      if (webhook.type === 'Pushbullet' && webhook.url) {
        try {
          const u = new URL(webhook.url)
          const token = u.searchParams.get('token') || u.searchParams.get('access_token')
          webhookForm.pushbulletAccessToken = token || ''
          // keep only base path in the url field
          webhookForm.url = u.origin + u.pathname
        } catch {
          // fallback: support pushbullet://TOKEN format
          if (webhook.url.startsWith('pushbullet://')) {
            webhookForm.pushbulletAccessToken = webhook.url.substring('pushbullet://'.length)
            webhookForm.url = 'https://api.pushbullet.com/v2/pushes'
          }
        }
      }
    } catch {
      // ignore
    }
  } catch {
    webhookForm.telegramChatId = ''
  }
  resetWebhookFormErrors()
  showWebhookForm.value = true
}

const saveWebhook = async () => {
  // Validate all fields
  validateWebhookField('name')
  validateWebhookField('url')
  validateWebhookField('type')
  validateWebhookField('triggers')

  // Check if form is valid
  if (!isWebhookFormValid.value) {
    toast.error('Validation error', 'Please fix the errors before saving')
    return
  }

  savingWebhook.value = true
  try {
    // Compose final URL for Telegram when user provided token/chat id separately
    let finalUrl = webhookForm.url.trim()
    if (webhookForm.type === 'Telegram') {
      // Build from bot token field
      const token = webhookForm.telegramBotToken.trim()
      finalUrl = `https://api.telegram.org/bot${token}/sendMessage`
      if (webhookForm.telegramChatId && webhookForm.telegramChatId.trim() !== '') {
        try {
          const u = new URL(finalUrl)
          u.searchParams.set('chat_id', webhookForm.telegramChatId.trim())
          finalUrl = u.toString()
        } catch {
          const sep = finalUrl.includes('?') ? '&' : '?'
          finalUrl = `${finalUrl}${sep}chat_id=${encodeURIComponent(webhookForm.telegramChatId.trim())}`
        }
      }
    }

    // Compose final URL for Pushbullet when user provided access token
    if (webhookForm.type === 'Pushbullet') {
      if (webhookForm.pushbulletAccessToken && webhookForm.pushbulletAccessToken.trim() !== '') {
        finalUrl = `https://api.pushbullet.com/v2/pushes?token=${encodeURIComponent(webhookForm.pushbulletAccessToken.trim())}`
      }
    }

    // Compose final URL for Pushover when user provided token/user separately
    if (webhookForm.type === 'Pushover') {
      if (webhookForm.pushoverApiToken && webhookForm.pushoverUserKey) {
        finalUrl = `https://api.pushover.net/1/messages.json?token=${encodeURIComponent(
          webhookForm.pushoverApiToken.trim(),
        )}&user=${encodeURIComponent(webhookForm.pushoverUserKey.trim())}`
      }
    }

    const webhook = {
      id: webhookForm.id || generateUUID(),
      name: webhookForm.name.trim(),
      url: finalUrl,
      type: webhookForm.type as
        | 'Pushbullet'
        | 'Telegram'
        | 'Slack'
        | 'Discord'
        | 'Pushover'
        | 'NTFY'
        | 'Zapier',
      triggers: [...webhookForm.triggers],
      isEnabled: webhookForm.isEnabled,
    }

    if (editingWebhook.value) {
      // Update existing webhook
      const index = webhooks.value.findIndex((w) => w.id === webhook.id)
      if (index !== -1) {
        webhooks.value[index] = webhook
      }
      toast.success('Webhook', 'Webhook updated successfully')
    } else {
      // Add new webhook
      webhooks.value.push(webhook)
      toast.success('Webhook', 'Webhook added successfully')
    }

    // Persist webhooks to settings
    await persistWebhooks()

    closeWebhookForm()
  } catch (error) {
    errorTracking.captureException(error as Error, {
      component: 'NotificationsTab',
      operation: 'saveWebhook',
    })
    toast.error('Save failed', 'Failed to save webhook')
  } finally {
    savingWebhook.value = false
  }
}

const webhookToDelete = ref<(typeof webhooks.value)[0] | null>(null)

const confirmDeleteWebhook = (webhook: (typeof webhooks.value)[0]) => {
  webhookToDelete.value = webhook
}

const executeDeleteWebhook = async () => {
  if (!webhookToDelete.value) return
  try {
    webhooks.value = webhooks.value.filter((w) => w.id !== webhookToDelete.value!.id)
    toast.success('Webhook', 'Webhook deleted successfully')
    await persistWebhooks()
  } catch (error) {
    errorTracking.captureException(error as Error, {
      component: 'NotificationsTab',
      operation: 'executeDeleteWebhook',
    })
    toast.error('Delete failed', 'Failed to delete webhook')
    throw error
  } finally {
    webhookToDelete.value = null
  }
}

const toggleWebhook = async (webhook: (typeof webhooks.value)[0]) => {
  const index = webhooks.value.findIndex((w) => w.id === webhook.id)
  if (index !== -1) {
    const targetWebhook = webhooks.value[index]
    if (targetWebhook) {
      targetWebhook.isEnabled = !targetWebhook.isEnabled
      toast.success(
        'Webhook',
        `${webhook.name} ${targetWebhook.isEnabled ? 'enabled' : 'disabled'}`,
      )

      // Persist webhooks to settings
      await persistWebhooks()
    }
  }
}

const testWebhook = async (webhook: (typeof webhooks.value)[0]) => {
  testingWebhook.value = webhook.id
  try {
    const payload = {
      trigger: 'book-available',
      data: { message: 'Test notification from Listenarr UI' },
    }
    const response = await apiService.testNotification(
      payload.trigger,
      payload.data,
      webhook.id,
      webhook.url,
    )
    if (response && response.success) {
      toast.success(
        'Test notification',
        response.message || `Test notification sent to ${webhook.name}`,
      )
      lastWebhookTestResults[webhook.id] = 'success'
    } else {
      toast.error('Test failed', response?.message || 'Failed to send test notification')
      lastWebhookTestResults[webhook.id] = 'fail'
    }
    console.debug(
      'NotificationsTab: lastWebhookTestResults set',
      webhook.id,
      lastWebhookTestResults[webhook.id],
    )
    await nextTick()
  } catch (error) {
    errorTracking.captureException(error as Error, {
      component: 'NotificationsTab',
      operation: 'testWebhook',
    })
    const errorMessage = formatApiError(error)
    toast.error('Test failed', errorMessage)
    lastWebhookTestResults[webhook.id] = 'fail'
    console.debug(
      'NotificationsTab: lastWebhookTestResults set',
      webhook.id,
      lastWebhookTestResults[webhook.id],
    )
    await nextTick()
  } finally {
    testingWebhook.value = null
  }
}

const testWebhookConfig = async () => {
  testingWebhookConfig.value = true
  try {
    const payload = {
      trigger: 'book-available',
      data: { message: 'Test notification from Listenarr UI' },
    }
    const response = await apiService.testNotification(payload.trigger, payload.data)
    if (response && response.success) {
      toast.success(
        'Test successful',
        response.message || `Test notification sent to ${webhookForm.type}`,
      )
    } else {
      toast.error('Test failed', response?.message || 'Failed to send test notification')
    }
  } catch (error) {
    errorTracking.captureException(error as Error, {
      component: 'NotificationsTab',
      operation: 'testWebhookConfig',
    })
    const errorMessage = formatApiError(error)
    toast.error('Test failed', errorMessage)
  } finally {
    testingWebhookConfig.value = false
  }
}

// Persist webhooks to backend settings (do not mutate incoming props)
const persistWebhooks = async () => {
  // Create a shallow copy of settings and assign updated webhooks
  const current = configStore.applicationSettings ?? props.settings
  if (!current) {
    throw new Error('Application settings are unavailable')
  }
  try {
    const payload: ApplicationSettings = {
      ...current,
      webhooks: webhooks.value,
    }
    // Save from the latest committed snapshot so repeated webhook edits carry
    // the current optimistic-concurrency version. Propagate the returned snapshot
    // to the parent because the backend increments that version on every save.
    const savedSettings = await configStore.saveApplicationSettings(payload)
    emit('update:settings', savedSettings)
  } catch (error) {
    errorTracking.captureException(error as Error, {
      component: 'NotificationsTab',
      operation: 'persistWebhooks',
    })
    toast.error('Save failed', 'Failed to save webhooks to settings')
    throw error
  }
}

// Initialize webhooks from settings
onMounted(() => {
  if (props.settings?.webhooks) {
    webhooks.value = props.settings.webhooks
  }
  if (props.settings?.customScripts) {
    customScripts.value = props.settings.customScripts
  }
})

// ---------------------------------------------------------------------------
// Custom Scripts
// ---------------------------------------------------------------------------

// The channels a script can be enabled for. Matches NotificationChannel.cs; "Test" is excluded
// because it is reached through the Test button, not a checkable event.
const scriptChannelOptions: Array<{
  value: NotificationChannel
  label: string
  description: string
}> = [
  { value: 'Grab', label: 'Grab', description: 'A release was sent to a download client' },
  {
    value: 'Download',
    label: 'Import Complete',
    description: 'A download finished and was imported into the library',
  },
  {
    value: 'DownloadFailed',
    label: 'Download Failed',
    description: 'A download failed and will not be imported',
  },
  {
    value: 'BookAdded',
    label: 'Book Added',
    description: 'An audiobook was added to the library',
  },
  {
    value: 'BookAvailable',
    label: 'Book Available',
    description: 'A scan found files for a monitored book that were not imported',
  },
  {
    value: 'Rename',
    label: 'Rename',
    description: 'Files belonging to a book were relocated on disk',
  },
]

// The full Listenarr_* contract a script can read, from CustomScriptEnvironment.cs:52-115.
// There is no backend endpoint that serves this list, so it is kept in step with that file by
// hand; CustomScriptEnvironmentTests.cs is what would catch the two drifting.
const environmentVariables: Array<{ name: string; description: string }> = [
  {
    name: 'Listenarr_EventType',
    description: 'The event name: Grab, Download, DownloadFailed, BookAdded, BookAvailable, Rename, or Test',
  },
  { name: 'Listenarr_InstanceName', description: "This Listenarr instance's configured name" },
  {
    name: 'Listenarr_ApplicationUrl',
    description: "This instance's absolute base URL, or empty when not configured",
  },
  { name: 'Listenarr_Book_Id', description: 'The book id' },
  { name: 'Listenarr_Book_Title', description: 'The book title' },
  { name: 'Listenarr_Book_Asin', description: 'The book ASIN' },
  { name: 'Listenarr_Book_Authors', description: 'Author names, pipe-separated' },
  { name: 'Listenarr_Book_Narrators', description: 'Narrator names, pipe-separated' },
  { name: 'Listenarr_Book_Publisher', description: 'The publisher' },
  { name: 'Listenarr_Book_Year', description: 'The publication year' },
  { name: 'Listenarr_Release_Title', description: 'The release title' },
  { name: 'Listenarr_Release_Indexer', description: 'The indexer the release came from' },
  { name: 'Listenarr_Release_Size', description: 'The release size in bytes' },
  { name: 'Listenarr_Release_Quality', description: 'The release quality' },
  {
    name: 'Listenarr_Release_Protocol',
    description: 'The release protocol, e.g. torrent or usenet',
  },
  { name: 'Listenarr_Download_Id', description: "The download client's id for this download" },
  { name: 'Listenarr_Download_Client', description: 'The download client name' },
  { name: 'Listenarr_Download_Client_Type', description: 'The download client type' },
  {
    name: 'Listenarr_Download_ErrorMessage',
    description: 'The failure reason, set on DownloadFailed',
  },
  { name: 'Listenarr_AddedBookPaths', description: 'Paths added to the library, pipe-separated' },
  { name: 'Listenarr_SourcePath', description: 'The source path for the event, when applicable' },
  {
    name: 'Listenarr_DestinationPath',
    description: 'The destination path for the event, when applicable',
  },
  { name: 'Listenarr_Message', description: 'A human-readable summary of the event' },
  { name: 'Listenarr_Timestamp', description: 'When the event occurred, in ISO 8601' },
]

const showScriptForm = ref(false)
const editingScript = ref<CustomScriptConfiguration | null>(null)
const testingScript = ref<string | null>(null)
const testingScriptForm = ref(false)
const lastScriptTestResults = reactive<Record<string, 'success' | 'fail' | undefined>>({})
const lastScriptTestMessages = reactive<Record<string, string | undefined>>({})
const scriptFormTestMessage = ref('')
const scriptFormTestSuccess = ref(false)
const customScripts = ref<CustomScriptConfiguration[]>([])
const showEnvVars = ref(false)

const scriptForm = reactive({
  id: '',
  name: '',
  path: '',
  channels: [] as NotificationChannel[],
  isEnabled: true,
})

const scriptFormErrors = reactive({
  name: '',
  path: '',
  channels: '',
})

const savingScript = ref(false)
const scriptToDelete = ref<CustomScriptConfiguration | null>(null)

// Every field below is a UX guardrail only: the backend has no save-time validator for
// CustomScriptConfiguration (read: listenarr.domain/Configuration/CustomScriptConfiguration.cs
// and grep for its uses turns up no FluentValidation). Path existence and rootedness are checked
// only at run/test time, in CustomScriptNotification.ValidatePath, so nothing here duplicates or
// contradicts that check; it is left to report through the Test button.
const isScriptFormValid = computed(() => {
  if (!scriptForm.name.trim() || !scriptForm.path.trim()) return false
  if (scriptForm.channels.length === 0) return false
  if (scriptFormErrors.name || scriptFormErrors.path || scriptFormErrors.channels) return false
  return true
})

const validateScriptField = (field: 'name' | 'path' | 'channels') => {
  switch (field) {
    case 'name':
      scriptFormErrors.name =
        !scriptForm.name || scriptForm.name.trim().length === 0 ? 'Script name is required' : ''
      break
    case 'path':
      scriptFormErrors.path =
        !scriptForm.path || scriptForm.path.trim().length === 0
          ? 'Script path is required'
          : ''
      break
    case 'channels':
      scriptFormErrors.channels =
        scriptForm.channels.length === 0 ? 'Select at least one event' : ''
      break
  }
}

const resetScriptFormErrors = () => {
  scriptFormErrors.name = ''
  scriptFormErrors.path = ''
  scriptFormErrors.channels = ''
}

const onToggleScriptChannel = (channel: NotificationChannel, enabled: boolean) => {
  const idx = scriptForm.channels.indexOf(channel)
  if (enabled && idx === -1) scriptForm.channels.push(channel)
  if (!enabled && idx !== -1) scriptForm.channels.splice(idx, 1)
}

const openScriptForm = () => {
  editingScript.value = null
  scriptForm.id = ''
  scriptForm.name = ''
  scriptForm.path = ''
  scriptForm.channels = []
  scriptForm.isEnabled = true
  scriptFormTestMessage.value = ''
  showEnvVars.value = false
  resetScriptFormErrors()
  showScriptForm.value = true
}

const closeScriptForm = () => {
  showScriptForm.value = false
  editingScript.value = null
  scriptForm.id = ''
  scriptForm.name = ''
  scriptForm.path = ''
  scriptForm.channels = []
  scriptForm.isEnabled = true
  scriptFormTestMessage.value = ''
  showEnvVars.value = false
  resetScriptFormErrors()
}

const editScript = (script: CustomScriptConfiguration) => {
  editingScript.value = script
  scriptForm.id = script.id
  scriptForm.name = script.name
  scriptForm.path = script.path
  scriptForm.channels = [...script.channels]
  scriptForm.isEnabled = script.isEnabled
  scriptFormTestMessage.value = ''
  showEnvVars.value = false
  resetScriptFormErrors()
  showScriptForm.value = true
}

// Persist custom scripts to backend settings (do not mutate incoming props)
const persistScripts = async () => {
  const current = configStore.applicationSettings ?? props.settings
  if (!current) {
    throw new Error('Application settings are unavailable')
  }
  try {
    const payload: ApplicationSettings = {
      ...current,
      customScripts: customScripts.value,
    }
    const savedSettings = await configStore.saveApplicationSettings(payload)
    emit('update:settings', savedSettings)
  } catch (error) {
    errorTracking.captureException(error as Error, {
      component: 'NotificationsTab',
      operation: 'persistScripts',
    })
    toast.error('Save failed', 'Failed to save custom scripts to settings')
    throw error
  }
}

const saveScript = async () => {
  validateScriptField('name')
  validateScriptField('path')
  validateScriptField('channels')

  if (!isScriptFormValid.value) {
    toast.error('Validation error', 'Please fix the errors before saving')
    return
  }

  savingScript.value = true
  try {
    const script: CustomScriptConfiguration = {
      id: scriptForm.id || generateUUID(),
      name: scriptForm.name.trim(),
      path: scriptForm.path.trim(),
      channels: [...scriptForm.channels],
      isEnabled: scriptForm.isEnabled,
    }

    if (editingScript.value) {
      const index = customScripts.value.findIndex((s) => s.id === script.id)
      if (index !== -1) {
        customScripts.value[index] = script
      }
      toast.success('Custom script', 'Custom script updated successfully')
    } else {
      customScripts.value.push(script)
      toast.success('Custom script', 'Custom script added successfully')
    }

    await persistScripts()
    closeScriptForm()
  } catch (error) {
    errorTracking.captureException(error as Error, {
      component: 'NotificationsTab',
      operation: 'saveScript',
    })
    toast.error('Save failed', 'Failed to save custom script')
  } finally {
    savingScript.value = false
  }
}

const confirmDeleteScript = (script: CustomScriptConfiguration) => {
  scriptToDelete.value = script
}

const executeDeleteScript = async () => {
  if (!scriptToDelete.value) return
  try {
    customScripts.value = customScripts.value.filter((s) => s.id !== scriptToDelete.value!.id)
    toast.success('Custom script', 'Custom script deleted successfully')
    await persistScripts()
  } catch (error) {
    errorTracking.captureException(error as Error, {
      component: 'NotificationsTab',
      operation: 'executeDeleteScript',
    })
    toast.error('Delete failed', 'Failed to delete custom script')
    throw error
  } finally {
    scriptToDelete.value = null
  }
}

const toggleScript = async (script: CustomScriptConfiguration) => {
  const index = customScripts.value.findIndex((s) => s.id === script.id)
  if (index !== -1) {
    const target = customScripts.value[index]
    if (target) {
      target.isEnabled = !target.isEnabled
      toast.success('Custom script', `${script.name} ${target.isEnabled ? 'enabled' : 'disabled'}`)
      await persistScripts()
    }
  }
}

// Runs the subscriber's TestAsync against a saved configuration id. There is no way to test an
// unsaved form: CustomScriptNotification.TestAsync(configurationId) looks the script up by id in
// stored configuration (CustomScriptNotification.cs:87-88), so a script that has never been saved
// has nothing for the backend to find. The Test button is withheld for a new, unsaved script for
// that reason rather than silently testing something other than what the operator typed.
const runSubscriberTest = async (id: string): Promise<{ success: boolean; message: string }> => {
  const response = await apiService.testNotificationSubscriber('Custom Script', id)
  lastScriptTestResults[id] = response.success ? 'success' : 'fail'
  lastScriptTestMessages[id] = response.message
  return response
}

const testScript = async (script: CustomScriptConfiguration) => {
  testingScript.value = script.id
  try {
    const response = await runSubscriberTest(script.id)
    if (response.success) {
      toast.success('Test successful', response.message || `${script.name} ran successfully`)
    } else {
      toast.error('Test failed', response.message || `${script.name} did not run successfully`)
    }
  } catch (error) {
    errorTracking.captureException(error as Error, {
      component: 'NotificationsTab',
      operation: 'testScript',
    })
    lastScriptTestResults[script.id] = 'fail'
    toast.error('Test failed', formatApiError(error))
  } finally {
    testingScript.value = null
  }
}

const testExistingScript = async () => {
  if (!editingScript.value) return
  testingScriptForm.value = true
  scriptFormTestMessage.value = ''
  try {
    const response = await runSubscriberTest(editingScript.value.id)
    scriptFormTestSuccess.value = response.success
    scriptFormTestMessage.value = response.message
    if (response.success) {
      toast.success('Test successful', response.message || 'Custom script test succeeded')
    } else {
      toast.error('Test failed', response.message || 'Custom script test failed')
    }
  } catch (error) {
    errorTracking.captureException(error as Error, {
      component: 'NotificationsTab',
      operation: 'testExistingScript',
    })
    scriptFormTestSuccess.value = false
    scriptFormTestMessage.value = formatApiError(error)
    toast.error('Test failed', scriptFormTestMessage.value)
  } finally {
    testingScriptForm.value = false
  }
}

// Expose openWebhookForm/openScriptForm for parent component
defineExpose({ openWebhookForm, openScriptForm })
</script>

<style scoped>
.tab-content {
  animation: fadeIn 0.2s ease;
}

/* @keyframes fadeIn is centralized in src/assets/animations.css */

/* Section Header */
.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 2rem;
  padding-bottom: 1rem;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
}

.section-header h3 {
  margin: 0;
  color: #fff;
  font-size: 1.5rem;
  font-weight: 500;
}

.section-header .small-inline-spinner {
  margin-left: 0.5rem;
  width: 18px;
  height: 18px;
}

/* Use centralized .icon-button in src/assets/buttons.css for consistent icon buttons */
/* Empty State */
.empty-state {
  text-align: center;
  padding: 4rem 2rem;
  color: #868e96;
}

.empty-icon {
  font-size: 4rem;
  color: #868e96;
  margin-bottom: 1rem;
  width: 4rem;
  height: 4rem;
}

.empty-state h3 {
  margin: 1rem 0 0.5rem 0;
  color: #fff;
  font-size: 1.5rem;
  font-weight: 500;
}

.empty-state p {
  margin: 0.5rem 0;
  font-size: 1.05rem;
  line-height: 1.6;
  color: #adb5bd;
}

.empty-help {
  font-size: 0.95rem;
  color: #868e96;
  margin-bottom: 2rem;
}

.add-button {
  padding: 0.75rem 1.5rem;
  background: #1e88e5;
  color: white;
  border: none;
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.2s ease;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-weight: 500;
  font-size: 0.95rem;
  box-shadow: 0 2px 8px rgba(30, 136, 229, 0.3);
}

.add-button:hover {
  background: var(--brand-600);
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(var(--brand-rgb), 0.4);
}

.add-button-large {
  margin-top: 1.5rem;
  padding: 1rem 2rem;
  background: var(--brand-600);
  color: white;
  border: none;
  border-radius: var(--btn-radius);
  cursor: pointer;
  transition: all 0.2s ease;
  display: inline-flex;
  align-items: center;
  gap: 0.75rem;
  font-weight: 500;
  font-size: 1rem;
  box-shadow: 0 4px 12px rgba(var(--brand-rgb), 0.3);
}

.add-button-large:hover {
  background: var(--brand-700);
  transform: translateY(-2px);
  box-shadow: 0 6px 16px rgba(var(--brand-rgb), 0.4);
}

/* Form styles */
.form-section {
  margin-bottom: 2rem;
}

.form-section:last-child {
  margin-bottom: 0;
}

.form-section h3 {
  color: #fff;
  font-size: 1.1rem;
  margin: 0 0 1rem 0;
  padding-bottom: 0.5rem;
  border-bottom: 1px solid #444;
}

.form-group {
  margin-bottom: 1.5rem;
}

.form-group:last-child {
  margin-bottom: 0;
}

.form-group label {
  display: block;
  margin-bottom: 0.5rem;
  color: #fff;
  font-weight: 500;
}

.form-group input,
.form-group select {
  width: 100%;
  padding: 0.75rem;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 6px;
  background-color: #1a1a1a;
  color: #fff;
  font-size: 0.95rem;
  transition: border-color 0.2s;
}

.form-group input:focus,
.form-group select:focus {
  outline: none;
  border-color: var(--brand-focus);
}

.form-group small {
  display: block;
  margin-top: 0.25rem;
  color: #b3b3b3;
  font-size: 0.85rem;
}

.error-text {
  color: #ff6b6b;
  font-size: 0.85rem;
  margin-top: 0.25rem;
  display: block;
}

/* Base checkbox-group styles are provided globally via `src/styles/global.css`.
   Keep per-view small overrides below. */

/* Notifications-specific overrides */
.checkbox-group label:hover {
  border-color: var(--brand-500);
}
.checkbox-group label span {
  flex: 1;
}
.checkbox-group label small {
  color: #b3b3b3;
}
.checkbox-group input[type='checkbox']:focus-visible {
  outline: 2px solid rgba(var(--brand-rgb), 0.24);
}
/* Modal footer styling moved to shared `modals.css` */

/* Modal actions styling moved to shared `modals.css` */

/* Webhooks Grid */
.webhooks-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(450px, 1fr));
  gap: 1.5rem;
}

/* Webhook Card */
.webhook-card {
  background-color: #2a2a2a;
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 6px;
  overflow: hidden;
  transition: all 0.2s ease;
}

.webhook-card:hover {
  border-color: rgba(var(--brand-rgb), 0.3);
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(var(--brand-rgb), 0.15);
}

.webhook-card.disabled {
  opacity: 0.5;
  filter: grayscale(50%);
}

.webhook-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1.5rem;
  background-color: rgba(0, 0, 0, 0.2);
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
}

.webhook-title-row {
  display: flex;
  align-items: center;
  gap: 1rem;
  flex: 1;
  min-width: 0;
}

.webhook-info {
  min-width: 0;
}

.webhook-info h4 {
  margin: 0 0 0.5rem 0;
  color: #fff;
  font-size: 1.1rem;
  font-weight: 500;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  display: flex;
  align-items: initial;
}

.webhook-meta {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.webhook-type-badge {
  display: inline-block;
  padding: 0.25rem 0.65rem;
  background-color: rgba(77, 171, 247, 0.15);
  color: #4dabf7;
  border: 1px solid rgba(77, 171, 247, 0.3);
  border-radius: 6px;
  font-size: 0.75rem;
  font-weight: 500;
  letter-spacing: 0.5px;
}

.triggers-preview {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.trigger-badge-small {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border-radius: 6px;
  border: 1px solid;
  cursor: help;
  transition: all 0.2s ease;
}

.trigger-badge-small:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 8px rgba(0, 0, 0, 0.2);
}

.trigger-badge-small svg {
  width: 14px;
  height: 14px;
}

.trigger-badge-small.trigger-added {
  background-color: rgba(76, 175, 80, 0.15);
  color: #51cf66;
  border-color: rgba(76, 175, 80, 0.3);
}

.trigger-badge-small.trigger-downloading {
  background-color: rgba(77, 171, 247, 0.15);
  color: #4dabf7;
  border-color: rgba(77, 171, 247, 0.3);
}

.trigger-badge-small.trigger-available {
  background-color: rgba(156, 39, 176, 0.15);
  color: #b197fc;
  border-color: rgba(156, 39, 176, 0.3);
}

.trigger-badge-small.trigger-completed {
  background-color: rgba(255, 255, 255, 0.02);
  color: #fff;
  border-color: rgba(255, 255, 255, 0.12);
}

.webhook-title {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin: 0;
  line-height: 1;
}

.webhook-type-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  border-radius: 6px;
  background: rgba(255, 255, 255, 0.03);
  color: var(--color-text-secondary);
  font-size: 1rem;
  flex-shrink: 0;
}

.webhook-name {
  display: inline-block;
  line-height: 1;
}

@media (max-width: 768px) {
  .webhook-title {
    width: 100%;
  }
}

.webhook-header-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-left: 1rem;
}

/* Use centralized .icon-button in src/assets/buttons.css for consistent icon buttons */

.webhook-body {
  padding: 1.5rem;
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.webhook-url-container {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.75rem 1rem;
  background-color: rgba(0, 0, 0, 0.3);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 6px;
}

.url-icon {
  color: #4dabf7;
  font-size: 1.1rem;
}

.webhook-url {
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 0.85rem;
  color: #adb5bd;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* Triggers grid styles */
.webhook-triggers {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 0.75rem;
}

.webhook-triggers .checkbox-group {
  background: rgba(255, 255, 255, 0.02);
  border: 1px solid rgba(255, 255, 255, 0.04);
  padding: 0.6rem 0.75rem;
  border-radius: 8px;
  margin: 0;
}

.webhook-triggers .input-checkbox {
  gap: 0.75rem;
  align-items: center;
}

/* Layout the label contents with icon on the left and stacked text */
.webhook-triggers .checkbox-label {
  display: flex;
  flex-direction: row;
  align-items: center;
  gap: 0.75rem;
}

.webhook-triggers .checkbox-text {
  display: flex;
  flex-direction: row;
  align-items: center;
  gap: 0.5rem;
  line-height: 1;
}

.webhook-triggers .trigger-icon {
  color: var(--color-text-secondary);
  font-size: 1.05rem;
  display: inline-flex;
  align-items: center;
  justify-content: center;
}

.webhook-triggers .checkbox-text small {
  font-size: 0.82rem;
  color: var(--color-text-secondary);
  margin: 0 0 0 0.25rem;
}

/* Mobile Responsive */
@media (max-width: 768px) {
  .webhooks-grid {
    grid-template-columns: 1fr;
  }

  .webhook-header {
    flex-direction: column;
    align-items: flex-start;
    gap: 1rem;
  }

  .webhook-header-actions {
    width: 100%;
    justify-content: space-between;
    margin-left: 0;
  }
}

/* Override to ensure the checkbox uses our custom component styling inside teleported modal */
.webhook-modal .input-checkbox {
  display: flex;
  align-items: flex-start;
  gap: 0.75rem;
  cursor: pointer !important;
}
.webhook-modal .input-checkbox input {
  position: absolute;
  opacity: 0;
  width: 0;
  height: 0;
}
.webhook-modal .checkbox-box {
  width: 18px;
  height: 18px;
  border-radius: 3px;
  margin-top: 0;
  flex-shrink: 0;
}
.webhook-modal .checkbox-label > :first-child {
  height: 18px;
  display: flex;
  align-items: center;
}

/* Spin animation for loading icons */
.ph-spin {
  animation: spin 1s linear infinite;
}

/* @keyframes spin is centralized in src/assets/animations.css */

/* Custom Scripts section (mirrors the webhook grid/card layout above) */
.custom-scripts-header {
  margin-top: 3rem;
}

.scripts-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(450px, 1fr));
  gap: 1.5rem;
}

.script-card {
  background-color: #2a2a2a;
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 6px;
  overflow: hidden;
  transition: all 0.2s ease;
}

.script-card:hover {
  border-color: rgba(var(--brand-rgb), 0.3);
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(var(--brand-rgb), 0.15);
}

.script-card.disabled {
  opacity: 0.5;
  filter: grayscale(50%);
}

.script-channel-count {
  font-size: 0.85rem;
  color: var(--color-text-secondary);
}

.script-test-message {
  margin: 0;
  font-size: 0.85rem;
  padding: 0.5rem 0.75rem;
  border-radius: 6px;
  background: rgba(255, 255, 255, 0.03);
}

.script-test-message.success {
  color: #51cf66;
}

.script-test-message.fail {
  color: #ff6b6b;
}

/* Warning banner explaining what the script path does, shown in the Add/Edit modal */
.script-warning {
  display: flex;
  align-items: flex-start;
  gap: 0.75rem;
  padding: 0.85rem 1rem;
  margin-bottom: 1.5rem;
  background: rgba(255, 193, 7, 0.08);
  border: 1px solid rgba(255, 193, 7, 0.3);
  border-radius: 6px;
  color: #ffc107;
}

.script-warning svg {
  flex-shrink: 0;
  margin-top: 0.15rem;
}

.script-warning p {
  margin: 0;
  color: #e0c15c;
  font-size: 0.9rem;
  line-height: 1.5;
}

.script-warning code {
  font-family: 'Consolas', 'Monaco', monospace;
  color: #ffc107;
}

/* Discoverable environment variables list */
.env-vars-toggle {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  background: none;
  border: none;
  color: var(--brand-500, #4dabf7);
  cursor: pointer;
  font-size: 0.9rem;
  padding: 0.25rem 0;
}

.env-vars-toggle:hover {
  text-decoration: underline;
}

.env-vars-list {
  margin-top: 0.75rem;
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  max-height: 320px;
  overflow-y: auto;
  padding: 0.75rem;
  background: rgba(0, 0, 0, 0.2);
  border-radius: 6px;
}

.env-var-row {
  display: grid;
  grid-template-columns: minmax(180px, 220px) 1fr;
  gap: 0.75rem;
  align-items: baseline;
}

.env-var-row code {
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 0.8rem;
  color: #4dabf7;
}

.env-var-row span {
  font-size: 0.85rem;
  color: var(--color-text-secondary);
}

@media (max-width: 768px) {
  .scripts-grid {
    grid-template-columns: 1fr;
  }

  .env-var-row {
    grid-template-columns: 1fr;
  }
}
</style>
