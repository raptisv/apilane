<script setup lang="ts">
import { BotIcon, PlusIcon, ShieldCheckIcon, Trash2Icon, TriangleAlertIcon, UsersIcon } from '@lucide/vue'
import { computed, ref, watch } from 'vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import ErrorState from '@/components/ErrorState.vue'
import FormDialog from '@/components/FormDialog.vue'
import FormField from '@/components/FormField.vue'
import LoadingState from '@/components/LoadingState.vue'
import PageHeader from '@/components/PageHeader.vue'
import StateMessage from '@/components/StateMessage.vue'
import SharingHelp from '@/components/help/SharingHelp.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { useApplication } from '@/composables/useApplication'
import { useApplications } from '@/composables/useApplications'
import { useAsync } from '@/composables/useAsync'
import { useMutation } from '@/composables/useMutation'
import { accountDisplayName, isAgent } from '@/lib/agents'
import { api, unwrap } from '@/lib/api'
import type { Schemas } from '@/lib/api'
import { agentPermissionsSummary, copyAgentPermissions, readOnlyAgentPermissions } from '@/lib/agentPermissions'
import type { AgentPermissionGrant } from '@/lib/agentPermissions'
import { formErrors } from '@/lib/forms'
import * as toast from '@/lib/toast'
import AgentPermissionsEditor from './AgentPermissionsEditor.vue'

type Collaborator = Schemas['CollaboratorResponse']

// Owner only: the route carries meta.requiresOwner, so AppLayout shows the Forbidden state to a
// collaborator and this screen never loads for one.
const { application } = useApplication()
const { reload: reloadApplications } = useApplications()

// AppLayout gives every application a fresh screen, so the token is fixed for the life of this one.
const appToken = application.value.Token

const { data, error, loading, reload } = useAsync(() =>
  unwrap(api.GET('/api/v1/applications/{appToken}/collaborators', { params: { path: { appToken } } })),
)

// Only an existing agent returned by this list can be selected. Its name is shown, while its
// account address stays the identifier sent to the API.
const { data: agents, error: agentsError, loading: agentsLoading, reload: reloadAgents } = useAsync(() =>
  unwrap(api.GET('/api/v1/applications/{appToken}/collaborators/available-agents', { params: { path: { appToken } } })),
)
const availableAgents = computed(() => agents.value?.Data.filter((agent) => isAgent(agent.Email)) ?? [])

const { data: permissionCatalogue, error: permissionError, loading: permissionsLoading, reload: reloadPermissions } = useAsync(() =>
  unwrap(api.GET('/api/v1/applications/{appToken}/permissions', { params: { path: { appToken } } })),
)

// Every change also reloads the applications list, so 'Shared with N' on the cards is right.
function afterChange(): void {
  void reload()
  void reloadAgents()
  void reloadApplications()
}

// Share
const shareOpen = ref(false)
const email = ref('')
const personEmailError = ref<string>()
const agentShareOpen = ref(false)
const selectedAgentEmail = ref('')
const selectedAgent = computed(() => availableAgents.value.find((agent) => agent.Email === selectedAgentEmail.value))
const agentSelectionError = ref<string>()
const sharePermissions = ref<AgentPermissionGrant[]>([])

// useMutation does not hand back the answer; the toast needs to know whether the mail went out.
let added: Schemas['CollaboratorAddedResponse'] | undefined

const share = useMutation(async (recipientEmail: string, permissions?: AgentPermissionGrant[]) => {
  added = await unwrap(
    api.POST('/api/v1/applications/{appToken}/collaborators', {
      params: { path: { appToken } },
      body: {
        Email: recipientEmail,
        Permissions: permissions,
      },
    }),
  )
})
const shareErrors = computed(() => formErrors(share.error.value, ['Email', 'Permissions']))

watch(email, () => (personEmailError.value = undefined))
watch(selectedAgentEmail, () => (agentSelectionError.value = undefined))
watch(availableAgents, (list) => {
  if (!list.some((agent) => agent.Email === selectedAgentEmail.value)) {
    selectedAgentEmail.value = ''
  }
})

function openShare(): void {
  email.value = ''
  personEmailError.value = undefined
  share.reset()
  shareOpen.value = true
}

function openAgentShare(): void {
  selectedAgentEmail.value = ''
  agentSelectionError.value = undefined
  sharePermissions.value = readOnlyAgentPermissions(permissionCatalogue.value?.Resources ?? [])
  share.reset()
  agentShareOpen.value = true
}

async function submitShare(): Promise<boolean> {
  if (isAgent(email.value.trim())) {
    personEmailError.value = 'Use Share with agent to select an agent by name.'
    return false
  }
  if (!(await share.run(email.value))) {
    return false
  }
  return afterShared()
}

async function submitAgentShare(): Promise<boolean> {
  const agent = selectedAgent.value
  if (!agent || agentsLoading.value || agentsError.value) {
    agentSelectionError.value = 'Select an available agent.'
    return false
  }
  if (!(await share.run(agent.Email, permissionCatalogue.value ? sharePermissions.value : undefined))) {
    return false
  }
  return afterShared()
}

function afterShared(): boolean {
  if (!added) {
    return false
  }

  // An agent is never mailed, and nobody has to be told.
  // NotificationSent = false: mail is not set up, or the mail could not be prepared. The owner has to
  // tell the user, so it is a warning. True only means the mail was handed over for sending.
  if (isAgent(added.Email)) {
    toast.success(`Shared with ${accountDisplayName(added.Email)}. You can edit this agent’s rights later from Sharing.`)
  } else if (added.NotificationSent) {
    toast.success(`Shared with ${added.Email}. A notification email was sent.`)
  } else {
    toast.warning(
      `Shared with ${added.Email}. No email was sent: mail is not set up on this instance, or the mail could not be prepared.`,
    )
  }
  afterChange()
  return true
}

// Only the owner can edit an agent's policy. The API applies it to the agent's next request.
const permissionsOpen = ref(false)
const editing = ref<Collaborator>()
const editPermissions = ref<AgentPermissionGrant[]>([])
const savePermissions = useMutation((id: number) =>
  unwrap(api.PUT('/api/v1/applications/{appToken}/collaborators/{id}/permissions', {
    params: { path: { appToken, id } },
    body: { Permissions: editPermissions.value },
  })),
)
const permissionErrors = computed(() => formErrors(savePermissions.error.value, ['Permissions']))

function openPermissions(collaborator: Collaborator): void {
  editing.value = collaborator
  editPermissions.value = copyAgentPermissions(permissionCatalogue.value?.Resources ?? [], collaborator.Permissions ?? [])
  savePermissions.reset()
  permissionsOpen.value = true
}

// If a dialog was opened before the catalogue arrived, initialize it once the read succeeds.
watch(permissionCatalogue, (catalogue) => {
  if (!catalogue) {
    return
  }
  if (agentShareOpen.value && sharePermissions.value.length === 0) {
    sharePermissions.value = readOnlyAgentPermissions(catalogue.Resources)
  }
  if (permissionsOpen.value && editing.value && editPermissions.value.length === 0) {
    editPermissions.value = copyAgentPermissions(catalogue.Resources, editing.value.Permissions ?? [])
  }
})

async function submitPermissions(): Promise<boolean> {
  if (!editing.value || !permissionCatalogue.value || !(await savePermissions.run(editing.value.ID))) {
    return false
  }
  toast.success(`Updated the rights of ${accountDisplayName(editing.value.Email)}.`)
  await reload()
  return true
}

// Remove. `removing` keeps its value after the dialog closes, so the text does not change while it fades out.
const removeOpen = ref(false)
const removing = ref<Collaborator>()

const remove = useMutation((id: number) =>
  unwrap(api.DELETE('/api/v1/applications/{appToken}/collaborators/{id}', { params: { path: { appToken, id } } })),
)

function openRemove(collaborator: Collaborator): void {
  removing.value = collaborator
  remove.reset()
  removeOpen.value = true
}

async function submitRemove(): Promise<boolean> {
  if (!removing.value || !(await remove.run(removing.value.ID))) {
    return false
  }

  toast.success(`Stopped sharing with ${accountDisplayName(removing.value.Email)}.`)
  // Wait for the list before the dialog closes: the row and its button are gone by then, so the
  // dialog moves focus to the main content instead of to a button that is about to disappear.
  void reloadAgents()
  void reloadApplications()
  await reload()
  return true
}
</script>

<template>
  <PageHeader title="Sharing" description="The people and agents who manage this application with you.">
    <SharingHelp />
    <Button variant="outline" @click="openAgentShare">
      <BotIcon />
      Share with agent
    </Button>
    <Button @click="openShare">
      <PlusIcon />
      Share
    </Button>
  </PageHeader>

  <div class="grid max-w-2xl gap-6">
    <LoadingState v-if="loading" label="Loading the users this application is shared with" />

    <ErrorState v-else-if="error" :error="error" @retry="reload" />

    <StateMessage
      v-else-if="!data || data.Data.length === 0"
      :icon="UsersIcon"
      title="Not shared yet"
      description="Share it to let another user of this instance manage it with you."
    >
      <Button @click="openShare">
        <PlusIcon />
        Share with another user
      </Button>
    </StateMessage>

    <section v-else aria-labelledby="shared-title" class="rounded-lg border bg-card">
      <h2 id="shared-title" class="border-b px-4 py-2.5 text-sm font-semibold">Shared with</h2>
      <ul class="divide-y">
        <li v-for="collaborator in data.Data" :key="collaborator.ID" class="flex flex-wrap items-center gap-2 py-3 pr-2 pl-4">
          <div class="min-w-0 flex-1 basis-40">
            <p v-if="isAgent(collaborator.Email)" class="text-sm wrap-anywhere">{{ accountDisplayName(collaborator.Email) }}</p>
            <a
              v-else
              :href="`mailto:${collaborator.Email}`"
              class="rounded-sm text-sm wrap-anywhere underline-offset-4 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            >
              {{ collaborator.Email }}
              <span class="sr-only">(write an email)</span>
            </a>
            <p class="mt-1 text-xs text-muted-foreground">
              {{ isAgent(collaborator.Email) ? agentPermissionsSummary(collaborator.Permissions) : 'Full access, except sharing' }}
            </p>
          </div>
          <div class="flex items-center gap-1">
            <Button v-if="isAgent(collaborator.Email)" variant="outline" size="sm" @click="openPermissions(collaborator)">
              <ShieldCheckIcon />
              Edit rights<span class="sr-only"> of {{ accountDisplayName(collaborator.Email) }}</span>
            </Button>
            <Button variant="ghost" size="icon-sm" title="Stop sharing" @click="openRemove(collaborator)">
              <Trash2Icon />
              <span class="sr-only">Stop sharing with {{ accountDisplayName(collaborator.Email) }}</span>
            </Button>
          </div>
        </li>
      </ul>
    </section>
  </div>

  <FormDialog
    v-model:open="shareOpen"
    title="Share with another user"
    submit-label="Share"
    :submit="submitShare"
    :error="shareErrors.message"
  >
    <FormField
      v-slot="{ field }"
      label="User email"
      help="The address the person signs in with, written exactly as in their account, letter case included."
      :error="personEmailError ?? shareErrors.fields.Email"
    >
      <Input v-model="email" v-bind="field" type="email" autocomplete="off" spellcheck="false" />
    </FormField>

    <div class="flex gap-2.5 rounded-lg border border-warning/40 bg-warning/10 p-3 text-sm text-warning">
      <TriangleAlertIcon class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      <p>
        The user gets full access to modify the application, even delete it. When mail is set up on this instance, the
        user is notified by email.
      </p>
    </div>
  </FormDialog>

  <FormDialog
    v-model:open="agentShareOpen"
    title="Share with agent"
    description="Select an existing agent and choose its access to this application."
    submit-label="Share"
    :submit="submitAgentShare"
    :error="shareErrors.message"
    wide
  >
    <FormField v-slot="{ field }" label="Agent" :error="agentSelectionError ?? shareErrors.fields.Email">
      <Select v-model="selectedAgentEmail" :disabled="agentsLoading || !!agentsError || availableAgents.length === 0">
        <SelectTrigger v-bind="field" aria-label="Agent" class="w-full">
          <SelectValue placeholder="Choose an agent" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="agent in availableAgents" :key="agent.Email" :value="agent.Email">
            {{ accountDisplayName(agent.Email) }}
          </SelectItem>
        </SelectContent>
      </Select>
      <LoadingState v-if="agentsLoading" label="Loading available agents" />
      <ErrorState v-else-if="agentsError" :error="agentsError" @retry="reloadAgents" />
      <p v-else-if="availableAgents.length === 0" role="status" class="text-sm text-muted-foreground">
        No agents available. An administrator can add one under Instance &gt; Agents. Agents already shared with this application are not listed.
      </p>
    </FormField>

    <section v-if="selectedAgent" aria-labelledby="agent-other-apps" class="grid gap-2 rounded-lg border bg-muted/30 p-3">
      <h3 id="agent-other-apps" class="text-sm font-medium">Other applications</h3>
      <p class="text-xs text-muted-foreground">Only applications that both you and this agent can access are listed.</p>
      <ul v-if="selectedAgent.Applications.length > 0" class="list-disc space-y-1 pl-5 text-sm">
        <li v-for="other in selectedAgent.Applications" :key="other.Token" class="wrap-anywhere">{{ other.Name }}</li>
      </ul>
      <p v-else class="text-sm text-muted-foreground">This agent has no access to other applications you can see.</p>
    </section>

    <p class="text-sm text-muted-foreground">
      Agents start with read-only access. Choose their rights below; you can edit this agent’s rights later from Sharing.
      No email is sent to an agent.
    </p>
    <LoadingState v-if="permissionsLoading" label="Loading agent rights" />
    <template v-else-if="permissionError">
      <ErrorState :error="permissionError" @retry="reloadPermissions" />
      <p class="text-sm text-muted-foreground">You can still share now with the default read-only access and edit the rights later.</p>
    </template>
    <AgentPermissionsEditor v-else-if="permissionCatalogue" v-model="sharePermissions" :resources="permissionCatalogue.Resources" />
    <p v-if="shareErrors.fields.Permissions" role="alert" class="text-sm text-destructive">{{ shareErrors.fields.Permissions }}</p>
  </FormDialog>

  <FormDialog
    v-if="editing"
    v-model:open="permissionsOpen"
    title="Edit agent rights"
    description="These rights apply only to this application. Changes take effect on the agent’s next request."
    :submit="submitPermissions"
    :error="permissionErrors.message"
    wide
  >
    <p class="text-sm wrap-anywhere">{{ accountDisplayName(editing.Email) }}</p>
    <LoadingState v-if="permissionsLoading" label="Loading agent rights" />
    <ErrorState v-else-if="permissionError" :error="permissionError" @retry="reloadPermissions" />
    <AgentPermissionsEditor v-else-if="permissionCatalogue" v-model="editPermissions" :resources="permissionCatalogue.Resources" />
    <p v-if="permissionErrors.fields.Permissions" role="alert" class="text-sm text-destructive">{{ permissionErrors.fields.Permissions }}</p>
    <p class="text-xs text-muted-foreground">
      The agent can discover its current rights through the management API.
      Sharing, application deletion, encryption keys and instance administration remain unavailable to agents.
    </p>
  </FormDialog>

  <ConfirmDialog
    v-if="removing"
    v-model:open="removeOpen"
    :title="`Stop sharing with ${accountDisplayName(removing.Email)}?`"
    :description="`${accountDisplayName(removing.Email)} will lose access to this application. You can share the application with them again at any time.`"
    confirm-label="Stop sharing"
    destructive
    :action="submitRemove"
    :error="remove.error.value?.message"
  />
</template>
