<script setup lang="ts">
import { CircleAlertIcon, InfoIcon, Loader2Icon } from '@lucide/vue'
import { computed, nextTick, reactive, ref, useTemplateRef, watch } from 'vue'
import { useRouter } from 'vue-router'
import DatabaseTypeFields from '@/components/DatabaseTypeFields.vue'
import ErrorState from '@/components/ErrorState.vue'
import FileInput from '@/components/FileInput.vue'
import FormField from '@/components/FormField.vue'
import LoadingState from '@/components/LoadingState.vue'
import NoServersState from '@/components/NoServersState.vue'
import PageHeader from '@/components/PageHeader.vue'
import ServerSelect from '@/components/ServerSelect.vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { useApplications } from '@/composables/useApplications'
import { useMutation } from '@/composables/useMutation'
import { useServerChoice } from '@/composables/useServerChoice'
import { api, unwrap } from '@/lib/api'
import { needsConnectionString } from '@/lib/applications'
import type { Application } from '@/lib/applications'
import { toFormData } from '@/lib/formData'
import { formErrors } from '@/lib/forms'
import * as toast from '@/lib/toast'

const router = useRouter()
const { reload: reloadApplications } = useApplications()
const { servers, serverId, error, loading, reload } = useServerChoice()

const file = ref<File>()
// The connection string lives only here: it is gone when the page is left.
const form = reactive({ DatabaseType: 'SQLLite', ConnectionString: '' })

let imported: Application | undefined

const run = useMutation(async (upload: File, ServerID: number) => {
  imported = await unwrap(
    api.POST('/api/v1/applications/import', {
      body: {
        // The contract describes an uploaded file as a string; toFormData sends the file itself.
        File: upload as unknown as string,
        ServerID,
        DatabaseType: form.DatabaseType,
        ConnectionString: needsConnectionString(form.DatabaseType) ? form.ConnectionString : undefined,
      },
      bodySerializer: toFormData,
    }),
  )
})

// Checked here: without them there is nothing to send.
const missing = reactive<{ File?: string; ServerID?: string }>({})
// A message goes once its value is picked.
watch(file, () => (missing.File = undefined))
watch(serverId, () => (missing.ServerID = undefined))
// True from success until the screen has been left, so the button cannot import twice.
const leaving = ref(false)

const errors = computed(() => {
  const result = formErrors(run.error.value, ['File', 'ServerID', 'DatabaseType', 'ConnectionString'])

  if (missing.File) {
    result.fields.File = missing.File
  }

  if (missing.ServerID) {
    result.fields.ServerID = missing.ServerID
  }

  return result
})

const formEl = useTemplateRef<HTMLFormElement>('formEl')

async function submit(): Promise<void> {
  missing.File = file.value ? undefined : 'Select a file.'
  missing.ServerID = serverId.value === undefined ? 'Select a server.' : undefined

  // The file is read here and its copy is sent: a browser refuses to upload a picked file that
  // was edited on disk since, and that failure looks like the Portal being unreachable.
  const picked = file.value
  const bytes = await picked?.arrayBuffer().catch(() => undefined)

  if (picked && !bytes) {
    missing.File = 'The file changed after it was selected. Select it again.'
  }

  if (
    !picked ||
    !bytes ||
    serverId.value === undefined ||
    !(await run.run(new File([bytes], picked.name, { type: picked.type }), serverId.value)) ||
    !imported
  ) {
    // Move to the first rejected field, so it is read out and scrolled into view.
    await nextTick()
    formEl.value?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
    return
  }

  leaving.value = true
  form.ConnectionString = ''
  toast.success(`Application ${imported.Name} imported.`)
  // The sidebar switcher and the applications page learn about the new application.
  void reloadApplications()
  await router.push({ name: 'app-entities', params: { appToken: imported.Token } })
}
</script>

<template>
  <PageHeader title="Import application" description="Creates an application from the export of another one." />

  <LoadingState v-if="loading" label="Loading servers" :rows="4" />

  <ErrorState v-else-if="error" :error="error" @retry="reload" />

  <NoServersState v-else-if="servers.length === 0" />

  <form
    v-else
    ref="formEl"
    class="grid max-w-2xl gap-4 rounded-lg border bg-card p-4"
    novalidate
    @submit.prevent="submit"
  >
    <FormField v-slot="{ field }" label="Server" :error="errors.fields.ServerID">
      <ServerSelect v-model="serverId" v-bind="field" :servers="servers" />
    </FormField>

    <DatabaseTypeFields
      v-model:database-type="form.DatabaseType"
      v-model:connection-string="form.ConnectionString"
      :database-type-error="errors.fields.DatabaseType"
      :connection-string-error="errors.fields.ConnectionString"
    />

    <FormField v-slot="{ field }" label="File" :error="errors.fields.File">
      <FileInput v-model="file" v-bind="field" accept=".json,application/json" />
    </FormField>

    <div class="flex gap-2.5 rounded-lg border bg-muted/40 p-3 text-sm">
      <InfoIcon class="mt-0.5 size-4 shrink-0 text-link" aria-hidden="true" />
      <p>
        Upload <strong>application.json</strong> file from any exported application. The imported application will be
        generated using the same <strong>Token</strong>. If you wish to change the token, make sure to edit the json
        file before uploading.
      </p>
    </div>

    <Alert v-if="errors.message" variant="destructive">
      <CircleAlertIcon />
      <AlertDescription>{{ errors.message }}</AlertDescription>
    </Alert>

    <div class="flex justify-end gap-2">
      <Button as-child variant="outline">
        <RouterLink :to="{ name: 'apps' }">Cancel</RouterLink>
      </Button>
      <Button type="submit" :disabled="run.pending.value || leaving">
        <Loader2Icon v-if="run.pending.value || leaving" class="animate-spin" />
        Save
      </Button>
    </div>
  </form>
</template>
