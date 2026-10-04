<script setup lang="ts">
import { PencilIcon, PlusIcon, ServerIcon, Trash2Icon, TriangleAlertIcon } from '@lucide/vue'
import { computed, reactive, ref } from 'vue'
import ConfirmByNameDialog from '@/components/ConfirmByNameDialog.vue'
import ErrorState from '@/components/ErrorState.vue'
import FormDialog from '@/components/FormDialog.vue'
import FormField from '@/components/FormField.vue'
import LoadingState from '@/components/LoadingState.vue'
import PageHeader from '@/components/PageHeader.vue'
import ServerStatusDot from '@/components/ServerStatusDot.vue'
import StateMessage from '@/components/StateMessage.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { useAsync } from '@/composables/useAsync'
import { useMutation } from '@/composables/useMutation'
import { healthUrl } from '@/composables/useServerHealth'
import { api, unwrap } from '@/lib/api'
import type { Schemas } from '@/lib/api'
import { formErrors } from '@/lib/forms'
import * as toast from '@/lib/toast'

type Server = Schemas['ServerResponse']

const { data, error, loading, reload } = useAsync(() => unwrap(api.GET('/api/v1/admin/servers')))

// Create and edit share one dialog. `editing` is the server being changed; undefined when adding.
const formOpen = ref(false)
const editing = ref<Server>()
const form = reactive<Schemas['ServerRequest']>({ Name: '', ServerUrl: '' })

const save = useMutation(() =>
  editing.value
    ? unwrap(api.PUT('/api/v1/admin/servers/{id}', { params: { path: { id: editing.value.ID } }, body: form }))
    : unwrap(api.POST('/api/v1/admin/servers', { body: form })),
)
const errors = computed(() => formErrors(save.error.value, ['Name', 'ServerUrl']))

function openForm(server?: Server): void {
  editing.value = server
  form.Name = server?.Name ?? ''
  form.ServerUrl = server?.ServerUrl ?? ''
  save.reset()
  formOpen.value = true
}

async function submitForm(): Promise<boolean> {
  if (!(await save.run())) {
    return false
  }

  toast.success(editing.value ? 'Server saved.' : 'Server added.')
  void reload()
  return true
}

// `deleting` keeps its value after the dialog closes, so the text does not change while it fades out.
const deleteOpen = ref(false)
const deleting = ref<Server>()

const remove = useMutation((id: number) => unwrap(api.DELETE('/api/v1/admin/servers/{id}', { params: { path: { id } } })))

function openDelete(server: Server): void {
  deleting.value = server
  remove.reset()
  deleteOpen.value = true
}

async function submitDelete(): Promise<boolean> {
  if (!deleting.value || !(await remove.run(deleting.value.ID))) {
    return false
  }

  toast.success('Server deleted.')
  void reload()
  return true
}
</script>

<template>
  <PageHeader title="Servers" description="The Apilane API servers that host this instance's applications.">
    <Button @click="openForm()">
      <PlusIcon />
      Add server
    </Button>
  </PageHeader>

  <LoadingState v-if="loading" label="Loading servers" />

  <ErrorState v-else-if="error" :error="error" @retry="reload" />

  <StateMessage
    v-else-if="!data || data.Data.length === 0"
    :icon="ServerIcon"
    title="No servers yet"
    description="Register an API server before creating applications."
  >
    <Button @click="openForm()">
      <PlusIcon />
      Add server
    </Button>
  </StateMessage>

  <div v-else class="overflow-hidden rounded-lg border bg-card">
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead class="pl-4">Name</TableHead>
          <TableHead class="hidden sm:table-cell">Address</TableHead>
          <TableHead class="text-right">Applications</TableHead>
          <TableHead class="pr-4"><span class="sr-only">Actions</span></TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow v-for="server in data.Data" :key="server.ID">
          <TableCell class="pl-4 whitespace-normal">
            <div class="flex items-center gap-2.5">
              <ServerStatusDot :server-url="server.ServerUrl" />
              <a
                :href="healthUrl(server.ServerUrl)"
                target="_blank"
                rel="noopener"
                class="font-medium wrap-anywhere underline-offset-4 hover:underline sm:wrap-break-word"
              >
                {{ server.Name }}
                <span class="sr-only">(opens the health check in a new tab)</span>
              </a>
            </div>
            <!-- On a phone the address moves under the name, so the table fits the screen. -->
            <p class="mt-1 pl-[1.125rem] font-mono text-xs break-all text-muted-foreground sm:hidden">
              {{ server.ServerUrl }}
            </p>
          </TableCell>
          <TableCell class="hidden font-mono text-xs break-all whitespace-normal text-muted-foreground sm:table-cell">
            {{ server.ServerUrl }}
          </TableCell>
          <TableCell class="text-right">
            <Badge variant="secondary">{{ server.ApplicationCount }}</Badge>
          </TableCell>
          <TableCell class="w-px pr-4">
            <div class="flex justify-end gap-1">
              <Button variant="ghost" size="icon-sm" title="Edit" @click="openForm(server)">
                <PencilIcon />
                <span class="sr-only">Edit {{ server.Name }}</span>
              </Button>
              <Button variant="ghost" size="icon-sm" title="Delete" @click="openDelete(server)">
                <Trash2Icon />
                <span class="sr-only">Delete {{ server.Name }}</span>
              </Button>
            </div>
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>
  </div>

  <FormDialog
    v-model:open="formOpen"
    :title="editing ? 'Edit server' : 'Add server'"
    :submit-label="editing ? 'Save' : 'Add server'"
    :submit="submitForm"
    :error="errors.message"
  >
    <FormField v-slot="{ field }" label="Name" help="You can change the name at any time." :error="errors.fields.Name">
      <Input v-model="form.Name" v-bind="field" autocomplete="off" />
    </FormField>

    <FormField
      v-slot="{ field }"
      label="Address"
      help="The base URL of the API server, for example https://api.example.com. You can change it later, but if it is wrong the applications on this server stop working."
      :error="errors.fields.ServerUrl"
    >
      <Input v-model="form.ServerUrl" v-bind="field" type="url" autocomplete="off" spellcheck="false" />
    </FormField>

    <div
      v-if="editing"
      class="flex gap-2.5 rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive"
    >
      <TriangleAlertIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      <p>
        Changing the address takes effect at once for every application on this server. Leave it as it is unless you
        are sure.
      </p>
    </div>
  </FormDialog>

  <ConfirmByNameDialog
    v-if="deleting"
    v-model:open="deleteOpen"
    :title="`Delete server ${deleting.Name}?`"
    description="This cannot be undone."
    :name="deleting.Name"
    :action="submitDelete"
    :error="remove.error.value?.message"
  />
</template>
