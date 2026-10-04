<script setup lang="ts">
import { ref } from 'vue'
import ErrorState from '@/components/ErrorState.vue'
import LoadingState from '@/components/LoadingState.vue'
import PageHeader from '@/components/PageHeader.vue'
import { useApplication } from '@/composables/useApplication'
import { useAsync } from '@/composables/useAsync'
import { api, unwrap } from '@/lib/api'
import type { Schemas } from '@/lib/api'
import SecurityRulesEditor from './SecurityRulesEditor.vue'
import SecuritySettingsForm from './SecuritySettingsForm.vue'

// The security of an application: the access settings (one form, its own Save) and the rules of
// who may call what (edited per item, saved together). ?item= selects the item of the rule grid,
// ?view=tree and ?view=matrix open the read-only views.
const { application } = useApplication()

// AppLayout gives every application a fresh screen, so the token is fixed for the life of this one.
const appToken = application.value.Token

const { data, error, loading, reload } = useAsync(() =>
  unwrap(api.GET('/api/v1/applications/{appToken}/security', { params: { path: { appToken } } })),
)

// The two parts save separately, but only one of them asks before the screen is left: the settings
// form stands back while the rules editor has unsaved rules (its bar asks for the whole screen).
const rulesDirty = ref(false)

function settingsSaved(settings: Schemas['SecuritySettingsResponse']): void {
  if (data.value) {
    data.value = { ...data.value, Settings: settings }
  }
}

function rulesSaved(rules: Schemas['SecurityRuleResponse'][]): void {
  if (data.value) {
    data.value = { ...data.value, Rules: rules }
  }
}
</script>

<template>
  <PageHeader
    title="Security"
    description="How the users of this application sign in, which addresses may call its API, and which roles may call what."
  />

  <LoadingState v-if="loading" label="Loading the security settings" :rows="6" />

  <ErrorState v-else-if="error" :error="error" @retry="reload" />

  <div v-else-if="data" class="grid gap-10">
    <SecuritySettingsForm
      :app-token="appToken"
      :settings="data.Settings"
      :links="data.ForgotPasswordLinks"
      :rules-dirty="rulesDirty"
      @saved="settingsSaved"
    />

    <SecurityRulesEditor
      :app-token="appToken"
      :items="data.Items"
      :roles="data.Roles"
      :roles-available="data.RolesAvailable"
      :saved="data.Rules"
      @saved="rulesSaved"
      @dirty="rulesDirty = $event"
    />
  </div>
</template>
