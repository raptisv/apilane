<script setup lang="ts">
import { CircleAlertIcon, Loader2Icon, PowerIcon, Trash2Icon, WrenchIcon } from '@lucide/vue'
import { computed, nextTick, reactive, ref, useTemplateRef } from 'vue'
import { useRouter } from 'vue-router'
import ApplicationStatusDialog from '@/components/ApplicationStatusDialog.vue'
import ConsequenceList from '@/components/ConsequenceList.vue'
import DatabaseTypeFields from '@/components/DatabaseTypeFields.vue'
import DeleteApplicationDialog from '@/components/DeleteApplicationDialog.vue'
import FormField from '@/components/FormField.vue'
import PageHeader from '@/components/PageHeader.vue'
import RebuildApplicationDialog from '@/components/RebuildApplicationDialog.vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { useApplication } from '@/composables/useApplication'
import { useApplications } from '@/composables/useApplications'
import { useMutation } from '@/composables/useMutation'
import { api, unwrap } from '@/lib/api'
import { connectionStringToSend, deleteConsequences, offlineWarning, rebuildConsequences, statusChange } from '@/lib/applicationActions'
import { formErrors } from '@/lib/forms'
import * as toast from '@/lib/toast'

const router = useRouter()
const { application, reload } = useApplication()
const { reload: reloadApplications } = useApplications()

// AppLayout gives every application a fresh screen, so the token is fixed for the life of this one.
const appToken = application.value.Token

// General. The connection string lives only in this form: the box starts empty (the API never
// sends it) and is emptied again after a save.
const form = reactive({ Name: application.value.Name, ConnectionString: '' })

const save = useMutation(() =>
  unwrap(
    api.PUT('/api/v1/applications/{appToken}', {
      params: { path: { appToken } },
      body: {
        Name: form.Name,
        ConnectionString: connectionStringToSend(application.value.DatabaseType, form.ConnectionString),
      },
    }),
  ),
)

const errors = computed(() => formErrors(save.error.value, ['Name', 'ConnectionString']))
const formEl = useTemplateRef<HTMLFormElement>('formEl')

async function submit(): Promise<void> {
  if (!(await save.run())) {
    // Move to the first rejected field, so it is read out and scrolled into view.
    await nextTick()
    formEl.value?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
    return
  }

  form.ConnectionString = ''
  toast.success('Application saved.')
  // The new name shows in the breadcrumb, the sidebar switcher and the applications page.
  void reload()
  void reloadApplications()
}

// Status, rebuild, delete: the same dialogs as on the cards of the applications page.
const statusOpen = ref(false)
const rebuildOpen = ref(false)
const deleteOpen = ref(false)

const statusLabel = computed(() => statusChange(application.value.Name, application.value.Online).label)

function onDeleted(): void {
  void router.push({ name: 'apps' })
}

const sectionClass = 'grid content-start gap-4 rounded-lg border bg-card p-4'
const noteClass = 'text-sm text-muted-foreground'
</script>

<template>
  <PageHeader title="Settings" description="The name, connection string and status of this application." />

  <div class="grid max-w-2xl gap-6">
    <form ref="formEl" :class="sectionClass" aria-labelledby="general-title" novalidate @submit.prevent="submit">
      <h2 id="general-title" class="text-sm font-semibold">General</h2>

      <FormField v-slot="{ field }" label="Name" help="4 to 100 characters." :error="errors.fields.Name">
        <Input v-model="form.Name" v-bind="field" autocomplete="off" maxlength="100" />
      </FormField>

      <FormField v-slot="{ field }" label="Server" help="The server cannot be changed.">
        <Input v-bind="field" :model-value="application.Server.Name" disabled />
      </FormField>

      <DatabaseTypeFields
        v-model:connection-string="form.ConnectionString"
        :database-type="application.DatabaseType"
        :connection-string-error="errors.fields.ConnectionString"
        existing
        :has-connection-string="application.HasConnectionString"
      />

      <Alert v-if="errors.message" variant="destructive" role="alert">
        <CircleAlertIcon />
        <AlertDescription>{{ errors.message }}</AlertDescription>
      </Alert>

      <div class="flex justify-end">
        <Button type="submit" :disabled="save.pending.value">
          <Loader2Icon v-if="save.pending.value" class="animate-spin" />
          Save
        </Button>
      </div>
    </form>

    <section :class="sectionClass" aria-labelledby="status-title">
      <div class="flex flex-wrap items-center gap-2.5">
        <h2 id="status-title" class="text-sm font-semibold">Status</h2>
        <Badge v-if="application.Online" variant="outline" class="border-success/40 text-success">Online</Badge>
        <Badge v-else variant="destructive">Offline</Badge>
      </div>

      <p :class="noteClass">
        <template v-if="application.Online">
          The application is available to the users. Taken offline, it will not be available to the users until the
          status is set back online: the API server refuses every call for it.
        </template>
        <template v-else>{{ offlineWarning }}</template>
      </p>

      <div>
        <Button variant="outline" @click="statusOpen = true">
          <PowerIcon />
          {{ statusLabel }}
        </Button>
      </div>
    </section>

    <section class="grid content-start gap-4 rounded-lg border border-destructive/40 bg-card p-4" aria-labelledby="danger-title">
      <h2 id="danger-title" class="text-sm font-semibold text-destructive">Danger zone</h2>

      <div class="grid gap-3 sm:grid-cols-[1fr_auto] sm:items-start sm:gap-6">
        <div class="grid gap-2">
          <h3 class="text-sm font-medium">Rebuild</h3>
          <ConsequenceList :items="rebuildConsequences" />
        </div>
        <div>
          <Button variant="destructive" @click="rebuildOpen = true">
            <WrenchIcon />
            Rebuild
          </Button>
        </div>
      </div>

      <div class="grid gap-3 border-t pt-4 sm:grid-cols-[1fr_auto] sm:items-start sm:gap-6">
        <div class="grid gap-2">
          <h3 class="text-sm font-medium">Delete</h3>
          <ConsequenceList :items="deleteConsequences" />
        </div>
        <div>
          <Button variant="destructive" @click="deleteOpen = true">
            <Trash2Icon />
            Delete
          </Button>
        </div>
      </div>
    </section>
  </div>

  <ApplicationStatusDialog v-model:open="statusOpen" :application="application" @done="reload" />
  <RebuildApplicationDialog v-model:open="rebuildOpen" :application="application" />
  <DeleteApplicationDialog v-model:open="deleteOpen" :application="application" @deleted="onDeleted" />
</template>
