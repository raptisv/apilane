<script setup lang="ts">
import { CircleAlertIcon, Loader2Icon } from '@lucide/vue'
import { computed, nextTick, reactive, ref, useTemplateRef, watch } from 'vue'
import { useRouter } from 'vue-router'
import DatabaseTypeFields from '@/components/DatabaseTypeFields.vue'
import ErrorState from '@/components/ErrorState.vue'
import FormField from '@/components/FormField.vue'
import LoadingState from '@/components/LoadingState.vue'
import NoServersState from '@/components/NoServersState.vue'
import PageHeader from '@/components/PageHeader.vue'
import ServerSelect from '@/components/ServerSelect.vue'
import CreateApplicationHelp from '@/components/help/CreateApplicationHelp.vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useApplications } from '@/composables/useApplications'
import { useMutation } from '@/composables/useMutation'
import { useServerChoice } from '@/composables/useServerChoice'
import { api, unwrap } from '@/lib/api'
import { needsConnectionString } from '@/lib/applications'
import type { Application } from '@/lib/applications'
import { formErrors } from '@/lib/forms'
import * as toast from '@/lib/toast'

const router = useRouter()
const { reload: reloadApplications } = useApplications()
const { servers, serverId, error, loading, reload } = useServerChoice()

// The connection string lives only here: it is gone when the page is left.
const form = reactive({
  Name: '',
  DatabaseType: 'SQLLite',
  ConnectionString: '',
  DifferentiationEntity: '',
})

let created: Application | undefined

const create = useMutation(async (ServerID: number) => {
  created = await unwrap(
    api.POST('/api/v1/applications', {
      body: {
        Name: form.Name,
        ServerID,
        DatabaseType: form.DatabaseType,
        ConnectionString: needsConnectionString(form.DatabaseType) ? form.ConnectionString : null,
        DifferentiationEntity: form.DifferentiationEntity.trim() || null,
      },
    }),
  )
})

// Checked here: without a choice there is no ServerID to send.
const serverError = ref<string>()
// The message goes once a server is picked.
watch(serverId, () => (serverError.value = undefined))
// True from success until the screen has been left, so the button cannot create a second application.
const leaving = ref(false)

const errors = computed(() => {
  const result = formErrors(create.error.value, ['Name', 'ServerID', 'DatabaseType', 'ConnectionString', 'DifferentiationEntity'])

  if (serverError.value) {
    result.fields.ServerID = serverError.value
  }

  return result
})

const formEl = useTemplateRef<HTMLFormElement>('formEl')

// The form is the whole screen, so typing can start as soon as it shows.
watch(formEl, (el) => el?.querySelector('input')?.focus())

async function submit(): Promise<void> {
  serverError.value = serverId.value === undefined ? 'Select a server.' : undefined

  if (serverId.value === undefined || !(await create.run(serverId.value)) || !created) {
    // Move to the first rejected field, so it is read out and scrolled into view.
    await nextTick()
    formEl.value?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
    return
  }

  leaving.value = true
  form.ConnectionString = ''
  toast.success(`Application ${created.Name} created.`)
  // The sidebar switcher and the applications page learn about the new application.
  void reloadApplications()
  await router.push({ name: 'app-entities', params: { appToken: created.Token } })
}
</script>

<template>
  <PageHeader title="New application" description="An application is the backend of your client application.">
    <CreateApplicationHelp />
  </PageHeader>

  <LoadingState v-if="loading" label="Loading servers" :rows="4" />

  <ErrorState v-else-if="error" :error="error" @retry="reload" />

  <NoServersState v-else-if="servers.length === 0" />

  <div v-else class="grid max-w-2xl gap-6">
    <form ref="formEl" class="grid gap-4 rounded-lg border bg-card p-4" novalidate @submit.prevent="submit">
      <FormField v-slot="{ field }" label="Name" help="4 to 100 characters. You can change it later." :error="errors.fields.Name">
        <Input v-model="form.Name" v-bind="field" autocomplete="off" maxlength="100" />
      </FormField>

      <FormField v-slot="{ field }" label="Server" help="The server cannot be changed later." :error="errors.fields.ServerID">
        <ServerSelect v-model="serverId" v-bind="field" :servers="servers" />
      </FormField>

      <DatabaseTypeFields
        v-model:database-type="form.DatabaseType"
        v-model:connection-string="form.ConnectionString"
        :database-type-error="errors.fields.DatabaseType"
        :connection-string-error="errors.fields.ConnectionString"
      />

      <FormField
        v-slot="{ field }"
        label="Differentiation entity (optional)"
        help="For example Company. It cannot be changed or removed later."
        :error="errors.fields.DifferentiationEntity"
      >
        <Input v-model="form.DifferentiationEntity" v-bind="field" autocomplete="off" spellcheck="false" maxlength="40" />
      </FormField>

      <Alert v-if="errors.message" variant="destructive">
        <CircleAlertIcon />
        <AlertDescription>{{ errors.message }}</AlertDescription>
      </Alert>

      <div class="flex justify-end gap-2">
        <Button as-child variant="outline">
          <RouterLink :to="{ name: 'apps' }">Cancel</RouterLink>
        </Button>
        <Button type="submit" :disabled="create.pending.value || leaving">
          <Loader2Icon v-if="create.pending.value || leaving" class="animate-spin" />
          Save
        </Button>
      </div>
    </form>
  </div>
</template>
