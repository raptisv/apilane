<script setup lang="ts">
import { CircleAlertIcon, CopyIcon, InfoIcon, Loader2Icon, TriangleAlertIcon } from '@lucide/vue'
import { computed, nextTick, reactive, ref, useId, useTemplateRef, watch } from 'vue'
import { useRouter } from 'vue-router'
import DatabaseTypeFields from '@/components/DatabaseTypeFields.vue'
import ErrorState from '@/components/ErrorState.vue'
import FormField from '@/components/FormField.vue'
import LoadingState from '@/components/LoadingState.vue'
import NoServersState from '@/components/NoServersState.vue'
import PageHeader from '@/components/PageHeader.vue'
import ServerSelect from '@/components/ServerSelect.vue'
import SwitchField from '@/components/SwitchField.vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { useApplication } from '@/composables/useApplication'
import { useApplications } from '@/composables/useApplications'
import { useAsync } from '@/composables/useAsync'
import { useMutation } from '@/composables/useMutation'
import { useServerChoice } from '@/composables/useServerChoice'
import { api, unwrap } from '@/lib/api'
import type { Schemas } from '@/lib/api'
import { needsConnectionString } from '@/lib/applications'
import { cloneableEntities, entitiesToSend } from '@/lib/clone'
import { formErrors } from '@/lib/forms'

// The clone form: where the copy goes (server, database) and whether the records are copied too.
// Starting a clone answers at once; the work is followed on CloneProgressPage.
const router = useRouter()
const { application } = useApplication()
const { reload: reloadApplications } = useApplications()

// AppLayout gives every application a fresh screen, so the token is fixed for the life of this one.
const appToken = application.value.Token

const { servers, serverId, error: serversError, loading: serversLoading, reload: reloadServers } = useServerChoice()

// The form starts on the server of the application being cloned.
watch(servers, (list) => {
  if (list.some((server) => server.ID === application.value.Server.ID)) {
    serverId.value = application.value.Server.ID
  }
})

const {
  data: entityList,
  error: entitiesError,
  loading: entitiesLoading,
  reload: reloadEntities,
} = useAsync(() => unwrap(api.GET('/api/v1/applications/{appToken}/entities', { params: { path: { appToken } } })))

const entityNames = computed(() => cloneableEntities((entityList.value?.Data ?? []).map((entity) => entity.Name)))

// The entities whose records are copied. All of them to start with.
const selected = ref<string[]>([])
watch(entityNames, (names) => (selected.value = [...names]))

function toggleEntity(name: string, checked: boolean): void {
  selected.value = entityNames.value.filter((other) => (other === name ? checked : selected.value.includes(other)))
}

// The connection string lives only here: it is gone when the page is left.
const form = reactive({
  DatabaseType: 'SQLLite',
  ConnectionString: '',
  CloneData: false,
})

let started: Schemas['CloneStartedResponse'] | undefined

const start = useMutation(async (ServerID: number) => {
  started = await unwrap(
    api.POST('/api/v1/applications/{appToken}/clones', {
      params: { path: { appToken } },
      body: {
        ServerID,
        DatabaseType: form.DatabaseType,
        ConnectionString: needsConnectionString(form.DatabaseType) ? form.ConnectionString : null,
        CloneData: form.CloneData,
        Entities: entitiesToSend(form.CloneData, selected.value),
      },
    }),
  )
})

// Checked here: without a choice there is no ServerID to send.
const serverError = ref<string>()
watch(serverId, () => (serverError.value = undefined))

// Checked here too: the API reads an empty list as 'every entity', which is not what an empty
// selection says.
const selectionError = ref<string>()
watch([selected, () => form.CloneData], () => (selectionError.value = undefined))

// True from success until the screen has been left, so the button cannot start a second clone.
const leaving = ref(false)

const errors = computed(() => {
  const result = formErrors(start.error.value, ['ServerID', 'DatabaseType', 'ConnectionString', 'CloneData'])

  if (serverError.value) {
    result.fields.ServerID = serverError.value
  }

  return result
})

const formEl = useTemplateRef<HTMLFormElement>('formEl')
const entitiesId = useId()

async function submit(): Promise<void> {
  // The failure of an earlier attempt goes, also when the checks below stop this one.
  start.reset()
  serverError.value = serverId.value === undefined ? 'Select a server.' : undefined
  selectionError.value =
    form.CloneData && entityNames.value.length > 0 && selected.value.length === 0
      ? "Select at least one entity, or switch off 'Clone data'."
      : undefined

  if (serverId.value === undefined || selectionError.value || !(await start.run(serverId.value)) || !started) {
    // Move to the first rejected field, so it is read out and scrolled into view.
    await nextTick()
    formEl.value?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
    return
  }

  leaving.value = true
  form.ConnectionString = ''
  // The clone is listed from this moment on: the sidebar switcher and the applications page learn about it.
  void reloadApplications()
  await router.push({ name: 'app-clone-progress', params: { appToken, operationId: started.OperationId } })
}

// What a clone is.
const notes = [
  'The cloned application is going to be identical to the original.',
  'Entities, properties, custom endpoints and security will be identical.',
  'If "Clone data" is selected, data will also be copied. Otherwise the new database will be empty.',
  'You can select which entities to clone data for. By default, all entities are selected.',
  'A new application token will be automatically generated.',
  'The applications (source and clone) will not be connected to each other.',
  'Any change to the schema or data of one application will not be mirrored to the other.',
  'Data cloning runs in the background. You will see a progress bar with the estimated time remaining.',
]
</script>

<template>
  <PageHeader
    title="Clone application"
    :description="`A copy of ${application.Name} on the server and database you choose.`"
    class="wrap-anywhere"
  />

  <LoadingState v-if="serversLoading || entitiesLoading" label="Loading servers and entities" :rows="4" />

  <ErrorState v-else-if="serversError" :error="serversError" @retry="reloadServers" />

  <ErrorState v-else-if="entitiesError" :error="entitiesError" @retry="reloadEntities" />

  <NoServersState v-else-if="servers.length === 0" />

  <div v-else class="grid max-w-2xl gap-6">
    <div class="flex gap-2.5 rounded-lg border bg-muted/40 p-3 text-sm">
      <InfoIcon class="mt-0.5 size-4 shrink-0 text-link" aria-hidden="true" />
      <ul class="grid list-disc gap-1 pl-4">
        <li v-for="note in notes" :key="note">{{ note }}</li>
      </ul>
    </div>

    <div class="flex gap-2.5 rounded-lg border border-warning/40 bg-warning/10 p-3 text-sm text-warning">
      <TriangleAlertIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      <p>
        If you change storage provider (for example from SQLite to MySQL) you may run into compatibility issues, because
        the databases handle data types differently. SQLite is flexible with data types and converts them implicitly,
        while MySQL enforces stricter type definitions. Review and adapt your schema and data to make the move go
        smoothly.
      </p>
    </div>

    <form ref="formEl" class="grid gap-4 rounded-lg border bg-card p-4" novalidate @submit.prevent="submit">
      <FormField v-slot="{ field }" label="Server" help="The server the clone is created on." :error="errors.fields.ServerID">
        <ServerSelect v-model="serverId" v-bind="field" :servers="servers" />
      </FormField>

      <DatabaseTypeFields
        v-model:database-type="form.DatabaseType"
        v-model:connection-string="form.ConnectionString"
        :database-type-error="errors.fields.DatabaseType"
        :connection-string-error="errors.fields.ConnectionString"
        check-note="It is checked when the clone starts: a wrong value shows as a failed clone on the progress page."
      />

      <SwitchField
        v-model="form.CloneData"
        label="Clone data"
        help="Copies the records too. When off, only the schema is cloned and the new database is empty."
        :error="errors.fields.CloneData"
      />

      <div v-if="form.CloneData" class="grid gap-2">
        <p :id="entitiesId" class="text-sm font-medium">Entities to clone data for</p>

        <div class="flex flex-wrap items-center gap-2">
          <Button type="button" variant="outline" size="sm" @click="selected = [...entityNames]">Select all</Button>
          <Button type="button" variant="outline" size="sm" @click="selected = []">Deselect all</Button>
          <span class="text-xs text-muted-foreground" role="status">
            {{ selected.length }} of {{ entityNames.length }} selected
          </span>
        </div>

        <!-- Clipped sideways, with room around the boxes: their enlarged click area and focus ring stay inside. -->
        <div
          role="group"
          :aria-labelledby="entitiesId"
          class="grid gap-3 overflow-x-hidden rounded-lg border px-3.5 py-3 sm:grid-cols-2"
        >
          <div v-for="(name, index) in entityNames" :key="name" class="flex min-w-0 items-center gap-2.5">
            <Checkbox
              :id="`${entitiesId}-${index}`"
              :model-value="selected.includes(name)"
              @update:model-value="(checked) => toggleEntity(name, checked === true)"
            />
            <label :for="`${entitiesId}-${index}`" class="min-w-0 font-mono text-sm wrap-anywhere">{{ name }}</label>
          </div>
        </div>

        <p class="text-xs text-muted-foreground">
          Every entity is created in the clone. Only the selected ones get their records. Files are never copied and are
          not listed.
        </p>
        <p v-if="selectionError" role="alert" class="text-xs text-destructive">{{ selectionError }}</p>
      </div>

      <Alert v-if="errors.message" variant="destructive">
        <CircleAlertIcon />
        <AlertDescription>{{ errors.message }}</AlertDescription>
      </Alert>

      <div class="flex justify-end gap-2">
        <Button as-child variant="outline">
          <RouterLink :to="{ name: 'apps' }">Cancel</RouterLink>
        </Button>
        <Button type="submit" :disabled="start.pending.value || leaving">
          <Loader2Icon v-if="start.pending.value || leaving" class="animate-spin" />
          <CopyIcon v-else />
          Clone
        </Button>
      </div>
    </form>
  </div>
</template>
