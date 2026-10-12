<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ErrorState from '@/components/ErrorState.vue'
import LoadingState from '@/components/LoadingState.vue'
import PageHeader from '@/components/PageHeader.vue'
import SecurityHelp from '@/components/help/SecurityHelp.vue'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { useApplication } from '@/composables/useApplication'
import { useAsync } from '@/composables/useAsync'
import { api, unwrap } from '@/lib/api'
import type { Schemas } from '@/lib/api'
import SecurityRulesEditor from './SecurityRulesEditor.vue'
import SecuritySettingsForm from './SecuritySettingsForm.vue'

// The security of an application, in two tabs so that neither is far down the page: the access
// rules (who may call what, edited per item and saved together) and the access settings (one form,
// its own Save). ?tab=settings opens the settings; the rules are the default, and ?item= selects
// the item of the rule grid and ?view=tree and ?view=matrix open the read-only views. Both tabs stay
// mounted, so what is typed or switched in one is still there after a look at the other.
const { application } = useApplication()
const route = useRoute()
const router = useRouter()

const tab = computed({
  get: () => (route.query.tab === 'settings' ? 'settings' : 'rules'),
  set: (value: string | number) => {
    const query = { ...route.query }

    if (value === 'settings') {
      query.tab = 'settings'
    } else {
      delete query.tab
    }

    void router.replace({ query })
  },
})

// AppLayout gives every application a fresh screen, so the token is fixed for the life of this one.
const appToken = application.value.Token

const { data, error, loading, reload } = useAsync(() =>
  unwrap(api.GET('/api/v1/applications/{appToken}/security', { params: { path: { appToken } } })),
)

// The two parts save separately, but only one of them asks before the screen is left: the settings
// form stands back while the rules editor has unsaved rules (its bar asks for the whole screen).
const rulesDirty = ref(false)
const settingsDirty = ref(false)

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
  >
    <SecurityHelp />
  </PageHeader>

  <LoadingState v-if="loading" label="Loading the security settings" :rows="6" />

  <ErrorState v-else-if="error" :error="error" @retry="reload" />

  <Tabs v-else-if="data" v-model="tab" class="gap-5">
    <TabsList aria-label="Parts of the security">
      <TabsTrigger value="rules" class="px-3">
        Access rules
        <span v-if="rulesDirty" class="size-2 rounded-full bg-warning" aria-hidden="true" />
        <span v-if="rulesDirty" class="sr-only">(unsaved changes)</span>
      </TabsTrigger>
      <TabsTrigger value="settings" class="px-3">
        Access settings
        <span v-if="settingsDirty" class="size-2 rounded-full bg-warning" aria-hidden="true" />
        <span v-if="settingsDirty" class="sr-only">(unsaved changes)</span>
      </TabsTrigger>
    </TabsList>

    <TabsContent value="rules" force-mount class="data-[state=inactive]:hidden">
      <SecurityRulesEditor
        :app-token="appToken"
        :items="data.Items"
        :roles="data.Roles"
        :roles-available="data.RolesAvailable"
        :saved="data.Rules"
        @saved="rulesSaved"
        @dirty="rulesDirty = $event"
      />
    </TabsContent>

    <TabsContent value="settings" force-mount class="data-[state=inactive]:hidden">
      <SecuritySettingsForm
        :app-token="appToken"
        :settings="data.Settings"
        :links="data.ForgotPasswordLinks"
        :rules-dirty="rulesDirty"
        @saved="settingsSaved"
        @dirty="settingsDirty = $event"
      />
    </TabsContent>
  </Tabs>
</template>
